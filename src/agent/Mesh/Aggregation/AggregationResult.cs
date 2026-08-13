namespace Hercules.Mesh.Aggregation;

/// <summary>
///     Result of fan-out / fan-in aggregation: winner response, all responses,
///     selection metadata, and schema validation violations.
///     Specification: docs/ROADMAP-RU.md Phase 4 task_045.
/// </summary>
public sealed class AggregationResult
{
    /// <summary>
    ///     The selected best response, or null if no successful response was received.
    /// </summary>
    public IntentResponse? Winner { get; init; }

    /// <summary>
    ///     All responses received from peers (including failed and schema-violating ones).
    /// </summary>
    public IReadOnlyList<IntentResponse> AllResponses { get; init; } = [];

    /// <summary>
    ///     Subset of responses that were successful and passed schema validation.
    /// </summary>
    public IReadOnlyList<IntentResponse> ValidResponses { get; init; } = [];

    /// <summary>
    ///     Human-readable description of the selection method used.
    ///     Examples: "deterministic-highest-confidence", "voting-majority", "llm-judge", "single-peer", "local".
    /// </summary>
    public string SelectionMethod { get; init; } = "";

    /// <summary>
    ///     Total wall-clock time spent on fan-out + aggregation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    ///     For LLM-judge strategy: the judge's rationale for choosing the winner.
    ///     Null for non-LLM strategies.
    /// </summary>
    public string? JudgeRationale { get; init; }

    /// <summary>
    ///     Schema validation violations: peer agent id → reason string.
    ///     Responses listed here were excluded from aggregation.
    /// </summary>
    public IReadOnlyDictionary<string, string> SchemaViolations { get; init; } = new Dictionary<string, string>();

    /// <summary>
    ///     Number of peers that were called (before filtering).
    /// </summary>
    public int PeersContacted { get; init; }

    /// <summary>
    ///     Number of successful responses received.
    /// </summary>
    public int SuccessCount { get; init; }

    /// <summary>
    ///     Whether a winner was selected (i.e., <see cref="Winner"/> is non-null and successful).
    /// </summary>
    public bool HasWinner => Winner is not null && Winner.IsSuccess;

    /// <summary>
    ///     Factory: build a result for a local (non-fan-out) execution.
    /// </summary>
    public static AggregationResult Local(
        IntentResponse localResponse,
        TimeSpan duration)
    {
        return new AggregationResult
        {
            Winner = localResponse,
            AllResponses = [localResponse],
            ValidResponses = localResponse.IsSuccess ? [localResponse] : [],
            SelectionMethod = "local",
            Duration = duration,
            PeersContacted = 0,
            SuccessCount = localResponse.IsSuccess ? 1 : 0
        };
    }

    /// <summary>
    ///     Factory: build a result when fan-out was skipped (too few peers) and a single peer was used.
    /// </summary>
    public static AggregationResult SinglePeer(
        IntentResponse response,
        TimeSpan duration)
    {
        return new AggregationResult
        {
            Winner = response,
            AllResponses = [response],
            ValidResponses = response.IsSuccess ? [response] : [],
            SelectionMethod = "single-peer",
            Duration = duration,
            PeersContacted = 1,
            SuccessCount = response.IsSuccess ? 1 : 0
        };
    }

    /// <summary>
    ///     Factory: build an empty result (no peers available, no local handler).
    /// </summary>
    public static AggregationResult NoPeers(TimeSpan duration)
    {
        return new AggregationResult
        {
            Winner = null,
            AllResponses = [],
            ValidResponses = [],
            SelectionMethod = "no-peers",
            Duration = duration,
            PeersContacted = 0,
            SuccessCount = 0
        };
    }
}
