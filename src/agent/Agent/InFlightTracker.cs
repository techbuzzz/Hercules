using Microsoft.Extensions.Logging;

namespace Hercules.Agent;

/// <summary>
///     Lock-free in-flight tracker. The hot path (Begin / Dispose) only touches an
///     <see cref="Interlocked" /> counter; the cold path (<see cref="WaitForEmptyAsync" />) uses
///     a per-wait <see cref="TaskCompletionSource" /> protected by a short lock so the
///     completion signal survives late Begin/Dispose pairs.
///     Specification: task_080.
/// </summary>
public sealed class InFlightTracker : IInFlightTracker
{
    private readonly ILogger<InFlightTracker>? _logger;
    private int _count;
    private readonly object _signalLock = new();

    public InFlightTracker(ILogger<InFlightTracker>? logger = null)
    {
        _logger = logger;
    }

    public int InFlightCount => Volatile.Read(ref _count);

    public IDisposable Begin()
    {
        Interlocked.Increment(ref _count);
        return new Decrementer(this);
    }

    public Task<bool> WaitForEmptyAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        // Fast path: nothing in flight, return immediately.
        if (Volatile.Read(ref _count) == 0)
        {
            return Task.FromResult(true);
        }

        TaskCompletionSource tcs;
        lock (_signalLock)
        {
            // Re-check under lock to avoid the race where the last request
            // completed between our fast-path read and lock acquisition.
            if (Volatile.Read(ref _count) == 0)
            {
                return Task.FromResult(true);
            }

            tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        // Decrementer.NotifyEmpty() will TrySetResult(tcs) when count hits 0,
        // regardless of which TCS instance is current. We need to be careful: if
        // a later Begin() arrives, Decrementer may not see *this* TCS. To handle
        // that, NotifyEmpty() walks the chain via a linked list.
        // Simpler approach: when count hits 0, we set *all* registered TCSs.
        _pendingSignals.Enqueue(tcs);
        // Re-check: if the counter is now zero (because the last request finished
        // between lock release and queue.Enqueue), drain the queue ourselves.
        if (Volatile.Read(ref _count) == 0)
        {
            DrainPendingSignals();
        }

        return WaitForSignalAsync(tcs, timeout, ct);
    }

    private readonly System.Collections.Concurrent.ConcurrentQueue<TaskCompletionSource> _pendingSignals = new();

    private void NotifyEmpty()
    {
        DrainPendingSignals();
    }

    private void DrainPendingSignals()
    {
        // Only drain if counter is actually zero (caller checked).
        while (_pendingSignals.TryDequeue(out var tcs))
        {
            tcs.TrySetResult();
        }
    }

    private async Task<bool> WaitForSignalAsync(TaskCompletionSource tcs, TimeSpan timeout, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.InfiniteTimeSpan, timeoutCts.Token));
        if (completed == tcs.Task)
        {
            timeoutCts.Cancel();
            return true;
        }

        _logger?.LogWarning("[InFlightTracker] WaitForEmptyAsync timed out after {Timeout}", timeout);
        return false;
    }

    private sealed class Decrementer : IDisposable
    {
        private readonly InFlightTracker _owner;
        private int _disposed;

        public Decrementer(InFlightTracker owner) => _owner = owner;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            var newCount = Interlocked.Decrement(ref _owner._count);
            if (newCount == 0)
            {
                _owner.NotifyEmpty();
            }
        }
    }
}
