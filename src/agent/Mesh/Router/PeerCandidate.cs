namespace Hercules.Mesh.Router;

/// <summary>
///     A peer agent considered as a routing candidate, with scored metadata.
///     Produced by <see cref="IMeshRouter"/> after capability lookup, trust filtering,
///     health tracking, and composite ranking.
///     Specification: docs/ROADMAP-RU.md Phase 4 task_043.
/// </summary>
public sealed record PeerCandidate
{
    /// <summary>Agent identifier.</summary>
    public string AgentId { get; init; } = "";

    /// <summary>Human-readable display name.</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>Endpoint URL for the peer (e.g. https://peer:5000).</summary>
    public string Endpoint { get; init; } = "";

    /// <summary>Last observed health score (0.0 – 1.0). 1.0 = fully healthy.</summary>
    public double HealthScore { get; init; }

    /// <summary>Observed or hint latency in milliseconds.</summary>
    public int LatencyMs { get; init; }

    /// <summary>Peer quality score (0.0 – 1.0). Derived from skill quality metrics.</summary>
    public double QualityScore { get; init; }

    /// <summary>Trust level string (e.g. "verified", "unverified", "denied").</summary>
    public string TrustLevel { get; init; } = "";

    /// <summary>UTC timestamp of the last successful call or health check.</summary>
    public DateTimeOffset LastSeen { get; init; }

    /// <summary>
    ///     Composite routing score (0.0 – 1.0).
    ///     Computed by <see cref="RouterRanking.ComputeScore"/>.
    /// </summary>
    public double CompositeScore { get; init; }

    /// <summary>Cost hint in USD per call.</summary>
    public decimal CostHintUsd { get; init; }

    /// <summary>Circuit breaker state for this peer.</summary>
    public CircuitState CircuitState { get; init; }

    /// <summary>Whether this candidate passed trust policy evaluation.</summary>
    public bool TrustPassed { get; init; }

    /// <summary>
    ///     Normalised trust score (0.0 – 1.0) used in ranking.
    ///     Computed from the string <see cref="TrustLevel"/>.
    /// </summary>
    public double TrustScore { get; init; }
}
