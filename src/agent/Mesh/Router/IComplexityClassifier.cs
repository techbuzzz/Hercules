namespace Hercules.Mesh.Router;

/// <summary>
///     Intent complexity classifier — determines how complex an incoming request is.
///     Implementation: <see cref="ComplexityClassifier"/> (rule-based).
///     Specification: docs/ROADMAP-RU.md Phase 4 task_044.
/// </summary>
public interface IComplexityClassifier
{
    /// <summary>
    ///     Analyze an intent and produce structured complexity metadata.
    ///     Does not make routing decisions — only classification metadata.
    /// </summary>
    /// <param name="intent">Capability/intent name or user query.</param>
    /// <param name="payloadSizeBytes">Size of the request payload in bytes.</param>
    /// <param name="toolCount">Number of tool calls requested or implied.</param>
    /// <param name="estimatedCostUsd">Estimated cost in USD for one LLM call.</param>
    /// <param name="requestsPeer">True if the envelope has a specific recipient peer.</param>
    /// <param name="noLocalCapability">True if no local skill matched the intent.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Structured complexity analysis.</returns>
    Task<ComplexityIntentAnalysis> AnalyzeAsync(
        string intent,
        int payloadSizeBytes,
        int toolCount,
        decimal estimatedCostUsd,
        bool requestsPeer,
        bool noLocalCapability,
        CancellationToken ct = default);

    /// <summary>
    ///     Classify a pre-computed analysis into a <see cref="ComplexityLevel"/>.
    /// </summary>
    ComplexityLevel Classify(ComplexityIntentAnalysis analysis);
}
