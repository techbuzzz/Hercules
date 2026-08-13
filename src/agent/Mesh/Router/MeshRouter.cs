using Hercules.Mesh.Policy;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Router;

/// <summary>
///     Advanced mesh router: selects the best trusted peer(s) for a given capability.
///     Pipeline:
///     1. Query <see cref="ICapabilityRegistry"/> for agents with matching capability.
///     2. Filter by <see cref="ITrustAdmissionPolicy"/> (trust level check).
///     3. Enrich with health scores from <see cref="RouterHealthTracker"/>.
///     4. Rank by composite score (health + latency + quality + trust).
///     5. Filter by <see cref="MeshRouterOptions.MinConfidenceThreshold"/> and budget.
///     Specification: docs/ROADMAP-RU.md Phase 4 task_043.
/// </summary>
public sealed class CapabilityMeshRouter : IMeshRouter
{
    private readonly CapabilityRegistry _registry;
    private readonly CircuitBreaker _circuitBreaker;
    private readonly ITrustAdmissionPolicy _trustPolicy;
    private readonly RouterHealthTracker _healthTracker;
    private readonly MeshRouterOptions _options;
    private readonly ILogger<CapabilityMeshRouter> _logger;

    public CapabilityMeshRouter(
        CapabilityRegistry registry,
        CircuitBreaker circuitBreaker,
        ITrustAdmissionPolicy trustPolicy,
        RouterHealthTracker healthTracker,
        MeshRouterOptions options,
        ILogger<CapabilityMeshRouter> logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _circuitBreaker = circuitBreaker ?? throw new ArgumentNullException(nameof(circuitBreaker));
        _trustPolicy = trustPolicy ?? throw new ArgumentNullException(nameof(trustPolicy));
        _healthTracker = healthTracker ?? throw new ArgumentNullException(nameof(healthTracker));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PeerCandidate>> RouteAsync(string capability, CancellationToken ct = default)
    {
        return RouteAsync(capability, decimal.MaxValue, ct);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PeerCandidate>> RouteAsync(
        string capability,
        decimal maxCostUsd,
        CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("[MeshRouter] Router disabled — returning empty candidates");
            return Task.FromResult<IReadOnlyList<PeerCandidate>>(Array.Empty<PeerCandidate>());
        }

        // 1. Find agents with matching capability
        List<RegistryAgentEntry> agents = _registry.FindByCapability(capability);

        if (agents.Count == 0)
        {
            // Fallback: try phrase-based lookup
            agents = _registry.FindByPhrase(capability);
            _logger.LogDebug("[MeshRouter] No capability match for '{Capability}', phrase fallback returned {Count} agents",
                capability, agents.Count);
        }
        else
        {
            _logger.LogDebug("[MeshRouter] Capability '{Capability}' matched {Count} agents",
                capability, agents.Count);
        }

        var results = new List<PeerCandidate>();

        foreach (RegistryAgentEntry agent in agents)
        {
            // 2. Get full record (includes trust, latency, cost hints)
            RegistryAgentFullEntry? full = _registry.GetFull(agent.AgentId);

            // 3. Circuit breaker check
            CircuitState circuitState = _circuitBreaker.GetState(agent.AgentId);
            if (_healthTracker.IsCircuitOpen(agent.AgentId, circuitState))
            {
                _logger.LogDebug("[MeshRouter] Agent {AgentId} circuit open — skipping", agent.AgentId);
                continue;
            }

            // 4. Trust policy check
            bool trustPassed = EvaluateTrust(agent.AgentId, full?.TrustLevel ?? "unverified");
            if (!trustPassed)
            {
                _logger.LogDebug("[MeshRouter] Agent {AgentId} failed trust check (level={Level}) — skipping",
                    agent.AgentId, full?.TrustLevel ?? "unknown");
                continue;
            }

            // 5. Budget check
            decimal costHint = full?.CostHintUsd ?? 0;
            if (costHint > maxCostUsd)
            {
                _logger.LogDebug("[MeshRouter] Agent {AgentId} cost {Cost} > budget {Budget} — skipping",
                    agent.AgentId, costHint, maxCostUsd);
                continue;
            }

            // 6. Enrich with health tracker data
            double healthScore = _healthTracker.GetHealthScore(agent.AgentId);
            int latencyMs = _healthTracker.GetAverageLatencyMs(agent.AgentId);

            // Fall back to registry hints if no live data
            if (latencyMs == 0 && full?.LatencyHintMs > 0)
            {
                latencyMs = full.LatencyHintMs;
            }

            // 7. Build candidate and compute composite score
            double trustScore = RouterRanking.NormaliseTrust(full?.TrustLevel ?? "unverified");
            double qualityScore = QualityFromHealth(healthScore, full?.HealthStatus ?? "unknown");

            var candidate = new PeerCandidate
            {
                AgentId = agent.AgentId,
                DisplayName = agent.DisplayName,
                Endpoint = agent.Endpoint,
                HealthScore = healthScore,
                LatencyMs = latencyMs,
                QualityScore = qualityScore,
                TrustLevel = full?.TrustLevel ?? "unverified",
                LastSeen = DateTimeOffset.TryParse(agent.LastSeen, out var dt) ? dt : DateTimeOffset.UtcNow,
                CompositeScore = 0, // set below after construction
                CostHintUsd = costHint,
                CircuitState = circuitState,
                TrustPassed = true,
                TrustScore = trustScore
            };

            candidate = candidate with { CompositeScore = RouterRanking.ComputeScore(candidate, _options.Weights) };

            // 8. Filter by confidence threshold
            if (candidate.CompositeScore < _options.MinConfidenceThreshold)
            {
                _logger.LogDebug("[MeshRouter] Agent {AgentId} score {Score} < threshold {Threshold} — skipping",
                    agent.AgentId, candidate.CompositeScore, _options.MinConfidenceThreshold);
                continue;
            }

            results.Add(candidate);
        }

        // 9. Sort by composite score descending
        results.Sort((a, b) => b.CompositeScore.CompareTo(a.CompositeScore));

        // 10. Limit to MaxPeerCandidates
        if (results.Count > _options.MaxPeerCandidates)
        {
            results = results.Take(_options.MaxPeerCandidates).ToList();
        }

        _logger.LogDebug("[MeshRouter] Routed '{Capability}' → {Count} candidates (top={TopScore:F4})",
            capability, results.Count,
            results.Count > 0 ? results[0].CompositeScore : 0);

        return Task.FromResult<IReadOnlyList<PeerCandidate>>(results);
    }

    private bool EvaluateTrust(string agentId, string trustLevel)
    {
        if (_trustPolicy.Mode == PolicyMode.Disabled)
        {
            return true;
        }

        // Trust is denied for explicitly denied peers
        return trustLevel.ToLowerInvariant() is not ("denied" or "suspicious");
    }

    private static double QualityFromHealth(double healthScore, string healthStatus)
    {
        // Derive quality from observed health and registry health status
        if (healthStatus.ToLowerInvariant() is "healthy")
        {
            return Math.Max(healthScore, 0.8);
        }

        if (healthStatus.ToLowerInvariant() is "unhealthy")
        {
            return Math.Min(healthScore, 0.3);
        }

        return healthScore;
    }
}
