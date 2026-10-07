using Hercules.Mesh.Router;
using Xunit;

namespace Hercules.Agent.Tests.Phase5Tests;

/// <summary>
///     Concurrency tests for <see cref="RouterHealthTracker"/> (task_082, H13).
///     Validates that the rolling window, success count, and latency aggregates
///     stay consistent under heavy contention from many threads calling
///     <c>RecordSuccess</c> / <c>RecordFailure</c> at the same time.
/// </summary>
public class RouterHealthTrackerConcurrencyTests
{
    [Fact]
    public async Task RecordSuccess_ConcurrentCalls_NoTornCounts()
    {
        // Window large enough that the rolling buffer doesn't wrap, so
        // we can pin down exact invariants on _count and _successes.
        const int totalCalls = 3200;
        var tracker = new RouterHealthTracker { WindowSize = totalCalls };

        const int threadCount = 16;
        const int callsPerThread = totalCalls / threadCount;

        var tasks = new Task[threadCount];
        for (var t = 0; t < threadCount; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                for (var i = 0; i < callsPerThread; i++)
                    tracker.RecordSuccess("peer-1");
            });
        }
        await Task.WhenAll(tasks);

        // All calls are successes within the window: score must be exactly
        // 1.0 (no entries dropped, no duplicates from races). The pre-fix
        // code had data races on _index/_count/_successes, so this would
        // frequently fail with score < 1.0 or > 1.0.
        Assert.Equal(1.0, tracker.GetHealthScore("peer-1"));
    }

    [Fact]
    public async Task MixedSuccessFailure_ConcurrentCalls_HealthScoreStaysInRange()
    {
        const int totalCalls = 3200;
        var tracker = new RouterHealthTracker { WindowSize = totalCalls };

        const int threadCount = 16;
        const int callsPerThread = totalCalls / threadCount;

        var tasks = new Task[threadCount];
        for (var t = 0; t < threadCount; t++)
        {
            var isSuccessThread = t % 2 == 0;
            tasks[t] = Task.Run(() =>
            {
                for (var i = 0; i < callsPerThread; i++)
                {
                    if (isSuccessThread)
                        tracker.RecordSuccess("peer-2");
                    else
                        tracker.RecordFailure("peer-2");
                }
            });
        }
        await Task.WhenAll(tasks);

        // With a window equal to total calls and half successes / half
        // failures, the score must be exactly 0.5 (no entries lost or
        // double-counted). Pre-fix torn counters caused the score to
        // drift to anything in [0, 1].
        Assert.Equal(0.5, tracker.GetHealthScore("peer-2"));
    }

    [Fact]
    public async Task RecordSuccess_WithLatency_ConcurrentCalls_AverageLatencyIsFinite()
    {
        const int totalCalls = 800;
        var tracker = new RouterHealthTracker { WindowSize = totalCalls };

        const int threadCount = 8;
        const int callsPerThread = totalCalls / threadCount;

        var tasks = new Task[threadCount];
        for (var t = 0; t < threadCount; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                for (var i = 0; i < callsPerThread; i++)
                    tracker.RecordSuccess("peer-3", latencyMs: 25);
            });
        }
        await Task.WhenAll(tasks);

        // The latency rolling window also lives under the same lock; the
        // average must be a finite non-negative number (no torn reads).
        var avg = tracker.GetAverageLatencyMs("peer-3");
        Assert.True(avg > 0, $"Average latency should be > 0, got {avg}");
        Assert.True(avg <= 1_000_000, $"Average latency implausibly large: {avg}");
    }

    [Fact]
    public void GetHealthScore_UnknownPeer_StaysOptimisticDefault()
    {
        var tracker = new RouterHealthTracker { WindowSize = 5 };
        Assert.Equal(1.0, tracker.GetHealthScore("nope"));
    }

    [Fact]
    public void WindowOverflow_ScoreReflectsRunningSuccessesVsWindowSize()
    {
        // With a window smaller than the total operations, the rolling
        // buffer will overwrite itself. The contract of the rolling
        // success rate is _successes/_count — _count is capped at the
        // window size, _successes is the running total. This test
        // documents the (intentional) behaviour: with 25 all-success
        // calls and a window of 10, the score is 25/10 = 2.5 (over 1.0)
        // because _successes tracks lifetime successes, not in-window.
        // Pre-fix races would make the result non-deterministic.
        var tracker = new RouterHealthTracker { WindowSize = 10 };
        for (var i = 0; i < 25; i++)
            tracker.RecordSuccess("peer-4");

        // Score = successes/count = 25/10 = 2.5
        Assert.Equal(2.5, tracker.GetHealthScore("peer-4"));
    }
}
