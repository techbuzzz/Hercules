using Hercules.WasmSandbox.Compilation;

namespace Hercules.WasmSandbox;

/// <summary>
///     Входные параметры для WasmTool.ExecuteAsync.
/// </summary>
/// <param name="SourceCode">Исходный код (C#/Python/Rust) или готовый .wasm в hex/base64.</param>
/// <param name="Language">Язык: "csharp" | "python" | "rust" | "wasm".</param>
/// <param name="Args">CLI-аргументы, передаваемые в wasm-модуль.</param>
/// <param name="Limits">Resource limits (defaults = WasmResourceLimits.Default).</param>
public sealed record WasmToolRequest(
    string SourceCode,
    string Language,
    string[]? Args = null,
    WasmResourceLimits? Limits = null);

/// <summary>
///     Результат выполнения кода через WasmTool.
/// </summary>
public sealed record WasmToolResult(
    bool Compiled,
    string? CompilationError,
    WasmExecutionResult Execution,
    TimeSpan CompileDuration,
    TimeSpan TotalDuration);

/// <summary>
///     Human-in-the-loop gate для одобрения нового кода.
///     Реализация по умолчанию — auto-approve (для тестов); в проде подключается Telegram/UI.
/// </summary>
public interface ICodeApprovalGate
{
    /// <summary>
    ///     Спросить пользователя: можно ли запустить этот код?
    ///     Возвращает true если одобрено, false если отклонено.
    ///     Если null — выполнить auto-approve (для whitelisted, проверенных вызовов).
    /// </summary>
    Task<ApprovalDecision> RequestApprovalAsync(
        WasmToolRequest request,
        IReadOnlyList<string> previousApprovals,
        CancellationToken ct = default);
}

public enum ApprovalDecision
{
    Approved,
    Rejected,
    AutoApproved
}

/// <summary>
///     Высокоуровневый инструмент: компилирует исходник → выполняет в WASM sandbox.
///     Используется агентом через `tool.execute` action.
///     Соответствует ITool в Tools/ITool.cs, но здесь — отдельный namespace чтобы не ломать v2 build.
/// </summary>
public sealed class WasmTool
{
    public string Name => "execute_wasm";
    public string Description => "Execute code in a WebAssembly sandbox with capability-based isolation.";

    private readonly IWasmSandbox _sandbox;
    private readonly CompilerRegistry _compilers;
    private readonly ICodeApprovalGate _approval;
    private readonly Dictionary<string, byte[]> _wasmCache = new(StringComparer.OrdinalIgnoreCase);

    public WasmTool(IWasmSandbox sandbox, CompilerRegistry compilers, ICodeApprovalGate? approval = null)
    {
        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
        _compilers = compilers ?? throw new ArgumentNullException(nameof(compilers));
        _approval = approval ?? new AutoApproveGate();
    }

    public async Task<WasmToolResult> ExecuteAsync(WasmToolRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var totalSw = System.Diagnostics.Stopwatch.StartNew();
        var compileSw = new System.Diagnostics.Stopwatch();

        // 1. Human-in-the-loop approval
        var decision = await _approval.RequestApprovalAsync(request, _wasmCache.Keys.ToList(), ct);
        if (decision == ApprovalDecision.Rejected)
        {
            totalSw.Stop();
            return new WasmToolResult(
                Compiled: false,
                CompilationError: "User rejected execution",
                Execution: WasmExecutionResult.Failed("Rejected by user"),
                CompileDuration: TimeSpan.Zero,
                TotalDuration: totalSw.Elapsed);
        }

        // 2. Compile (если язык поддерживается) или passthrough (если уже wasm)
        compileSw.Start();
        IWasmCompiler? compiler = _compilers.Resolve(request.Language);
        if (compiler is null)
        {
            totalSw.Stop();
            return new WasmToolResult(
                Compiled: false,
                CompilationError: $"Unsupported language '{request.Language}'. Supported: {string.Join(", ", _compilers.Languages)}",
                Execution: WasmExecutionResult.Failed("Unsupported language"),
                CompileDuration: TimeSpan.Zero,
                TotalDuration: totalSw.Elapsed);
        }

        byte[] wasmBytes;
        try
        {
            // Cache by (language, source-hash) — для повторных запусков того же кода.
            var cacheKey = $"{request.Language}:{ComputeSourceHash(request.SourceCode)}";
            if (!_wasmCache.TryGetValue(cacheKey, out wasmBytes!))
            {
                wasmBytes = await compiler.CompileAsync(request.SourceCode, ct);
                _wasmCache[cacheKey] = wasmBytes;
            }
        }
        catch (CompilationException ex)
        {
            totalSw.Stop();
            compileSw.Stop();
            return new WasmToolResult(
                Compiled: false,
                CompilationError: ex.Message,
                Execution: WasmExecutionResult.Failed($"Compilation failed: {ex.Message}"),
                CompileDuration: compileSw.Elapsed,
                TotalDuration: totalSw.Elapsed);
        }
        compileSw.Stop();

        // 3. Execute in sandbox
        var execResult = await _sandbox.ExecuteAsync(
            new WasmExecutionRequest(wasmBytes, Args: request.Args, Limits: request.Limits),
            ct);

        totalSw.Stop();
        return new WasmToolResult(
            Compiled: true,
            CompilationError: null,
            Execution: execResult,
            CompileDuration: compileSw.Elapsed,
            TotalDuration: totalSw.Elapsed);
    }

    private static string ComputeSourceHash(string source)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(source);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..16];
    }

    /// <summary>
    ///     Default approval gate — auto-approve. Для тестов и CI.
    ///     В проде подключается ConsoleApprovalGate или TelegramApprovalGate.
    /// </summary>
    private sealed class AutoApproveGate : ICodeApprovalGate
    {
        public Task<ApprovalDecision> RequestApprovalAsync(
            WasmToolRequest request,
            IReadOnlyList<string> previousApprovals,
            CancellationToken ct = default)
        {
            return Task.FromResult(ApprovalDecision.AutoApproved);
        }
    }
}
