using Hercules.Agent;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Agent;

public class InFlightTrackerTests
{
    [Fact]
    public void Begin_IncrementsCounter()
    {
        var tracker = new InFlightTracker(NullLogger<InFlightTracker>.Instance);

        Assert.Equal(0, tracker.InFlightCount);
        using (tracker.Begin())
        {
            Assert.Equal(1, tracker.InFlightCount);
        }

        Assert.Equal(0, tracker.InFlightCount);
    }

    [Fact]
    public async Task WaitForEmptyAsync_FastPath_WhenEmpty()
    {
        var tracker = new InFlightTracker(NullLogger<InFlightTracker>.Instance);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ok = await tracker.WaitForEmptyAsync(TimeSpan.FromSeconds(5));
        sw.Stop();

        Assert.True(ok);
        // fast path should return almost immediately, not block for 5s
        Assert.True(sw.ElapsedMilliseconds < 100, $"expected fast return, took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task WaitForEmptyAsync_Resolves_WhenLastRequestCompletes()
    {
        var tracker = new InFlightTracker(NullLogger<InFlightTracker>.Instance);
        using var scope1 = tracker.Begin();
        using var scope2 = tracker.Begin();
        Assert.Equal(2, tracker.InFlightCount);

        // start the wait BEFORE finishing — it should resolve when we dispose
        var waitTask = tracker.WaitForEmptyAsync(TimeSpan.FromSeconds(5));

        scope2.Dispose();
        scope1.Dispose();

        var ok = await waitTask;
        Assert.True(ok);
        Assert.Equal(0, tracker.InFlightCount);
    }

    [Fact]
    public async Task WaitForEmptyAsync_TimesOut_WhenInFlightHangs()
    {
        var tracker = new InFlightTracker(NullLogger<InFlightTracker>.Instance);
        using var scope = tracker.Begin();
        Assert.Equal(1, tracker.InFlightCount);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ok = await tracker.WaitForEmptyAsync(TimeSpan.FromMilliseconds(300));
        sw.Stop();

        Assert.False(ok);
        Assert.True(sw.ElapsedMilliseconds >= 250, $"expected ~300ms wait, got {sw.ElapsedMilliseconds}ms");
        Assert.True(sw.ElapsedMilliseconds < 1500, $"expected ~300ms wait, got {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task WaitForEmptyAsync_Resolves_ForMultipleConcurrentWaiters()
    {
        var tracker = new InFlightTracker(NullLogger<InFlightTracker>.Instance);
        using var scope = tracker.Begin();

        var w1 = tracker.WaitForEmptyAsync(TimeSpan.FromSeconds(2));
        var w2 = tracker.WaitForEmptyAsync(TimeSpan.FromSeconds(2));
        var w3 = tracker.WaitForEmptyAsync(TimeSpan.FromSeconds(2));

        // dispose after a short delay so all three waits are registered
        await Task.Delay(50);
        scope.Dispose();

        var results = await Task.WhenAll(w1, w2, w3);
        Assert.All(results, ok => Assert.True(ok));
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotDoubleDecrement()
    {
        var tracker = new InFlightTracker(NullLogger<InFlightTracker>.Instance);
        var scope = tracker.Begin();
        Assert.Equal(1, tracker.InFlightCount);

        scope.Dispose();
        scope.Dispose();
        scope.Dispose();

        Assert.Equal(0, tracker.InFlightCount);
    }

    [Fact]
    public async Task WaitForEmptyAsync_AfterTimeout_StillSignalsWhenCounterHitsZeroLater()
    {
        // Important: a wait that times out must not "consume" the empty signal.
        // The next call should still observe the zero counter.
        var tracker = new InFlightTracker(NullLogger<InFlightTracker>.Instance);
        using var scope = tracker.Begin();

        var first = await tracker.WaitForEmptyAsync(TimeSpan.FromMilliseconds(100));
        Assert.False(first);

        scope.Dispose();

        var second = await tracker.WaitForEmptyAsync(TimeSpan.FromSeconds(2));
        Assert.True(second);
    }
}
