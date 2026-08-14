using Hercules.Agent;
using Hercules.Audit;
using Hercules.Config;
using Hercules.Mesh.Eval;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Dashboard;

/// <summary>
///     Агрегатор данных для mesh dashboard (task_053).
///     Собирает topology, traffic, health, policy denials, skill heatmap, eval summary.
/// </summary>
public sealed class MeshDashboardService
{
    private readonly ICapabilityRegistryService _registry;
    private readonly CircuitBreaker _circuitBreaker;
    private readonly IAuditService _audit;
    private readonly WebApiAdapter _webApiAdapter;
    private readonly IMeshEvalRunner _evalRunner;
    private readonly ILogger<MeshDashboardService> _log;

    public MeshDashboardService(
        ICapabilityRegistryService registry,
        CircuitBreaker circuitBreaker,
        IAuditService audit,
        WebApiAdapter webApiAdapter,
        IMeshEvalRunner evalRunner,
        ILogger<MeshDashboardService> log)
    {
        _registry = registry;
        _circuitBreaker = circuitBreaker;
        _audit = audit;
        _webApiAdapter = webApiAdapter;
        _evalRunner = evalRunner;
        _log = log;
    }

    /// <summary>Возвращает полный dashboard за один запрос.</summary>
    public async Task<MeshDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var topology = await GetTopologyAsync(ct);
        var traffic = GetTrafficAsync();
        var health = await GetHealthAsync(ct);
        var denials = await GetPolicyDenialsAsync(ct);
        var skillHeatmap = GetSkillHeatmap();
        var evalSummary = await GetEvalSummaryAsync(ct);

        return new MeshDashboardDto(
            Topology: topology,
            Traffic: traffic,
            Health: health,
            PolicyDenials: denials,
            SkillHeatmap: skillHeatmap,
            EvalSummary: evalSummary,
            GeneratedAt: DateTime.UtcNow);
    }

    /// <summary>Agent topology из capability registry.</summary>
    public Task<MeshTopologyDto> GetTopologyAsync(CancellationToken ct = default)
    {
        var agents = _registry.ListAll();
        var now = DateTime.UtcNow;

        var entries = agents.Select(a =>
        {
            var healthScore = a.HealthStatus == "Healthy" ? 1.0
                : a.HealthStatus == "Unreachable" ? 0.0
                : 0.5;

            return new MeshAgentDto(
                AgentId: a.AgentId,
                DisplayName: a.DisplayName,
                Endpoint: a.Endpoint,
                HealthScore: healthScore,
                LatencyMs: a.LatencyHintMs,
                QualityScore: healthScore,
                TrustLevel: a.TrustLevel,
                LastSeen: DateTime.TryParse(a.LastSeen, out var ls) ? ls : null,
                Capabilities: []);
        }).ToList();

        return Task.FromResult(new MeshTopologyDto(
            AgentCount: entries.Count,
            Agents: entries,
            GeneratedAt: now));
    }

    /// <summary>Traffic metrics (24h window) — placeholder реализация, расширяется с observability.</summary>
    public MeshTrafficDto GetTrafficAsync()
    {
        // Traffic metrics collected via OTel counters (task_013 + task_065).
        // Returns current counters as of last scrape.
        var states = _circuitBreaker.GetAllStates();
        var rejectedCount = states.Values.Count(s => s == CircuitState.Open);

        return new MeshTrafficDto(
            TotalRequests: 0,
            FanOutRequests: 0,
            MeshDelegations: 0,
            AvgLatencyMs: 0,
            MeshRouterHits: 0,
            CircuitBreakerRejections: rejectedCount,
            From: DateTime.UtcNow.AddHours(-24),
            To: DateTime.UtcNow);
    }

    /// <summary>Per-agent health + circuit breaker states.</summary>
    public Task<MeshHealthDto> GetHealthAsync(CancellationToken ct = default)
    {
        var agents = _registry.ListAll();
        var cbStates = _circuitBreaker.GetAllStates();

        var entries = agents.Select(a =>
        {
            var cbState = cbStates.TryGetValue(a.AgentId, out var cs)
                ? cs.ToString().ToLowerInvariant()
                : "closed";

            var healthScore = a.HealthStatus == "Healthy" ? 1.0
                : a.HealthStatus == "Unreachable" ? 0.0
                : 0.5;

            var status = a.HealthStatus.ToLowerInvariant();

            return new MeshHealthEntryDto(
                AgentId: a.AgentId,
                DisplayName: a.DisplayName,
                HealthScore: healthScore,
                HealthStatus: status,
                CircuitState: cbState,
                ConsecutiveFailures: a.ConsecutiveFailures,
                LastSeen: DateTime.TryParse(a.LastSeen, out var ls) ? ls : null,
                AvgLatencyMs: a.LatencyHintMs);
        }).ToList();

        return Task.FromResult(new MeshHealthDto(
            Agents: entries,
            HealthyCount: entries.Count(e => e.HealthStatus == "healthy"),
            DegradedCount: entries.Count(e => e.HealthStatus == "degraded"),
            UnhealthyCount: entries.Count(e => e.HealthStatus == "unhealthy")));
    }

    /// <summary>Recent policy denials из audit log (last 50 entries with Denied policy decision).</summary>
    public async Task<MeshPolicyDenialsDto> GetPolicyDenialsAsync(CancellationToken ct = default)
    {
        try
        {
            var entries = await _audit.QueryAsync(
                result: "denied",
                limit: 50,
                ct: ct);

            // Also include trust admission denials
            var trustDenied = await _audit.QueryAsync(
                action: "trust_denied",
                limit: 50,
                ct: ct);

            var allDenials = entries.Concat(trustDenied)
                .OrderByDescending(e => e.CreatedAt)
                .DistinctBy(e => e.Id)
                .Take(50)
                .Select(e => new MeshDenialEntryDto(
                    Id: e.Id,
                    Actor: e.Actor,
                    Action: e.Action,
                    Details: e.Details,
                    PolicyDecision: e.PolicyDecision,
                    CreatedAt: e.CreatedAt))
                .ToList();

            return new MeshPolicyDenialsDto(Count: allDenials.Count, Denials: allDenials);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to query policy denials from audit log");
            return new MeshPolicyDenialsDto(Count: 0, Denials: []);
        }
    }

    /// <summary>Skill usage heatmap из локального реестра навыков.</summary>
    public MeshSkillHeatmapDto GetSkillHeatmap()
    {
        var skills = _webApiAdapter.ListSkills();

        var entries = skills.Select(s => new MeshSkillHeatmapEntryDto(
            SkillId: s.Id,
            SkillName: s.Name,
            TotalUses: s.TotalUses,
            SuccessRate: s.SuccessRate,
            Version: s.Version,
            CreatedAt: s.CreatedAt)).ToList();

        return new MeshSkillHeatmapDto(
            TotalSkills: entries.Count,
            Skills: entries);
    }

    /// <summary>Recent eval summary из mesh eval runner (last suite result).</summary>
    public async Task<MeshEvalSummaryDto> GetEvalSummaryAsync(CancellationToken ct = default)
    {
        try
        {
            var baseline = await _evalRunner.LoadBaselineAsync(ct);

            if (baseline == null)
            {
                return new MeshEvalSummaryDto(
                    TotalRuns: 0,
                    PassedRuns: 0,
                    FailedRuns: 0,
                    RecentRuns: []);
            }

            var recentRuns = baseline.Results
                .OrderByDescending(r => r.StartTimeUtc)
                .Take(20)
                .Select(r => new MeshEvalRunDto(
                    RunId: Guid.NewGuid().ToString("N")[..8],
                    ScenarioType: r.Type.ToString(),
                    Passed: r.Passed,
                    Score: r.Metrics.SuccessRate,
                    SuccessRate: r.Metrics.SuccessRate,
                    AssertionsPassed: r.Assertions.Count(a => a.Passed),
                    AssertionsFailed: r.Assertions.Count(a => !a.Passed),
                    StartTimeUtc: r.StartTimeUtc,
                    DurationMs: r.DurationMs))
                .ToList();

            return new MeshEvalSummaryDto(
                TotalRuns: recentRuns.Count,
                PassedRuns: recentRuns.Count(r => r.Passed),
                FailedRuns: recentRuns.Count(r => !r.Passed),
                RecentRuns: recentRuns);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to load eval baseline for dashboard");
            return new MeshEvalSummaryDto(
                TotalRuns: 0,
                PassedRuns: 0,
                FailedRuns: 0,
                RecentRuns: []);
        }
    }
}
