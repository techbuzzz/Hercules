using System.Diagnostics;

namespace Hercules.Agent.Loop;

/// <summary>
///     Combines an external <see cref="CancellationToken" /> with an optional wall-clock timeout,
///     exposing whether the cancellation was caused by the timeout specifically.
///     Implements <see cref="IAsyncDisposable" /> for auto-disposal of the internal TCS.
/// </summary>
public sealed class LinkedCancellationTokenSource : IDisposable
{
    private readonly CancellationTokenSource? _timeoutCts;
    private readonly CancellationTokenRegistration _externalReg;
    private readonly bool _ownsTimeoutCts;

    /// <summary>
    ///     The combined <see cref="CancellationToken" /> that fires on either
    ///     external cancellation or wall-clock timeout.
    /// </summary>
    public CancellationToken Token { get; }

    /// <summary>
    ///     True if the combined token was cancelled due to wall-clock timeout expiration.
    ///     False if cancelled by external token, or not cancelled yet.
    /// </summary>
    public bool IsWallClockTimeout { get; private set; }

    /// <summary>
    ///     Wall-clock duration of the timeout, or null if no timeout was set.
    /// </summary>
    public TimeSpan? Timeout { get; }

    /// <summary>
    ///     Elapsed time since this instance was created.
    /// </summary>
    public TimeSpan Elapsed => _stopwatch.Elapsed;

    private readonly Stopwatch _stopwatch;

    /// <summary>
    ///     Creates a linked CTS combining an external token with an optional wall-clock timeout.
    /// </summary>
    /// <param name="external">External cancellation token (e.g. request-level CT).</param>
    /// <param name="timeout">Wall-clock timeout duration. Null = no timeout limit.</param>
    public LinkedCancellationTokenSource(CancellationToken external, TimeSpan? timeout)
    {
        _stopwatch = Stopwatch.StartNew();

        if (timeout.HasValue && timeout.Value > TimeSpan.Zero)
        {
            Timeout = timeout.Value;
            _timeoutCts = new CancellationTokenSource(timeout.Value);
            _ownsTimeoutCts = true;
            Token = CancellationTokenSource.CreateLinkedTokenSource(external, _timeoutCts.Token).Token;
        }
        else
        {
            Timeout = null;
            _timeoutCts = null;
            _ownsTimeoutCts = false;
            Token = external;
        }

        // Propagate timeout cancellation to IsWallClockTimeout flag
        if (_timeoutCts is not null)
        {
            _externalReg = _timeoutCts.Token.Register(() => IsWallClockTimeout = true);
        }
    }

    /// <summary>
    ///     Creates a linked CTS with only an external token (no wall-clock timeout).
    /// </summary>
    public LinkedCancellationTokenSource(CancellationToken external)
    {
        _stopwatch = Stopwatch.StartNew();
        Timeout = null;
        _timeoutCts = null;
        _ownsTimeoutCts = false;
        Token = external;
    }

    public void Dispose()
    {
        _externalReg.Dispose();
        if (_ownsTimeoutCts)
        {
            _timeoutCts?.Dispose();
        }

        _stopwatch.Stop();
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
