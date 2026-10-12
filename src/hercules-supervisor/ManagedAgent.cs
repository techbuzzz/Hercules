using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Hercules.Supervisor;

/// <summary>
/// A single supervised child process: start, graceful stop, health probing and
/// crash detection.
/// </summary>
public sealed class ManagedAgent(AgentOptions options, ILogger logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private readonly object _stateLock = new();

    public AgentOptions Options { get; } = options;
    public string Name => Options.Name;

    /// <summary>
    /// How long to wait for the agent's port to be released before starting the
    /// replacement. Set from <see cref="SupervisorOptions.RestartGraceSeconds"/>.
    /// </summary>
    public int PortFreeTimeoutSeconds { get; set; } = 20;

    private int _consecutiveFailures;
    private DateTimeOffset _lastRestartAt = DateTimeOffset.MinValue;
    private int _restartCount;
    private int _crashCount;

    /// <summary>Set while the supervisor is deliberately restarting us, so the exit is not counted as a crash.</summary>
    public bool IsStopping { get; private set; }

    public AgentSnapshot Snapshot
    {
        get
        {
            lock (_stateLock)
            {
                var running = _process is { HasExited: false } p ? p : null;
                TimeSpan? uptime = null;
                if (running is not null)
                {
                    try
                    {
                        // Process.StartTime is local-kind; convert before comparing
                        // against UtcNow or the DateTimeOffset ctor throws.
                        uptime = DateTimeOffset.UtcNow - new DateTimeOffset(running.StartTime.ToUniversalTime());
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
                    {
                        // Accessing StartTime can fail for processes we do not own.
                        uptime = null;
                    }
                }

                return new AgentSnapshot(
                    Name,
                    running?.Id,
                    running is not null ? "running" : "stopped",
                    _restartCount,
                    _crashCount,
                    _consecutiveFailures,
                    _lastRestartAt == DateTimeOffset.MinValue ? null : _lastRestartAt,
                    uptime);
            }
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_stateLock) return _process is { HasExited: false };
        }
    }

    public async Task<bool> StartAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            lock (_stateLock)
            {
                if (_process is { HasExited: false }) return false;
            }

            var psi = new ProcessStartInfo
            {
                FileName = ResolveCommandPath(),
                WorkingDirectory = ResolveWorkingDirectory(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var arg in Options.Arguments) psi.ArgumentList.Add(arg);
            foreach (var (k, v) in Options.Environment) psi.Environment[k] = v;

            var proc = Process.Start(psi)
                       ?? throw new InvalidOperationException($"Failed to start '{Options.Name}': process did not start");

            // Drain the pipes. Without this a chatty agent fills the OS buffer and
            // deadlocks — and it preserves the output xterm.js used to show.
            _ = Task.Run(() => PumpAsync(proc.StandardOutput, ct), CancellationToken.None);
            _ = Task.Run(() => PumpAsync(proc.StandardError, ct), CancellationToken.None);

            proc.Exited += (_, _) =>
            {
                lock (_stateLock)
                {
                    if (!IsStopping) _crashCount++;
                }
                logger.LogWarning("[{Name}] exited unexpectedly with code {Code}", Name, SafeExitCode(proc));
            };

            lock (_stateLock)
            {
                _process = proc;
                _consecutiveFailures = 0;
            }

            logger.LogInformation("[{Name}] started (pid {Pid})", Name, proc.Id);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Stops the agent: asks the process to close, waits out the grace period, then
    /// kills the tree. Idempotent.
    /// </summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            Process? proc;
            lock (_stateLock)
            {
                IsStopping = true;
                proc = _process;
                _process = null;
            }
            if (proc is null) return;

            try
            {
                if (!proc.HasExited)
                {
                    // CloseMainWindow returns false for a console apphost with no
                    // window, so it is only worth trying, never worth relying on.
                    var closed = false;
                    try { closed = proc.CloseMainWindow(); } catch { /* no window */ }

                    if (closed)
                    {
                        // The wait times out by throwing OperationCanceledException —
                        // that is the expected "still running" signal, not a failure,
                        // and must not skip the Kill below.
                        using var graceCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        graceCts.CancelAfter(TimeSpan.FromSeconds(2));
                        try { await proc.WaitForExitAsync(graceCts.Token); }
                        catch (OperationCanceledException) { /* fall through to Kill */ }
                    }

                    if (!proc.HasExited)
                    {
                        proc.Kill(entireProcessTree: true);

                        // Kill returns once the signal is delivered. Starting the
                        // replacement before the old process has fully exited races
                        // on exclusive OS handles (its port, its log file) and the
                        // new process dies on boot. Wait it out.
                        using var killCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        killCts.CancelAfter(TimeSpan.FromSeconds(10));
                        try { await proc.WaitForExitAsync(killCts.Token); }
                        catch (OperationCanceledException)
                        {
                            logger.LogWarning("[{Name}] did not exit within 10s of kill", Name);
                        }
                    }
                }
                logger.LogInformation("[{Name}] stopped", Name);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{Name}] failed to stop cleanly", Name);
            }
            finally
            {
                proc.Dispose();
                lock (_stateLock) IsStopping = false;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Restarts the agent, honouring the cooldown to prevent restart storms.</summary>
    public async Task<bool> RestartAsync(string reason, TimeSpan cooldown, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastRestartAt < cooldown)
        {
            logger.LogWarning(
                "[{Name}] restart suppressed by cooldown ({Elapsed}s since last); reason={Reason}",
                Name, (int)(now - _lastRestartAt).TotalSeconds, reason);
            return false;
        }

        _lastRestartAt = now;
        logger.LogWarning("[{Name}] restarting: {Reason}", Name, reason);
        await StopAsync(ct);

        // The command may be a wrapper (e.g. `dotnet run`) whose real child owns
        // the listening socket. Killing the tree returns before the OS releases
        // the port, and the replacement then dies with "address already in use".
        await WaitForPortFreeAsync(ct, PortFreeTimeoutSeconds);

        var started = await StartAsync(ct);
        if (started)
        {
            _restartCount++;
            lock (_stateLock) _consecutiveFailures = 0;
        }
        return started;
    }

    /// <summary>
    /// Blocks until the agent's port can be bound, or the timeout elapses.
    /// Best-effort: a failure to determine the port just returns immediately.
    /// </summary>
    private async Task WaitForPortFreeAsync(CancellationToken ct, int timeoutSeconds)
    {
        if (!Uri.TryCreate(Options.HealthUrl, UriKind.Absolute, out var uri)) return;
        var port = uri.Port;
        if (port <= 0) return;

        // Bind the exact address the agent binds, not 0.0.0.0 — a wildcard bind
        // can succeed while the loopback listener is still up.
        System.Net.IPAddress ip;
        try
        {
            var candidates = await System.Net.Dns.GetHostAddressesAsync(uri.Host, ct);
            ip = candidates.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                 ?? candidates.FirstOrDefault()
                 ?? System.Net.IPAddress.Loopback;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        var attempt = 0;
        while (DateTimeOffset.UtcNow < deadline)
        {
            attempt++;
            try
            {
                var listener = new System.Net.Sockets.TcpListener(ip, port);
                listener.Start();
                listener.Stop();
                if (attempt > 1) logger.LogInformation("[{Name}] port {Port} free after {N} attempt(s)", Name, port, attempt);
                return;
            }
            catch (System.Net.Sockets.SocketException)
            {
                try { await Task.Delay(TimeSpan.FromMilliseconds(500), ct); }
                catch (OperationCanceledException) { return; }
            }
        }

        logger.LogError("[{Name}] port {Port} still in use after {Timeout}s; starting anyway", Name, port, timeoutSeconds);
    }

    /// <summary>
    /// Probes <see cref="AgentOptions.HealthUrl"/>. Returns null when healthy,
    /// otherwise the failure description.
    /// </summary>
    public async Task<string?> ProbeHealthAsync(HttpClient http, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Options.HealthUrl)) return null;
        if (!IsRunning)
        {
            lock (_stateLock) _consecutiveFailures++;
            return "process not running";
        }

        try
        {
            using var res = await http.GetAsync(Options.HealthUrl, ct);
            if (res.IsSuccessStatusCode)
            {
                lock (_stateLock) _consecutiveFailures = 0;
                return null;
            }
            var reason = $"health endpoint returned {(int)res.StatusCode}";
            lock (_stateLock) _consecutiveFailures++;
            return reason;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_stateLock) _consecutiveFailures++;
            return ex.Message;
        }
    }

    /// <summary>
    /// Absolute working directory. A relative <c>WorkingDirectory</c> in config is
    /// resolved against the supervisor's current directory, as the author expects.
    /// </summary>
    private string ResolveWorkingDirectory()
    {
        var dir = Options.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(dir)) return Environment.CurrentDirectory;
        return Path.IsPathRooted(dir) ? dir : Path.GetFullPath(dir);
    }

    /// <summary>
    /// Absolute command path.
    /// <para>
    /// Windows resolves a relative <c>FileName</c> against the *parent's* current
    /// directory, not <see cref="ProcessStartInfo.WorkingDirectory"/> — so a config
    /// like <c>Command: "app/bin/App.exe"</c> with <c>WorkingDirectory: "../agent"</c>
    /// silently fails with "cannot find the file specified". Resolve it ourselves.
    /// </para>
    /// </summary>
    private string ResolveCommandPath()
    {
        var command = Options.Command;
        if (Path.IsPathRooted(command)) return command;
        return Path.GetFullPath(Path.Combine(ResolveWorkingDirectory(), command));
    }

    public int ConsecutiveFailures
    {
        get { lock (_stateLock) return _consecutiveFailures; }
    }

    private static int? SafeExitCode(Process p)
    {
        try { return p.HasExited ? p.ExitCode : null; } catch { return null; }
    }

    private async Task PumpAsync(StreamReader reader, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;
                logger.LogInformation("[{Name}] {Line}", Name, line);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (IOException)
        {
            // Pipe closed as the child exited.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _gate.Dispose();
    }
}

public sealed record AgentSnapshot(
    string Name,
    int? Pid,
    string State,
    int RestartCount,
    int CrashCount,
    int ConsecutiveHealthFailures,
    DateTimeOffset? LastRestartAt,
    TimeSpan? Uptime);

/// <summary>Shape returned by the agent's <c>/api/system/restart-pending</c>.</summary>
public sealed record RestartPendingResponse(
    [property: JsonPropertyName("pending")] bool Pending,
    [property: JsonPropertyName("requestedAt")] DateTimeOffset? RequestedAt,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("requestedBy")] string? RequestedBy);

/// <summary>Shape returned by the agent's <c>/api/studio/session</c>.</summary>
public sealed record StudioSessionResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt);