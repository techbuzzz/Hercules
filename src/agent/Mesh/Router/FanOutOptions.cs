namespace Hercules.Mesh.Router;

/// <summary>
///     Strategy for selecting the best response after fan-out.
///     Specification: docs/ROADMAP-RU.md Phase 4 task_045.
/// </summary>
public enum FanOutSelectionStrategy
{
    /// <summary>
    ///     Deterministic: select by explicit criterion (first-success, highest-confidence, latest).
    ///     No LLM call required. Fast and predictable.
    /// </summary>
    Deterministic = 0,

    /// <summary>
    ///     Voting: select the response that appears most frequently across peers.
    ///     Works well for factual or code-output tasks.
    /// </summary>
    Voting = 1,

    /// <summary>
    ///     LLM-judge: send all responses to an LLM with the original question and let it pick the best.
    ///     Best quality for ambiguous or open-ended tasks. May be slower and costlier.
    /// </summary>
    LlmJudge = 2
}

/// <summary>
///     Criterion for deterministic selection when <see cref="FanOutSelectionStrategy.Deterministic"/> is used.
/// </summary>
public enum DeterministicCriterion
{
    /// <summary>First successful response wins (fastest peer). No LLM call needed.</summary>
    FirstSuccess = 0,

    /// <summary>Response with highest confidence score wins.</summary>
    HighestConfidence = 1,

    /// <summary>Most recent response wins (by timestamp).</summary>
    Latest = 2
}

/// <summary>
///     Configuration options for fan-out / fan-in orchestration.
///     Controls concurrency, budget, timeout, selection strategy, and schema validation.
///     Specification: docs/ROADMAP-RU.md Phase 4 task_045.
/// </summary>
public sealed class FanOutOptions
{
    /// <summary>
    ///     Enable fan-out orchestration. If false, falls back to single-peer routing.
    ///     Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Maximum number of peer agents to fan out to in parallel.
    ///     Default: 5.
    /// </summary>
    public int MaxConcurrency { get; set; } = 5;

    /// <summary>
    ///     Per-request budget ceiling in USD. If set, any peer whose cost hint exceeds this is excluded.
    ///     Default: 1.00m.
    /// </summary>
    public decimal BudgetCeilingUsd { get; set; } = 1.00m;

    /// <summary>
    ///     Default timeout in milliseconds for each peer call during fan-out.
    ///     Individual <see cref="IntentEnvelope.Deadline"/> overrides this if sooner.
    ///     Default: 30_000.
    /// </summary>
    public int DefaultTimeoutMs { get; set; } = 30_000;

    /// <summary>
    ///     Strategy for selecting the best response from fan-out responses.
    ///     Default: <see cref="FanOutSelectionStrategy.Deterministic"/>.
    /// </summary>
    public FanOutSelectionStrategy Strategy { get; set; } = FanOutSelectionStrategy.Deterministic;

    /// <summary>
    ///     Criterion used when <see cref="Strategy"/> is <see cref="FanOutSelectionStrategy.Deterministic"/>.
    ///     Default: <see cref="DeterministicCriterion.HighestConfidence"/>.
    /// </summary>
    public DeterministicCriterion DeterministicCriterion { get; set; } = DeterministicCriterion.HighestConfidence;

    /// <summary>
    ///     Minimum number of successful responses required to perform voting.
    ///     If fewer responses are received, falls back to deterministic selection.
    ///     Default: 3.
    /// </summary>
    public int MinResponsesForVoting { get; set; } = 3;

    /// <summary>
    ///     Fraction (0.0–1.0) of responses that must agree for a voting outcome to be selected.
    ///     If agreement is below this threshold, falls back to deterministic selection.
    ///     Default: 0.51 (simple majority).
    /// </summary>
    public double VotingThreshold { get; set; } = 0.51;

    /// <summary>
    ///     Validate responses against the requested <see cref="Hercules.Mesh.Schema.ResponseSchema"/>.
    ///     Schema-violating responses are excluded from aggregation.
    ///     Default: true.
    /// </summary>
    public bool EnableSchemaValidation { get; set; } = true;

    /// <summary>
    ///     Use LLM-judge as fallback when primary strategy fails (e.g., no agreement in voting).
    ///     If false, falls back to deterministic selection without LLM.
    ///     Default: true.
    /// </summary>
    public bool EnableLlmJudgeFallback { get; set; } = true;

    /// <summary>
    ///     Minimum peer count for fan-out to trigger. If fewer peers are available,
    ///     falls back to single-peer routing.
    ///     Default: 2.
    /// </summary>
    public int MinPeersForFanOut { get; set; } = 2;
}
