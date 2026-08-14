namespace Hercules.Mesh.Router;

/// <summary>
///     Metadata gathered during intent analysis for complexity classification.
///     Produced by <see cref="IComplexityClassifier.AnalyzeAsync"/> and consumed
///     by <see cref="IComplexityRouter.ClassifyAndRouteAsync"/>.
///     Specification: docs/ROADMAP-RU.md Phase 4 task_044.
/// </summary>
public sealed record ComplexityIntentAnalysis
{
    /// <summary>Original intent / capability name.</summary>
    public string Intent { get; init; } = "";

    /// <summary>Size of the request payload in bytes.</summary>
    public int PayloadSizeBytes { get; init; }

    /// <summary>Number of tool calls requested or implied by the intent.</summary>
    public int ToolCount { get; init; }

    /// <summary>Estimated cost in USD for a single LLM call at standard pricing.</summary>
    public decimal EstimatedCostUsd { get; init; }

    /// <summary>Keywords extracted from the intent text (for rule-based classification).</summary>
    public IReadOnlyList<string> IntentKeywords { get; init; } = Array.Empty<string>();

    /// <summary>
    ///     Whether any requested tool is safety-sensitive
    ///     (file system, network, code execution, database mutation, etc.).
    /// </summary>
    public bool HasSafetySensitiveTool { get; init; }

    /// <summary>
    ///     A numeric complexity score in [0, 1] produced by the classifier.
    ///     Higher = more complex.
    /// </summary>
    public double EstimatedComplexityScore { get; init; }

    /// <summary>Whether the intent mentions multi-step, chain-of-thought, or iterative reasoning.</summary>
    public bool MentionsMultiStep { get; init; }

    /// <summary>Whether the intent is likely idempotent (safe to retry).</summary>
    public bool IsIdempotent { get; init; } = true;

    /// <summary>Whether the intent is explicitly asking for a peer (recipient is set).</summary>
    public bool RequestsPeer { get; init; }

    /// <summary>True when no local capability matched the intent (forces peer routing).</summary>
    public bool NoLocalCapability { get; init; }
}
