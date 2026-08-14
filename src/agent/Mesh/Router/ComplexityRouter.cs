using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Router;

/// <summary>
///     Interface for the complexity-based routing decision engine.
///     Combines <see cref="IComplexityClassifier"/> with <see cref="IMeshRouter"/>
///     to select the optimal execution path for an intent.
///     Specification: docs/ROADMAP-RU.md Phase 4 task_044.
/// </summary>
public interface IComplexityRouter
{
    /// <summary>
    ///     Analyze an intent's complexity and select the best execution path.
    /// </summary>
    /// <param name="intent">Capability/intent name or user query.</param>
    /// <param name="payloadSizeBytes">Size of the request payload in bytes.</param>
    /// <param name="toolCount">Number of tool calls requested or implied.</param>
    /// <param name="estimatedCostUsd">Estimated LLM cost for one call (USD).</param>
    /// <param name="costBudgetUsd">Per-request budget ceiling (USD). Overrides default limits.</param>
    /// <param name="requestsPeer">True if the caller explicitly specified a recipient peer.</param>
    /// <param name="noLocalCapability">True if no local skill matched the intent.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A routing decision with path, cost estimate, and peer candidates (if applicable).</returns>
    Task<ComplexityRoutingDecision> ClassifyAndRouteAsync(
        string intent,
        int payloadSizeBytes,
        int toolCount,
        decimal estimatedCostUsd,
        decimal? costBudgetUsd,
        bool requestsPeer,
        bool noLocalCapability,
        CancellationToken ct = default);
}

/// <summary>
///     Complexity-based router: selects execution path based on intent complexity analysis.
///     Pipeline:
///     1. <see cref="IComplexityClassifier.AnalyzeAsync"/> — gather complexity metadata.
///     2. <see cref="IComplexityClassifier.Classify"/> — determine <see cref="ComplexityLevel"/>.
///     3. Apply routing rules per level + budget constraints.
///     4. For peer paths: query <see cref="IMeshRouter"/> for ranked candidates.
///     Specification: docs/ROADMAP-RU.md Phase 4 task_044.
/// </summary>
public sealed class ComplexityRouter : IComplexityRouter
{
    private readonly IComplexityClassifier _classifier;
    private readonly IMeshRouter _meshRouter;
    private readonly ComplexityRouterOptions _options;
    private readonly ILogger<ComplexityRouter> _logger;

    public ComplexityRouter(
        IComplexityClassifier classifier,
        IMeshRouter meshRouter,
        ComplexityRouterOptions options,
        ILogger<ComplexityRouter> logger)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _meshRouter = meshRouter ?? throw new ArgumentNullException(nameof(meshRouter));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ComplexityRoutingDecision> ClassifyAndRouteAsync(
        string intent,
        int payloadSizeBytes,
        int toolCount,
        decimal estimatedCostUsd,
        decimal? costBudgetUsd,
        bool requestsPeer,
        bool noLocalCapability,
        CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("[ComplexityRouter] Disabled — returning LocalDirect");
            return MakeDecision(ComplexityLevel.Simple, ExecutionPath.LocalDirect,
                "Complexity router is disabled", estimatedCostUsd, 0.5, null, false);
        }

        decimal budget = costBudgetUsd ?? _options.MaxBudgetPerRequestUsd;

        // Step 1: Analyze intent complexity
        ComplexityIntentAnalysis analysis = await _classifier.AnalyzeAsync(
            intent, payloadSizeBytes, toolCount, estimatedCostUsd, requestsPeer, noLocalCapability, ct);

        ComplexityLevel level = _classifier.Classify(analysis);

        // Step 2: Check budget — block if exceeds ceiling
        if (estimatedCostUsd > budget)
        {
            _logger.LogWarning(
                "[ComplexityRouter] Intent '{Intent}' cost {Cost:C} exceeds budget {Budget:C} — blocking",
                intent, estimatedCostUsd, budget);

            return MakeDecision(level, ExecutionPath.Blocked,
                $"Estimated cost {estimatedCostUsd:C} exceeds per-request budget {budget:C}",
                estimatedCostUsd, 0.9, null, false, wasDowngraded: false);
        }

        // Step 3: Determine execution path
        (ExecutionPath path, string reason, bool wasDowngraded) = DeterminePath(
            level, analysis, budget, estimatedCostUsd, requestsPeer, noLocalCapability);

        // Step 4: If peer routing, query mesh router
        IReadOnlyList<PeerCandidate>? peers = null;
        int? maxFanOut = null;

        if (path is ExecutionPath.SinglePeer or ExecutionPath.FanOut)
        {
            peers = await _meshRouter.RouteAsync(intent, budget, ct);
            _logger.LogDebug("[ComplexityRouter] MeshRouter returned {Count} peers for '{Intent}'",
                peers.Count, intent);

            if (peers.Count == 0)
            {
                // No peers available — downgrade to local execution
                return MakeDecision(level, ExecutionPath.LocalLargeModel,
                    $"No trusted peers available for '{intent}' — downgrading to local large model",
                    estimatedCostUsd, 0.6, null, withinBudget: true, wasDowngraded: true);
            }

            if (path == ExecutionPath.FanOut && _options.EnableFanOut)
            {
                maxFanOut = Math.Min(_options.MaxPeersForFanOut, peers.Count);
                peers = peers.Take(maxFanOut.Value).ToList();
            }
        }

        int suggestedRetries = level switch
        {
            ComplexityLevel.Simple => _options.MaxRetriesForSimple,
            ComplexityLevel.Moderate => _options.MaxRetriesForModerate,
            ComplexityLevel.Complex => _options.MaxRetriesForComplex,
            _ => 0
        };

        _logger.LogDebug(
            "[ComplexityRouter] intent={Intent} level={Level} path={Path} cost={Cost:C} budget={Budget:C} " +
            "peers={PeerCount} retries={Retries}",
            intent, level, path, estimatedCostUsd, budget, peers?.Count ?? 0, suggestedRetries);

        return new ComplexityRoutingDecision
        {
            Complexity = level,
            ExecutionPath = path,
            Reason = reason,
            EstimatedCostUsd = estimatedCostUsd,
            Confidence = analysis.EstimatedComplexityScore,
            SuggestedMaxRetries = suggestedRetries,
            PeerCandidates = peers,
            MaxFanOutPeers = maxFanOut,
            WithinBudget = true,
            WasDowngraded = wasDowngraded
        };
    }

    private (ExecutionPath Path, string Reason, bool WasDowngraded) DeterminePath(
        ComplexityLevel level,
        ComplexityIntentAnalysis analysis,
        decimal budget,
        decimal estimatedCostUsd,
        bool requestsPeer,
        bool noLocalCapability)
    {
        // Explicit peer request always takes priority
        if (requestsPeer)
        {
            if (!_options.EnableFanOut)
            {
                return (ExecutionPath.SinglePeer,
                    "Explicit peer request — routing to single peer", wasDowngraded: false);
            }

            return (ExecutionPath.FanOut,
                "Explicit peer request — routing to multiple peers (fan-out)", wasDowngraded: false);
        }

        // No local capability → must route to peer
        if (noLocalCapability)
        {
            IReadOnlyList<PeerCandidate> empty = Array.Empty<PeerCandidate>();
            if (_options.EnableFanOut && analysis.HasSafetySensitiveTool)
            {
                return (ExecutionPath.FanOut,
                    "No local capability + safety-sensitive tools — fan-out to multiple peers for redundancy",
                    wasDowngraded: false);
            }

            return (ExecutionPath.SinglePeer,
                "No local capability matched — routing to best available peer", wasDowngraded: false);
        }

        return level switch
        {
            // Simple: direct skill or small model
            ComplexityLevel.Simple when analysis.ToolCount == 0 =>
                (ExecutionPath.LocalDirect,
                    "Simple intent, no tools — direct skill execution", wasDowngraded: false),

            ComplexityLevel.Simple =>
                (estimatedCostUsd > _options.SmallModelCostThresholdUsd
                    ? ExecutionPath.LocalSmallModel
                    : ExecutionPath.LocalDirect,
                    $"Simple intent, {analysis.ToolCount} tool(s), cost {estimatedCostUsd:C} — " +
                    (estimatedCostUsd > _options.SmallModelCostThresholdUsd
                        ? "small model (exceeds direct cost threshold)"
                        : "direct skill (within cost threshold)"),
                    wasDowngraded: false),

            // Moderate: small model or large model
            ComplexityLevel.Moderate =>
                (estimatedCostUsd > _options.LargeModelCostThresholdUsd
                    ? ExecutionPath.LocalLargeModel
                    : ExecutionPath.LocalSmallModel,
                    $"Moderate intent, cost {estimatedCostUsd:C} — " +
                    (estimatedCostUsd > _options.LargeModelCostThresholdUsd
                        ? "large model (exceeds small model threshold)"
                        : "small model"),
                    wasDowngraded: false),

            // Complex: large model or fan-out (if peers available)
            ComplexityLevel.Complex when analysis.HasSafetySensitiveTool && _options.EnableFanOut =>
                (ExecutionPath.FanOut,
                    "Complex intent with safety-sensitive tools — fan-out to multiple trusted peers for verification",
                    wasDowngraded: false),

            ComplexityLevel.Complex =>
                (ExecutionPath.LocalLargeModel,
                    $"Complex intent — large model (safety-sensitive: {analysis.HasSafetySensitiveTool})",
                    wasDowngraded: false),

            _ => (ExecutionPath.LocalLargeModel,
                    "Default fallback — large model", wasDowngraded: false)
        };
    }

    private ComplexityRoutingDecision MakeDecision(
        ComplexityLevel level,
        ExecutionPath path,
        string reason,
        decimal costUsd,
        double confidence,
        IReadOnlyList<PeerCandidate>? peers,
        bool withinBudget,
        bool wasDowngraded = false)
    {
        return new ComplexityRoutingDecision
        {
            Complexity = level,
            ExecutionPath = path,
            Reason = reason,
            EstimatedCostUsd = costUsd,
            Confidence = confidence,
            SuggestedMaxRetries = level switch
            {
                ComplexityLevel.Simple => _options.MaxRetriesForSimple,
                ComplexityLevel.Moderate => _options.MaxRetriesForModerate,
                ComplexityLevel.Complex => _options.MaxRetriesForComplex,
                _ => 0
            },
            PeerCandidates = peers,
            WithinBudget = withinBudget,
            WasDowngraded = wasDowngraded
        };
    }
}
