namespace Hercules.Mesh.Router;

/// <summary>
///     Configuration options for the mesh router (task_043).
///     Controls routing behaviour: confidence thresholds, fallback, candidate limits.
/// </summary>
public sealed class MeshRouterOptions
{
    /// <summary>Enable the advanced mesh router. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     If no local capability matches and no peer qualifies, fall back to peer routing.
    ///     Default: true.
    /// </summary>
    public bool FallbackToPeer { get; set; } = true;

    /// <summary>Maximum peer candidates returned from a route query. Default: 10.</summary>
    public int MaxPeerCandidates { get; set; } = 10;

    /// <summary>Default timeout per peer call in milliseconds. Default: 30 000.</summary>
    public int DefaultTimeoutMs { get; set; } = 30_000;

    /// <summary>
    ///     Minimum composite score (0–1) for a peer to be considered.
    ///     Peers with a lower score are excluded from the result. Default: 0.2.
    /// </summary>
    public double MinConfidenceThreshold { get; set; } = 0.2;

    /// <summary>
    ///     Weights for the composite routing score.
    ///     Each weight is in [0, 1]; values are normalised internally.
    /// </summary>
    public RouterWeights Weights { get; set; } = new();
}

/// <summary>
///     Component weights for the router scoring formula.
///     Task_043 § RouterRanking: w_health*health + w_latency*latency + w_quality*quality + w_trust*trust.
/// </summary>
public sealed class RouterWeights
{
    /// <summary>Weight for peer health score. Default: 0.30.</summary>
    public double Health { get; set; } = 0.30;

    /// <summary>Weight for peer latency hint (inverted: lower latency = higher score). Default: 0.20.</summary>
    public double Latency { get; set; } = 0.20;

    /// <summary>Weight for peer quality score. Default: 0.25.</summary>
    public double Quality { get; set; } = 0.25;

    /// <summary>Weight for peer trust level. Default: 0.25.</summary>
    public double Trust { get; set; } = 0.25;
}
