namespace Hercules.Budget;

/// <summary>
///     Aggregated boundary metrics for a delegation chain.
///     Tracked in-memory keyed by root request ID.
///     Specification: docs/ROADMAP-RU.md Phase 4 task_048.
/// </summary>
public sealed class DelegationBoundaryContext
{
    /// <summary>Root request ID of the delegation chain.</summary>
    public string RootRequestId { get; set; } = "";

    /// <summary>Current hop count (0 = original sender).</summary>
    public int HopCount { get; set; }

    /// <summary>Cumulative tool calls across all hops in the chain.</summary>
    public int CumulativeToolCalls { get; set; }

    /// <summary>Cumulative cost in USD across all hops.</summary>
    public decimal CumulativeCostUsd { get; set; }

    /// <summary>Cumulative wall-clock milliseconds across all hops.</summary>
    public long CumulativeWallClockMs { get; set; }

    /// <summary>UTC timestamp of the original request.</summary>
    public DateTimeOffset ChainStartUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Last updated timestamp.</summary>
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Chain of agent IDs visited so far.</summary>
    public List<string> AgentChain { get; set; } = new();

    /// <summary>
    ///     Record an additional hop: increment counters for this agent.
    /// </summary>
    public void RecordHop(string agentId, int toolCalls, decimal costUsd, long wallClockMs)
    {
        HopCount++;
        CumulativeToolCalls += toolCalls;
        CumulativeCostUsd += costUsd;
        CumulativeWallClockMs += wallClockMs;
        UpdatedUtc = DateTimeOffset.UtcNow;

        if (!AgentChain.Contains(agentId))
        {
            AgentChain.Add(agentId);
        }
    }
}

/// <summary>
///     Result of a delegation boundary check.
/// </summary>
public sealed record DelegationBoundaryCheckResult(
    bool IsAllowed,
    bool IsHardViolation,
    IReadOnlyList<DelegationBoundaryViolation> Violations);

/// <summary>
///     A single boundary that was violated.
/// </summary>
public sealed record DelegationBoundaryViolation(
    DelegationBoundaryType Type,
    long Limit,
    long Actual,
    string Message);
