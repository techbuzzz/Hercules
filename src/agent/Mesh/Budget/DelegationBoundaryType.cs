namespace Hercules.Budget;

/// <summary>
///     Types of delegation boundary limits.
/// </summary>
public enum DelegationBoundaryType
{
    /// <summary>Maximum hop count (delegation depth) allowed in a chain.</summary>
    HopCount,

    /// <summary>Maximum fan-out width (number of parallel delegations from one hop).</summary>
    FanOutWidth,

    /// <summary>Maximum cumulative tool calls across all hops.</summary>
    CumulativeToolCalls,

    /// <summary>Maximum cumulative cost in USD across all hops.</summary>
    CumulativeCostUsd,

    /// <summary>Maximum cumulative wall-clock milliseconds across all hops.</summary>
    CumulativeWallClockMs
}
