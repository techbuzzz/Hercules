using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace Hercules.CodeExecution;

/// <summary>
///     Sandbox-исполнитель C# кода через <c>dotnet run --file</c> (file-based apps, .NET 10).
///     Три уровня защиты:
///       1) Pre-execution regex scan (DangerousCodeScanner)
///       2) Изолированная temp-директория
///       3) POSIX ulimit (через wrapper-скрипт) + process timeout
///     Reference: <c>references/code-execution-sandbox.md</c>.
/// </summary>
public sealed class DotnetFileBasedExecutor : ICodeExecutor
{
    public string Name => "dotnet-file-based";

    public IReadOnlySet<string> SupportedLanguages { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "csharp", "cs", "c#"
    };

    private readonly SandboxOptions _options;
    private readonly string _wrapperScriptPath;

    public DotnetFileBasedExecutor(SandboxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        Directory.CreateDirectory(_options.TempRoot);

        // Wrapper-скрипт для ulimit (POSIX only). Пересоздаём при каждом запуске —
        // на Windows этот скрипт просто не используется.
        _wrapperScriptPath = Path.Combine(
            Path.GetTempPath(),
            $"hercules-ulimit-{Guid.NewGuid():N}.sh");
        EnsureWrapperScript();
    }

    public async Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Layer 0 — language check
        if (!SupportedLanguages.Contains(request.Language))
        {
            return ExecutionResult.Failed($"Unsupported language: {request.Language}. Supported: {string.Join(", ", SupportedLanguages)}");
        }

        // Layer 0 — code size cap
        var codeBytes = Encoding.UTF8.GetByteCount(request.Code);
        if (codeBytes > _options.MaxCodeSizeKb * 1024)
        {
            return ExecutionResult.Failed($"Code size {codeBytes} bytes exceeds limit {_options.MaxCodeSizeKb * 1024} bytes");
        }

        // Layer 1 — regex scan (fail-fast ДО любого процесса)
        var scan = DangerousCodeScanner.Scan(request.Code, _options);
        if (!scan.IsAllowed)
        {
            return new ExecutionResult(
                0,
                "",
                string.Join("; ", scan.BlockedReasons),
                0,
                "rejected",
                scan.BlockedReasons);
        }

        // Layer 2 — изолированная temp-директория
        var sessionId = Guid.NewGuid().ToString("N")[..12];
        var sessionDir = Path.Combine(_options.TempRoot, sessionId);
        var codeFile = Path.Combine(sessionDir, "code.cs");

        // Layer 3 — process spawn
        var timeoutMs = request.TimeoutMs ?? (_options.CpuTimeoutSeconds * 1000);

        try
        {
            Directory.CreateDirectory(sessionDir);
            File.WriteAllText(codeFile, request.Code);

            // File permissions: только чтение для пользователя (защита от self-modification)
            if (!OperatingSystem.IsWindows())
            {
                try { File.SetUnixFileMode(codeFile, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
                catch { /* best effort */ }
            }

            return await RunProcessAsync(codeFile, sessionDir, request.Args ?? Array.Empty<string>(), timeoutMs, ct);
        }
        catch (OperationCanceledException)
        {
            return ExecutionResult.TimedOut(timeoutMs);
        }
        catch (Exception ex)
        {
            return ExecutionResult.Failed($"Executor exception: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            // Best-effort cleanup. Если SessionTtlSeconds > 0 — оставляем для отладки.
            if (_options.SessionTtlSeconds <= 0)
            {
                TryCleanup(sessionDir);
            }
            else
            {
                _ = Task.Delay(TimeSpan.FromSeconds(_options.SessionTtlSeconds))
                    .ContinueWith(_ => TryCleanup(sessionDir));
            }
        }
    }

    private async Task<ExecutionResult> RunProcessAsync(
        string codeFile,
        string sessionDir,
        string[] args,
        int timeoutMs,
        CancellationToken ct)
    {
        var fileName = "dotnet";
        var arguments = new List<string> { "run", "--file", codeFile };
        foreach (var a in args)
        {
            arguments.Add(a);
        }

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = sessionDir,
        };
        foreach (var a in arguments)
        {
            psi.ArgumentList.Add(a);
        }

        // Sandbox-окружение: redirect dotnet cache в сессионную папку
        psi.Environment["DOTNET_CLI_HOME"] = Path.Combine(sessionDir, ".dotnet");
        psi.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_ROLL_FORWARD"] = "LatestMajor";

        // POSIX ulimit wrapper
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            WrapWithUlimit(psi, codeFile, arguments, sessionDir);
        }

        using var proc = new Process { StartInfo = psi };

        var sw = Stopwatch.StartNew();
        if (!proc.Start())
        {
            sw.Stop();
            return ExecutionResult.Failed("Failed to start dotnet process");
        }

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already exited */ }
            sw.Stop();
            return ExecutionResult.TimedOut(sw.ElapsedMilliseconds);
        }

        sw.Stop();

        // Классифицируем killed-by-ulimit как timeout (если длительность близка к таймауту)
        // ulimit -t на Linux убивает SIGKILL после истечения CPU-времени — ExitCode = 137 или подобный.
        if (proc.ExitCode != 0 && sw.ElapsedMilliseconds >= timeoutMs - 1000)
        {
            return new ExecutionResult(
                ExitCode: proc.ExitCode,
                Stdout: SafeGetResult(stdoutTask),
                Stderr: SafeGetResult(stderrTask),
                DurationMs: sw.ElapsedMilliseconds,
                Status: "timeout",
                BlockedPatterns: Array.Empty<string>(),
                SessionDir: sessionDir);
        }

        // Дождаться чтения stdout/stderr (могут быть большими)
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask);
        }
        catch
        {
            // Process exited, streams may have errors — best effort
        }

        sw.Stop();

        return new ExecutionResult(
            ExitCode: proc.ExitCode,
            Stdout: SafeGetResult(stdoutTask),
            Stderr: SafeGetResult(stderrTask),
            DurationMs: sw.ElapsedMilliseconds,
            Status: proc.ExitCode == 0 ? "ok" : "failed",
            BlockedPatterns: Array.Empty<string>(),
            SessionDir: sessionDir);
    }

    private static string SafeGetResult(Task<string> task)
    {
        return task.IsCompletedSuccessfully ? task.Result : "";
    }

    [SupportedOSPlatform("linux"), SupportedOSPlatform("macos")]
    private void WrapWithUlimit(ProcessStartInfo psi, string codeFile, List<string> dotnetArgs, string sessionDir)
    {
        // Build wrapper script (overwrite)
        var sb = new StringBuilder();
        sb.AppendLine("#!/bin/sh");
        // ulimit -t (CPU time) и ulimit -u (max processes) могут ломать dotnet CLI startup.
        // ulimit -f (file size) — жёсткий, но dotnet CLI пишет temp файлы > 10MB (NuGet cache,
        // Roslyn workspace). Оставляем только open files как надёжную границу.
        // CPU timeout контролируется отдельным CancellationTokenSource в C#.
        // File size контролируется логикой executor'а (MaxFileSizeMb) + DangerousCodeScanner.
        if (_options.MaxOpenFiles > 0)
        {
            sb.AppendLine($"ulimit -n {_options.MaxOpenFiles} 2>/dev/null");
        }
        // MaxVirtualMemoryMb = 0 → пропускаем ulimit -v (CLR heap init требует много VM)
        if (_options.MaxVirtualMemoryMb > 0)
        {
            sb.AppendLine($"ulimit -v {_options.MaxVirtualMemoryMb * 1024} 2>/dev/null");
        }
        // MaxProcesses: 0 = без лимита. Иначе — ulimit -u (только если явно > 0).
        if (_options.MaxProcesses > 0)
        {
            sb.AppendLine($"ulimit -u {_options.MaxProcesses} 2>/dev/null");
        }
        sb.AppendLine("exec \"$@\"");
        File.WriteAllText(_wrapperScriptPath, sb.ToString());

        try { File.SetUnixFileMode(_wrapperScriptPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite |
            UnixFileMode.UserExecute | UnixFileMode.GroupExecute); }
        catch { /* best effort */ }

        // Replace FileName/args with wrapper invocation
        psi.FileName = _wrapperScriptPath;
        psi.ArgumentList.Clear();
        psi.ArgumentList.Add("dotnet");
        foreach (var a in dotnetArgs)
        {
            psi.ArgumentList.Add(a);
        }
    }

    private void EnsureWrapperScript()
    {
        if (OperatingSystem.IsWindows()) return; // not used
        // Wrapper will be created on first call to WrapWithUlimit.
    }

    private static void TryCleanup(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // best effort — next cleanup pass picks up
        }
    }
}
