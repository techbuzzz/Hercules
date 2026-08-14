namespace Hercules.Mesh.Dashboard;

/// <summary>
///     DTO-модели для mesh dashboard (task_053).
/// </summary>

// ---- Topology ----

public sealed record MeshTopologyDto(
    int AgentCount,
    IReadOnlyList<MeshAgentDto> Agents,
    DateTime GeneratedAt);

public sealed record MeshAgentDto(
    string AgentId,
    string DisplayName,
    string Endpoint,
    double HealthScore,
    double LatencyMs,
    double QualityScore,
    string TrustLevel,
    DateTime? LastSeen,
    IReadOnlyList<string> Capabilities);

// ---- Traffic ----

public sealed record MeshTrafficDto(
    long TotalRequests,
    long FanOutRequests,
    long MeshDelegations,
    double AvgLatencyMs,
    long MeshRouterHits,
    long CircuitBreakerRejections,
    DateTime From,
    DateTime To);

// ---- Health ----

public sealed record MeshHealthDto(
    IReadOnlyList<MeshHealthEntryDto> Agents,
    int HealthyCount,
    int DegradedCount,
    int UnhealthyCount);

public sealed record MeshHealthEntryDto(
    string AgentId,
    string DisplayName,
    double HealthScore,
    string HealthStatus,
    string CircuitState,
    int ConsecutiveFailures,
    DateTime? LastSeen,
    double? AvgLatencyMs);

// ---- Policy Denials ----

public sealed record MeshPolicyDenialsDto(
    int Count,
    IReadOnlyList<MeshDenialEntryDto> Denials);

public sealed record MeshDenialEntryDto(
    long Id,
    string Actor,
    string Action,
    string? Details,
    string? PolicyDecision,
    DateTime CreatedAt);

// ---- Skill Heatmap ----

public sealed record MeshSkillHeatmapDto(
    int TotalSkills,
    IReadOnlyList<MeshSkillHeatmapEntryDto> Skills);

public sealed record MeshSkillHeatmapEntryDto(
    string SkillId,
    string SkillName,
    int TotalUses,
    double SuccessRate,
    int Version,
    string CreatedAt);

// ---- Eval Summary ----

public sealed record MeshEvalSummaryDto(
    int TotalRuns,
    int PassedRuns,
    int FailedRuns,
    IReadOnlyList<MeshEvalRunDto> RecentRuns);

public sealed record MeshEvalRunDto(
    string RunId,
    string ScenarioType,
    bool Passed,
    double Score,
    double SuccessRate,
    int AssertionsPassed,
    int AssertionsFailed,
    DateTime StartTimeUtc,
    long DurationMs);

// ---- Complete Dashboard ----

public sealed record MeshDashboardDto(
    MeshTopologyDto Topology,
    MeshTrafficDto Traffic,
    MeshHealthDto Health,
    MeshPolicyDenialsDto PolicyDenials,
    MeshSkillHeatmapDto SkillHeatmap,
    MeshEvalSummaryDto EvalSummary,
    DateTime GeneratedAt);
