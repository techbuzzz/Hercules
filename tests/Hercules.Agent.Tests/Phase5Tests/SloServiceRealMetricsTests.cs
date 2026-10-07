using Hercules.Audit;
using Hercules.Config;
using Hercules.Mesh.Observability;
using Hercules.Slo;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Phase5Tests;

/// <summary>
///     Unit tests for task_087 sub-tasks #2 and #3 — SloService consumes real
///     data from the latency tracker and the connectivity provider instead
///     of the previous synthetic extrapolation. These tests exercise the
///     EvaluateAsync flow with mocked collaborators so the response-time and
///     recovery-time paths are pinned against regression.
/// </summary>
public class SloServiceRealMetricsTests : IDisposable
{
    private readonly string _slosDir;
    private readonly SlosConfig _config;
    private readonly Mock<IAuditService> _audit = new();
    private readonly Mock<IMeshObservabilityService> _meshObs = new();
    private readonly SloLatencyTracker _tracker = new();
    private readonly StubConnectivity _connectivity = new();

    public SloServiceRealMetricsTests()
    {
        _slosDir = Path.Combine(Path.GetTempPath(), $"slo-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_slosDir);
        // Drop a minimal SLO definition so EvaluateAsync has a vertical to
        // score. The loader uses PropertyNameCaseInsensitive but does not
        // translate snake_case → PascalCase, so we use the property names
        // as defined on SloDefinition (PascalCase, no underscores).
        File.WriteAllText(
            Path.Combine(_slosDir, "test.slo.json"),
            """
            {
              "Vertical": "test",
              "Description": "Test vertical",
              "Version": "1.0",
              "AvailabilityTarget": 0.999,
              "ResponseTimeTargetMs": 200,
              "DataLossTargetPerDay": 0,
              "RecoveryTimeTargetMinutes": 5,
              "CostTargetUsd": 10.0
            }
            """);

        _config = new SlosConfig
        {
            Enabled = true,
            SlosDir = _slosDir,
            DefaultThresholds = new SlosDefaultThresholds
            {
                AvailabilityWarningPct = 95,
                AvailabilityCriticalPct = 90,
                ResponseTimeWarningMs = 250,
                ResponseTimeCriticalMs = 500,
                DataLossWarningPerDay = 10,
                DataLossCriticalPerDay = 100,
                RecoveryTimeWarningMinutes = 10,
                RecoveryTimeCriticalMinutes = 20,
                CostWarningPct = 80,
                CostCriticalPct = 100
            }
        };

        // The audit service is queried by both availability and (legacy)
        // recovery-time fallback. Default mock returns zero so the audit
        // heuristic does not interfere with the tracker-driven paths.
        _audit.Setup(a => a.GetActionStatsAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditActionStats(0, 0, 0, 0, 0));
    }

    public void Dispose()
    {
        try { Directory.Delete(_slosDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task EvaluateAsync_ResponseTime_UsesLatencyTracker()
    {
        // [task_087] When the latency tracker has samples, EvaluateAsync
        // reports the real P95 instead of the audit-heuristic fallback.
        for (var i = 10; i <= 250; i += 10)
        {
            _tracker.RecordSample("intents.any", i);
        }

        var svc = Build();
        var status = await svc.EvaluateAsync("test", CancellationToken.None);

        var rt = status.Objectives.Single(o => o.Objective == "response_time");
        // P95 of 25 samples [10,20,...,250] is the value at index 23 → 240.
        Assert.Equal(240, rt.CurrentValue);
        Assert.Contains("tracker", rt.BreachDescription);
    }

    [Fact]
    public async Task EvaluateAsync_ResponseTime_WithoutTracker_FallsBackToAuditHeuristic()
    {
        // [task_087] Legacy deployments that have not wired the tracker
        // (or the tracker is empty) must still produce a non-default value
        // when the audit log shows recent tool executions.
        _audit.Setup(a => a.GetActionStatsAsync(
                "tool_execution", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditActionStats(100, 0, 0, 0, 0));

        var svc = Build(includeTracker: false);
        var status = await svc.EvaluateAsync("test", CancellationToken.None);

        var rt = status.Objectives.Single(o => o.Objective == "response_time");
        // Audit heuristic: target=200, +500 cap → Min(target*1.1, target+500)
        // = Min(220, 700) ≈ 220. Allow a small float tolerance to avoid
        // 220 vs 220.00000000000003 false negatives.
        Assert.InRange(rt.CurrentValue, 219.9, 220.1);
        Assert.Contains("audit-heuristic", rt.BreachDescription);
    }

    [Fact]
    public async Task EvaluateAsync_RecoveryTime_UsesConnectivityProvider()
    {
        // [task_087] When the connectivity provider reports a real outage
        // duration, EvaluateAsync reports that instead of the
        // "any degradation → target" audit heuristic.
        _connectivity.SetOutage(TimeSpan.FromMinutes(7));

        var svc = Build();
        var status = await svc.EvaluateAsync("test", CancellationToken.None);

        var rec = status.Objectives.Single(o => o.Objective == "recovery_time");
        Assert.Equal(7, rec.CurrentValue);
        Assert.Contains("connectivity", rec.BreachDescription);
    }

    [Fact]
    public async Task EvaluateAsync_RecoveryTime_WithoutOutage_StaysAtZero()
    {
        // [task_087] No outage observed and audit empty → 0, severity Ok.
        _connectivity.SetOutage(TimeSpan.Zero);

        var svc = Build();
        var status = await svc.EvaluateAsync("test", CancellationToken.None);

        var rec = status.Objectives.Single(o => o.Objective == "recovery_time");
        Assert.Equal(0, rec.CurrentValue);
        Assert.Equal(SloSeverity.Ok, rec.Severity);
    }

    private SloService Build(bool includeTracker = true)
    {
        return new SloService(
            _config,
            _audit.Object,
            _meshObs.Object,
            outbox: null,
            budget: null,
            NullLogger<SloService>.Instance,
            includeTracker ? _tracker : null,
            _connectivity);
    }

    private sealed class StubConnectivity : IConnectivityStateProvider
    {
        private TimeSpan _outage = TimeSpan.Zero;
        public bool IsOnline => true;
        public TimeSpan LastOutageDuration => _outage;
        public void SetOutage(TimeSpan v) => _outage = v;
    }
}
