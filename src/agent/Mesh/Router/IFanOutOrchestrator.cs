using Hercules.Mesh.Aggregation;
using Hercules.Mesh.Schema;

namespace Hercules.Mesh.Router;

/// <summary>
///     Orchestrates fan-out requests to multiple peer agents with concurrency control,
///     budget enforcement, schema validation, and response aggregation.
///     Specification: docs/ROADMAP-RU.md Phase 4 task_045.
/// </summary>
public interface IFanOutOrchestrator
{
    /// <summary>
    ///     Send an intent to multiple applicable peer agents in parallel and aggregate responses.
    ///     1. Query <see cref="IMeshRouter"/> for ranked peer candidates.
    ///     2. Fan out respecting <paramref name="budgetUsd"/> and per-request deadline.
    ///     3. Validate responses against <paramref name="responseSchema"/>.
    ///     4. Select best response using <paramref name="strategy"/> (deterministic / voting / LLM-judge).
    /// </summary>
    /// <param name="envelope">Intent envelope with payload and routing metadata.</param>
    /// <param name="responseSchema">Expected response schema (used for validation). Pass null to skip validation.</param>
    /// <param name="strategy">Selection strategy override. Pass null to use configured default.</param>
    /// <param name="budgetUsd">Per-request budget ceiling. Overrides config default.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Aggregation result with winner, all responses, and selection metadata.</returns>
    Task<AggregationResult> OrchestrateFanOutAsync(
        IntentEnvelope envelope,
        ResponseSchema? responseSchema,
        FanOutSelectionStrategy? strategy,
        decimal? budgetUsd,
        CancellationToken ct = default);
}
