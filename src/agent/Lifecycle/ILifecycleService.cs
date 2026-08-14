namespace Hercules.Lifecycle;

/// <summary>
///     Lifecycle management service for agents and skill packages.
///     Provides: inventory, start, stop, drain, update, canary, health check, rollback, decommissioning.
///     Specification: task_057.
/// </summary>
public interface ILifecycleService
{
    // ─── Agent inventory ────────────────────────────────────────────────────────

    /// <summary>Get all agents in the fleet (local + known peers).</summary>
    Task<IReadOnlyList<AgentInventoryEntry>> GetAgentInventoryAsync(CancellationToken ct = default);

    /// <summary>Get inventory of skill packages across the fleet.</summary>
    Task<IReadOnlyList<SkillPackageInventoryEntry>> GetSkillPackageInventoryAsync(CancellationToken ct = default);

    // ─── Agent lifecycle actions ───────────────────────────────────────────────

    /// <summary>Start a stopped agent (local or peer).</summary>
    Task<LifecycleActionResult> StartAgentAsync(string agentId, CancellationToken ct = default);

    /// <summary>Stop an agent immediately.</summary>
    Task<LifecycleActionResult> StopAgentAsync(string agentId, CancellationToken ct = default);

    /// <summary>
    ///     Drain an agent: stop accepting new requests, finish in-flight work,
    ///     then stop. Used before maintenance or update.
    /// </summary>
    Task<LifecycleActionResult> DrainAgentAsync(string agentId, CancellationToken ct = default);

    /// <summary>
    ///     Decommission an agent: remove from fleet, clean up state.
    /// </summary>
    Task<LifecycleActionResult> DecommissionAgentAsync(string agentId, CancellationToken ct = default);

    // ─── Health checks ──────────────────────────────────────────────────────────

    /// <summary>Run health check on one or all agents.</summary>
    Task<HealthCheckResult> CheckHealthAsync(string? agentId = null, CancellationToken ct = default);

    // ─── Skill package lifecycle ───────────────────────────────────────────────

    /// <summary>Update a skill package on target agent(s).</summary>
    Task<LifecycleActionResult> UpdateSkillPackageAsync(
        string packageId,
        string targetAgentId,
        CancellationToken ct = default);

    /// <summary>
    ///     Deploy a skill package as canary: small traffic share on one agent first.
    /// </summary>
    Task<CanaryResult> DeployCanaryAsync(
        string packageId,
        string targetAgentId,
        int trafficPercent = 10,
        CancellationToken ct = default);

    /// <summary>Promote canary to full deployment after validation.</summary>
    Task<LifecycleActionResult> PromoteCanaryAsync(
        string packageId,
        string targetAgentId,
        CancellationToken ct = default);

    /// <summary>Rollback a skill package to the previous version.</summary>
    Task<RollbackResult> RollbackSkillPackageAsync(
        string packageId,
        string targetAgentId,
        CancellationToken ct = default);

    /// <summary>Rollback agent itself to previous version (if applicable).</summary>
    Task<RollbackResult> RollbackAgentAsync(
        string agentId,
        CancellationToken ct = default);
}
