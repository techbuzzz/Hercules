namespace Hercules.Mesh.Router;

/// <summary>
///     Execution path selected by the complexity router based on intent analysis.
///     Specification: docs/ROADMAP-RU.md Phase 4 task_044.
/// </summary>
public enum ExecutionPath
{
    /// <summary>
    ///     Execute directly using a local skill (no model call needed).
    ///     Used for trivial, well-defined tasks.
    /// </summary>
    LocalDirect = 0,

    /// <summary>
    ///     Execute using a small/fast model (e.g. gpt-4o-mini, gpt-3.5-turbo).
    ///     Low cost, sufficient for simple reasoning tasks.
    /// </summary>
    LocalSmallModel = 1,

    /// <summary>
    ///     Execute using a large/focused model (e.g. gpt-4, claude-3.5-sonnet).
    ///     Higher cost, needed for complex reasoning.
    /// </summary>
    LocalLargeModel = 2,

    /// <summary>
    ///     Delegate to a single trusted peer agent via the mesh router.
    ///     Used when no local capability matches or peer is better suited.
    /// </summary>
    SinglePeer = 3,

    /// <summary>
    ///     Fan out to multiple trusted peer agents in parallel, aggregate responses.
    ///     Used for complex tasks requiring diverse expertise or redundancy.
    /// </summary>
    FanOut = 4,

    /// <summary>
    ///     Request blocked: exceeds configured limits (budget, safety, tool scope).
    ///     Return error to caller instead of executing.
    /// </summary>
    Blocked = 5
}

/// <summary>
///     Decision produced by <see cref="IComplexityRouter.ClassifyAndRouteAsync"/>.
///     Contains complexity assessment, selected execution path, cost estimate,
///     and routing metadata.
///     Specification: docs/ROADMAP-RU.md Phase 4 task_044.
/// </summary>
public sealed record ComplexityRoutingDecision
{
    /// <summary>Assessed complexity level of the intent.</summary>
    public ComplexityLevel Complexity { get; init; }

    /// <summary>Selected execution path.</summary>
    public ExecutionPath ExecutionPath { get; init; }

    /// <summary>Human-readable explanation of why this path was chosen.</summary>
    public string Reason { get; init; } = "";

    /// <summary>Estimated cost in USD for this execution path.</summary>
    public decimal EstimatedCostUsd { get; init; }

    /// <summary>Confidence in the complexity classification (0.0–1.0).</summary>
    public double Confidence { get; init; }

    /// <summary>Suggested maximum retry count for this execution path.</summary>
    public int SuggestedMaxRetries { get; init; }

    /// <summary>
    ///     For <see cref="ExecutionPath.SinglePeer"/> or <see cref="ExecutionPath.FanOut"/>:
    ///     ranked list of peer candidates from the mesh router.
    ///     Null for local execution paths.
    /// </summary>
    public IReadOnlyList<PeerCandidate>? PeerCandidates { get; init; }

    /// <summary>
    ///     For <see cref="ExecutionPath.FanOut"/>: maximum number of peers to fan out to.
    /// </summary>
    public int? MaxFanOutPeers { get; init; }

    /// <summary>
    ///     Whether this decision was made within the configured budget.
    /// </summary>
    public bool WithinBudget { get; init; }

    /// <summary>
    ///     Whether the requested execution path was restricted (budget exceeded → downgrade).
    /// </summary>
    public bool WasDowngraded { get; init; }
}
