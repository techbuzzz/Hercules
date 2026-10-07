using Hercules.Mesh.Observability;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hercules.Agent.Tests.Phase4Tests.Observability;

/// <summary>
///     Unit tests for <see cref="MeshDiagnosticsService"/> (task_093).
///     Focus: counter increments, by-capability / by-peer maps, ring buffer
///     behavior (FIFO eviction, snapshot ordering, level filtering on logs).
/// </summary>
public sealed class MeshDiagnosticsServiceTests
{
    [Fact]
    public void RecordMetric_RoutingDecision_IncrementsCounterAndCapabilityMap()
    {
        var svc = new MeshDiagnosticsService();
        svc.RecordMetric("routing_decision", 1, intent: "read:user");
        svc.RecordMetric("routing_decision", 1, intent: "read:user");
        svc.RecordMetric("routing_decision", 1, intent: "write:file");

        Assert.Equal(3, svc.RoutingDecisionCount);
        Assert.Equal(2, svc.RoutingByCapability["read:user"]);
        Assert.Equal(1, svc.RoutingByCapability["write:file"]);
    }

    [Fact]
    public void RecordMetric_Delegation_IncrementsCounterAndPeerMap()
    {
        var svc = new MeshDiagnosticsService();
        svc.RecordMetric("delegation", 1, peerAgentId: "peer-a");
        svc.RecordMetric("delegation", 1, peerAgentId: "peer-a");
        svc.RecordMetric("delegation", 1, peerAgentId: "peer-b");

        Assert.Equal(3, svc.DelegationCount);
        Assert.Equal(2, svc.RoutingByPeer["peer-a"]);
        Assert.Equal(1, svc.RoutingByPeer["peer-b"]);
    }

    [Fact]
    public void RecordMetric_RetryAttempt_OnlyIncrementsRetryCounter()
    {
        var svc = new MeshDiagnosticsService();
        svc.RecordMetric("retry_attempt", 1, intent: "x");

        Assert.Equal(1, svc.RetryAttemptCount);
        Assert.Equal(0, svc.RoutingDecisionCount);
        Assert.Empty(svc.RoutingByCapability);
    }

    [Fact]
    public void RecordMetric_CircuitBreakerStateChange_IncrementsCounter()
    {
        var svc = new MeshDiagnosticsService();
        svc.RecordMetric("circuit_breaker_state_change", 1, peerAgentId: "peer-a", intent: "x");

        Assert.Equal(1, svc.CircuitBreakerStateChangeCount);
    }

    [Fact]
    public void RecordMetric_UnknownMetric_IsIgnored()
    {
        var svc = new MeshDiagnosticsService();
        svc.RecordMetric("delegation_latency_ms", 42.0);

        // None of the counters increment for histogram / unknown metrics.
        Assert.Equal(0, svc.RoutingDecisionCount);
        Assert.Equal(0, svc.RetryAttemptCount);
        Assert.Equal(0, svc.DelegationCount);
    }

    [Fact]
    public void RecordMetric_PeerCaseInsensitive_TreatedAsSamePeer()
    {
        var svc = new MeshDiagnosticsService();
        svc.RecordMetric("delegation", 1, peerAgentId: "PEER-A");
        svc.RecordMetric("delegation", 1, peerAgentId: "peer-a");

        Assert.Single(svc.RoutingByPeer);
        Assert.Equal(2, svc.RoutingByPeer["PEER-A"]);
    }

    [Fact]
    public void Snapshot_CapturesAllCounters()
    {
        var svc = new MeshDiagnosticsService();
        svc.RecordMetric("routing_decision", 1, intent: "c1");
        svc.RecordMetric("retry_attempt", 1);
        svc.RecordMetric("circuit_breaker_state_change", 1);
        svc.RecordMetric("delegation", 1, peerAgentId: "p1");
        svc.RecordMetric("mesh_backend_health", 1);

        var snap = svc.Snapshot();

        Assert.Equal(1, snap.RoutingDecisionCount);
        Assert.Equal(1, snap.RetryAttemptCount);
        Assert.Equal(1, snap.CircuitBreakerStateChangeCount);
        Assert.Equal(1, snap.DelegationCount);
        Assert.Equal(1, snap.MeshBackendHealthCount);
        Assert.True(snap.From <= snap.To);
        Assert.Equal(1, snap.ByCapability["c1"]);
        Assert.Equal(1, snap.ByPeer["p1"]);
    }

    [Fact]
    public void AppendTrace_NewestFirstOrdering()
    {
        var svc = new MeshDiagnosticsService();
        var t1 = new TraceSummary { TraceId = "t1", RootName = "op1", StartedAt = DateTimeOffset.UtcNow, DurationMs = 1, Status = "Ok", SpanCount = 1 };
        var t2 = new TraceSummary { TraceId = "t2", RootName = "op2", StartedAt = DateTimeOffset.UtcNow, DurationMs = 1, Status = "Ok", SpanCount = 1 };
        var t3 = new TraceSummary { TraceId = "t3", RootName = "op3", StartedAt = DateTimeOffset.UtcNow, DurationMs = 1, Status = "Ok", SpanCount = 1 };

        svc.AppendTrace(t1);
        svc.AppendTrace(t2);
        svc.AppendTrace(t3);

        var snap = svc.RecentTraces(10);
        Assert.Equal(3, snap.Count);
        Assert.Equal("t3", snap[0].TraceId);
        Assert.Equal("t2", snap[1].TraceId);
        Assert.Equal("t1", snap[2].TraceId);
    }

    [Fact]
    public void AppendTrace_OverCapacity_EvictsOldest()
    {
        var svc = new MeshDiagnosticsService();
        // Cap is MaxTraces (100). Push 105 traces; oldest 5 should be evicted.
        for (var i = 0; i < MeshDiagnosticsService.MaxTraces + 5; i++)
        {
            svc.AppendTrace(new TraceSummary
            {
                TraceId = $"t{i:D3}",
                RootName = "op",
                StartedAt = DateTimeOffset.UtcNow,
                DurationMs = 1,
                Status = "Ok",
                SpanCount = 1
            });
        }

        var snap = svc.RecentTraces(MeshDiagnosticsService.MaxTraces);
        Assert.Equal(MeshDiagnosticsService.MaxTraces, snap.Count);
        // Newest entry (t104) is at index 0; oldest retained (t005) is at the end.
        Assert.Equal($"t{MeshDiagnosticsService.MaxTraces + 4:D3}", snap[0].TraceId);
        Assert.Equal("t005", snap[^1].TraceId);
    }

    [Fact]
    public void RecentTraces_LimitClampsResult()
    {
        var svc = new MeshDiagnosticsService();
        for (var i = 0; i < 20; i++)
        {
            svc.AppendTrace(new TraceSummary
            {
                TraceId = $"t{i:D3}",
                RootName = "op",
                StartedAt = DateTimeOffset.UtcNow,
                DurationMs = 1,
                Status = "Ok",
                SpanCount = 1
            });
        }

        var snap = svc.RecentTraces(5);
        Assert.Equal(5, snap.Count);
        Assert.Equal("t019", snap[0].TraceId);
        Assert.Equal("t015", snap[4].TraceId);
    }

    [Fact]
    public void RecentTraces_ZeroLimit_ReturnsEmpty()
    {
        var svc = new MeshDiagnosticsService();
        svc.AppendTrace(new TraceSummary
        {
            TraceId = "t1", RootName = "op", StartedAt = DateTimeOffset.UtcNow, DurationMs = 1, Status = "Ok", SpanCount = 1
        });

        Assert.Empty(svc.RecentTraces(0));
    }

    [Fact]
    public void AppendLog_StoresEntry()
    {
        var svc = new MeshDiagnosticsService();
        svc.AppendLog(new LogEntrySummary
        {
            Timestamp = DateTimeOffset.UtcNow,
            Level = "Info",
            Source = "Hercules.Agent",
            Message = "hello",
            TraceId = "abc",
            RequestId = "req-1",
            StructuredFields = new Dictionary<string, object?> { ["k"] = "v" }
        });

        var snap = svc.RecentLogs(10);
        Assert.Single(snap);
        Assert.Equal("hello", snap[0].Message);
        Assert.Equal("abc", snap[0].TraceId);
        Assert.Equal("req-1", snap[0].RequestId);
        Assert.Equal("v", snap[0].StructuredFields["k"]);
    }

    [Fact]
    public void RecentLogs_FilterByMinLevel_ExcludesLowerLevels()
    {
        var svc = new MeshDiagnosticsService();
        svc.AppendLog(new LogEntrySummary { Timestamp = DateTimeOffset.UtcNow, Level = "Debug", Source = "s", Message = "m" });
        svc.AppendLog(new LogEntrySummary { Timestamp = DateTimeOffset.UtcNow, Level = "Info", Source = "s", Message = "m" });
        svc.AppendLog(new LogEntrySummary { Timestamp = DateTimeOffset.UtcNow, Level = "Warning", Source = "s", Message = "m" });
        svc.AppendLog(new LogEntrySummary { Timestamp = DateTimeOffset.UtcNow, Level = "Error", Source = "s", Message = "m" });

        var infoOrAbove = svc.RecentLogs(10, LogLevel.Information);
        Assert.Equal(3, infoOrAbove.Count);
        Assert.DoesNotContain(infoOrAbove, l => l.Level == "Debug");

        var warnings = svc.RecentLogs(10, LogLevel.Warning);
        Assert.Equal(2, warnings.Count);

        var errors = svc.RecentLogs(10, LogLevel.Error);
        Assert.Single(errors);
        Assert.Equal("Error", errors[0].Level);
    }

    [Fact]
    public void RecentLogs_UnknownLevelPassesFilter()
    {
        var svc = new MeshDiagnosticsService();
        svc.AppendLog(new LogEntrySummary { Timestamp = DateTimeOffset.UtcNow, Level = "Bizarre", Source = "s", Message = "m" });

        var snap = svc.RecentLogs(10, LogLevel.Warning);
        Assert.Single(snap);
    }

    [Fact]
    public void AppendLog_OverCapacity_EvictsOldest()
    {
        var svc = new MeshDiagnosticsService();
        for (var i = 0; i < MeshDiagnosticsService.MaxLogs + 5; i++)
        {
            svc.AppendLog(new LogEntrySummary
            {
                Timestamp = DateTimeOffset.UtcNow,
                Level = "Info",
                Source = "s",
                Message = $"m{i:D3}"
            });
        }

        var snap = svc.RecentLogs(MeshDiagnosticsService.MaxLogs);
        Assert.Equal(MeshDiagnosticsService.MaxLogs, snap.Count);
        Assert.Equal($"m{MeshDiagnosticsService.MaxLogs + 4:D3}", snap[0].Message);
        Assert.Equal("m005", snap[^1].Message);
    }

    [Fact]
    public void Constructor_StartedAtIsUtc()
    {
        var svc = new MeshDiagnosticsService();
        Assert.True((DateTimeOffset.UtcNow - svc.StartedAt).TotalSeconds < 5);
    }
}
