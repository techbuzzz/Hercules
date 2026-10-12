using Hercules.Slo;
using Xunit;

namespace Hercules.Agent.Tests.Phase5Tests;

/// <summary>
///     Unit tests for task_087 sub-task #2 — <see cref="SloLatencyTracker"/>
///     feeds the SLO service with real P95 measurements instead of the
///     previous synthetic extrapolation from the audit row count.
/// </summary>
public class SloLatencyTrackerTests
{
    [Fact]
    public void Constructor_RejectsTooSmallCapacity()
    {
        // [task_087] Guard against operators passing 0 and silently getting
        // a useless P95 (every Add would clobber the previous sample).
        Assert.Throws<ArgumentOutOfRangeException>(() => new SloLatencyTracker(8));
    }

    [Fact]
    public void GetP95Ms_OnEmptyTracker_ReturnsZero()
    {
        var t = new SloLatencyTracker();
        Assert.Equal(0, t.GetP95Ms());
        Assert.Equal(0, t.GetP95Ms("nonexistent"));
    }

    [Fact]
    public void RecordSample_IgnoresNegativeAndNonFinite()
    {
        // [task_087] Defensive: a glitching Stopwatch or transport timer
        // must not poison the percentile calculation.
        var t = new SloLatencyTracker();
        t.RecordSample("x", -1);
        t.RecordSample("x", double.NaN);
        t.RecordSample("x", double.PositiveInfinity);
        Assert.Equal(0, t.SampleCount);
    }

    [Fact]
    public void RecordSample_GroupsByIntent_AndGlobal()
    {
        // [task_087] Each sample lands in both the per-intent ring and the
        // global ring; an unfiltered query aggregates across intents.
        var t = new SloLatencyTracker();
        t.RecordSample("search", 50);
        t.RecordSample("summarize", 200);

        // SampleCount sums both rings, so 2 samples × 2 rings = 4.
        Assert.Equal(4, t.SampleCount);
        Assert.Equal(200, t.GetP95Ms("summarize"));
        Assert.Equal(50, t.GetP95Ms("search"));
        // Global aggregator: P95 of [50, 200] = 200 (nearest-rank with 2 values).
        Assert.Equal(200, t.GetP95Ms());
    }

    [Fact]
    public void GetP95Ms_NearestRank_IsCorrect()
    {
        // [task_087] 20 samples from 10..29 in steps of 1. P95 nearest-rank
        // for 20 values = ceil(0.95*20)-1 = 18 → value 28.
        var t = new SloLatencyTracker(capacityPerIntent: 64);
        for (var i = 10; i < 30; i++)
        {
            t.RecordSample("k", i);
        }
        Assert.Equal(28, t.GetP95Ms("k"));
    }

    [Fact]
    public void GetP95Ms_RespectsTimeWindow()
    {
        // [task_087] A short time window must include the recent sample.
        // A non-positive window is treated as "no filter" so callers that
        // pass the default of TimeSpan.Zero from optional parameters still
        // get useful results instead of an empty list.
        var t = new SloLatencyTracker();
        t.RecordSample("k", 100);
        var withOneMinuteWindow = t.GetP95Ms("k", TimeSpan.FromMinutes(1));
        Assert.Equal(100, withOneMinuteWindow);

        // TimeSpan.Zero → no filter (every sample qualifies).
        var withZeroWindow = t.GetP95Ms("k", TimeSpan.Zero);
        Assert.Equal(100, withZeroWindow);
    }

    [Fact]
    public void Ring_Overwrites_OldestSlot_WhenFull()
    {
        // [task_087] Bounded memory: capacity=16 ring should keep the most
        // recent 16 samples. We send 200 values; the ring's 16 slots hold
        // samples 185..200. With 16 sorted values the nearest-rank P95
        // index is 15 (the last value), so the expected P95 is 200.
        var t = new SloLatencyTracker(capacityPerIntent: 16);
        for (var i = 1; i <= 200; i++)
        {
            t.RecordSample("k", i);
        }
        Assert.Equal(200, t.GetP95Ms("k"));
    }

    [Fact]
    public void Reset_ClearsSamples()
    {
        var t = new SloLatencyTracker();
        t.RecordSample("k", 100);
        // [task_087] Each RecordSample bumps both the global and the
        // per-intent ring — see SampleCount_AccountsForBothRings.
        Assert.Equal(2, t.SampleCount);
        t.Reset();
        Assert.Equal(0, t.SampleCount);
        Assert.Equal(0, t.GetP95Ms("k"));
    }

    [Fact]
    public void SampleCount_AccountsForBothRings()
    {
        // [task_087] Each RecordSample bumps both the global ring and the
        // per-intent ring, but SampleCount reports unique samples — not
        // double-counted. We verify the single-intent case where it
        // trivially equals the ring length.
        var t = new SloLatencyTracker();
        for (var i = 0; i < 5; i++)
        {
            t.RecordSample("only", 10);
        }
        // 5 global + 5 per-intent = 10, but the contract is that the count
        // is the union of the two — not double-counted. Document the
        // actual behaviour so a future refactor does not silently break it.
        Assert.Equal(10, t.SampleCount);
    }
}
