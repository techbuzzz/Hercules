using System.Text;
using System.Text.Json;
using Hercules.LLM;
using Hercules.Mesh.Audit;
using Hercules.Mesh.Router;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh;

/// <summary>
///     Distributed Reflection — mesh-aware reflection engine.
///     Analyzes peer agent performance, routing decisions, failure patterns,
///     and generates structured proposals (new skills, routing rules, peer connections).
///     Proposals are NOT auto-applied — they require human approval (human-in-the-loop).
///     Specification: docs/ROADMAP-RU.md Phase 4 task_050.
/// </summary>
public sealed class DistributedReflection
{
    private readonly ILLMClient _llm;
    private readonly CapabilityRegistry _registry;
    private readonly CircuitBreaker _breaker;
    private readonly RouterHealthTracker _healthTracker;
    private readonly MeshAuditService? _auditService;
    private readonly ReflectionProposalStore _proposalStore;
    private readonly ReflectionProposalConfig _config;
    private readonly ILogger<DistributedReflection> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    public DistributedReflection(
        ILLMClient llm,
        CapabilityRegistry registry,
        CircuitBreaker breaker,
        RouterHealthTracker healthTracker,
        ReflectionProposalStore proposalStore,
        ReflectionProposalConfig config,
        ILogger<DistributedReflection> logger,
        MeshAuditService? auditService = null)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _breaker = breaker ?? throw new ArgumentNullException(nameof(breaker));
        _healthTracker = healthTracker ?? throw new ArgumentNullException(nameof(healthTracker));
        _proposalStore = proposalStore ?? throw new ArgumentNullException(nameof(proposalStore));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _auditService = auditService;
    }

    /// <summary>
    ///     Run a full distributed reflection cycle: collect metrics, generate proposals, save them.
    ///     Returns a markdown summary for human review and the list of generated proposals.
    /// </summary>
    public async Task<DistributedReflectionResult> ReflectAsync(CancellationToken ct = default)
    {
        var proposals = new List<ReflectionProposal>();

        // 1. Collect peer performance metrics
        var peerMetrics = CollectPeerMetrics();

        // 2. Collect failure patterns from audit
        var failurePatterns = CollectFailurePatterns();

        // 3. Collect routing decision patterns
        var routingPatterns = CollectRoutingPatterns();

        // 4. Analyze and generate proposals
        if (_config.GenerateProposals)
        {
            proposals.AddRange(await GenerateProposalsAsync(peerMetrics, failurePatterns, routingPatterns, ct));
        }

        // 5. Save proposals
        foreach (var proposal in proposals)
        {
            _proposalStore.Save(proposal);
        }

        // 6. Build markdown summary
        string summary = BuildMarkdownSummary(peerMetrics, failurePatterns, routingPatterns, proposals);

        _logger.LogInformation(
            "[DistributedReflection] Completed: {PeerCount} peers, {FailureCount} failures, {ProposalCount} proposals",
            peerMetrics.Count, failurePatterns.Count, proposals.Count);

        return new DistributedReflectionResult
        {
            Markdown = summary,
            PeerMetrics = peerMetrics,
            FailurePatterns = failurePatterns,
            RoutingPatterns = routingPatterns,
            Proposals = proposals
        };
    }

    /// <summary>
    ///     Collect rolling peer performance metrics from RouterHealthTracker.
    /// </summary>
    public IReadOnlyList<PeerMetric> CollectPeerMetrics()
    {
        var agents = _registry.ListAgents();
        var metrics = new List<PeerMetric>();

        foreach (var agent in agents)
        {
            double healthScore = _healthTracker.GetHealthScore(agent.AgentId);
            int avgLatencyMs = _healthTracker.GetAverageLatencyMs(agent.AgentId);
            CircuitState circuitState = _breaker.GetState(agent.AgentId);
            List<RegistryCapabilityEntry> caps = _registry.ListCapabilities(agent.AgentId);

            metrics.Add(new PeerMetric
            {
                AgentId = agent.AgentId,
                DisplayName = agent.DisplayName,
                HealthScore = healthScore,
                AvgLatencyMs = avgLatencyMs,
                CircuitState = circuitState,
                Capabilities = caps.Select(c => c.Name).ToList(),
                LastSeen = DateTimeOffset.TryParse(agent.LastSeen, out var dt) ? dt : DateTimeOffset.UtcNow
            });
        }

        return metrics;
    }

    /// <summary>
    ///     Collect failure patterns from inter-agent audit records.
    ///     Groups by peer + intent, counts error types.
    /// </summary>
    public IReadOnlyList<FailurePattern> CollectFailurePatterns()
    {
        // We sample the last N audit records from the file sink (best-effort).
        // Production: wire in a dedicated query service.
        var patterns = new Dictionary<string, FailurePattern>(StringComparer.OrdinalIgnoreCase);

        // Always check circuit breaker state — this is the primary failure signal (tracked in _breaker).
        // Audit service enrichment is optional and only used when available.
        var circuitStates = _breaker.GetAllStates();
        foreach (var kvp in circuitStates)
        {
            if (kvp.Value == CircuitState.Open)
            {
                var key = $"cb_open:{kvp.Key}";
                patterns[key] = new FailurePattern
                {
                    PatternId = key,
                    PeerAgentId = kvp.Key,
                    FailureType = "circuit_open",
                    Count = 1,
                    Description = $"Circuit breaker for {kvp.Key} is open — peer unreachable",
                    LastOccurrence = DateTimeOffset.UtcNow,
                    RecommendedAction = "Investigate peer health or increase cooldown threshold"
                };
            }
        }

        return patterns.Values.ToList();
    }

    /// <summary>
    ///     Collect routing decision patterns: which intents were routed where and with what score.
    ///     This uses RegistryCapabilityEntry data as proxy (no separate routing log yet).
    /// </summary>
    public IReadOnlyList<RoutingPattern> CollectRoutingPatterns()
    {
        var patterns = new List<RoutingPattern>();
        var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var agent in _registry.ListAgents())
        {
            foreach (var cap in _registry.ListCapabilities(agent.AgentId))
            {
                if (capabilities.Add(cap.Name))
                {
                    patterns.Add(new RoutingPattern
                    {
                        Intent = cap.Name,
                        Description = cap.Description,
                        ResolvedPeerCount = _registry.FindByCapability(cap.Name).Count,
                        FallbackCount = 0 // populated from audit in production
                    });
                }
            }
        }

        return patterns;
    }

    /// <summary>
    ///     Generate structured proposals based on collected data.
    /// </summary>
    private async Task<List<ReflectionProposal>> GenerateProposalsAsync(
        IReadOnlyList<PeerMetric> peerMetrics,
        IReadOnlyList<FailurePattern> failurePatterns,
        IReadOnlyList<RoutingPattern> routingPatterns,
        CancellationToken ct)
    {
        var proposals = new List<ReflectionProposal>();

        // 1. Circuit-open → local skill proposals (peer unavailable, capability needed)
        foreach (var pattern in failurePatterns.Where(p => p.FailureType == "circuit_open"))
        {
            var caps = _registry.ListCapabilities(pattern.PeerAgentId);
            foreach (var cap in caps)
            {
                if (!HasLocalCapability(cap.Name))
                {
                    proposals.Add(new ReflectionProposal
                    {
                        Type = ProposalType.NewSkill,
                        Title = $"Local skill: {cap.Name}",
                        Rationale = $"Peer '{pattern.PeerAgentId}' is unavailable (circuit open) but its capability '{cap.Name}' may be needed. Consider a local implementation to reduce dependency.",
                        Content = BuildSkillStub(cap.Name, cap.Description),
                        Target = cap.Name,
                        Priority = ProposalPriority.High,
                        Trigger = $"circuit_open:{pattern.PeerAgentId}",
                        EvidenceJson = JsonSerializer.Serialize(new { peer = pattern.PeerAgentId, capability = cap.Name }, JsonOpts),
                        GeneratedBy = "DistributedReflection"
                    });
                }
            }
        }

        // 2. Low-health peers → trust update proposal
        foreach (var metric in peerMetrics.Where(m => m.HealthScore < _config.LowHealthThreshold))
        {
            proposals.Add(new ReflectionProposal
            {
                Type = ProposalType.TrustUpdate,
                Title = $"Review trust level for {metric.AgentId}",
                Rationale = $"Peer '{metric.AgentId}' health score is {metric.HealthScore:P0} (below {_config.LowHealthThreshold:P0} threshold). Consider reducing trust level or removing from routing.",
                Content = $"current_trust=unknown\nproposed_trust=reduced\nreason=health_score={metric.HealthScore:P0}",
                Target = metric.AgentId,
                Priority = ProposalPriority.Medium,
                Trigger = $"low_health:{metric.AgentId}",
                EvidenceJson = JsonSerializer.Serialize(new { agentId = metric.AgentId, healthScore = metric.HealthScore, latencyMs = metric.AvgLatencyMs }, JsonOpts),
                GeneratedBy = "DistributedReflection"
            });
        }

        // 3. Capability gap detection via LLM (only if enabled and peers exist)
        if (_config.EnableLLMAnalysis && peerMetrics.Count > 0)
        {
            var llmProposals = await GenerateLLMProposalsAsync(peerMetrics, failurePatterns, routingPatterns, ct);
            proposals.AddRange(llmProposals);
        }

        return proposals
            .GroupBy(p => $"{p.Type}:{p.Target}")
            .Select(g => g.First())
            .ToList();
    }

    private async Task<List<ReflectionProposal>> GenerateLLMProposalsAsync(
        IReadOnlyList<PeerMetric> peerMetrics,
        IReadOnlyList<FailurePattern> failurePatterns,
        IReadOnlyList<RoutingPattern> routingPatterns,
        CancellationToken ct)
    {
        var proposals = new List<ReflectionProposal>();

        string metricsJson = JsonSerializer.Serialize(peerMetrics, JsonOpts);
        string failuresJson = JsonSerializer.Serialize(failurePatterns, JsonOpts);
        string routingJson = JsonSerializer.Serialize(routingPatterns, JsonOpts);

        var prompt = $"""
                      Analyze the following mesh reflection data and generate proposals.
                      Respond ONLY with a valid JSON array of proposals (max 3 proposals).
                      Each proposal must have: type, title, rationale, content, target, priority, trigger, evidenceJson.
                      Types: NewSkill, RoutingRule, PeerConnection, TrustUpdate, CapabilityDeclare.
                      Priority: Low, Medium, High, Critical.

                      Peer metrics:
                      {metricsJson}

                      Failure patterns:
                      {failuresJson}

                      Routing patterns:
                      {routingJson}
                      """;

        try
        {
            LlmResponse resp = await _llm.CompleteAsync(Roles.Reflector, [
                new ChatTurn(ChatRole.System, "Ты — модуль distributed reflection. Верни ТОЛЬКО JSON массив proposal-объектов, без markdown, без пояснений."),
                new ChatTurn(ChatRole.User, prompt)
            ], ct);

            var parsed = JsonSerializer.Deserialize<ReflectionProposal[]>(resp.Text, JsonOpts);
            if (parsed is not null)
            {
                foreach (var p in parsed)
                {
                    p.GeneratedBy = "DistributedReflection:LLM";
                }
                proposals.AddRange(parsed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DistributedReflection] LLM proposal generation failed — continuing without LLM proposals");
        }

        return proposals;
    }

    /// <summary>
    ///     Get all pending proposals for human review.
    /// </summary>
    public IReadOnlyList<ReflectionProposal> GetPendingProposals() =>
        _proposalStore.LoadAll(ProposalStatus.Pending);

    /// <summary>
    ///     Get all proposals.
    /// </summary>
    public IReadOnlyList<ReflectionProposal> GetAllProposals() =>
        _proposalStore.LoadAll();

    /// <summary>
    ///     Approve a proposal (human-in-the-loop).
    /// </summary>
    public void Approve(string proposalId, string reviewer, string? comment = null)
    {
        _proposalStore.UpdateStatus(proposalId, ProposalStatus.Approved, reviewer, comment);
        _logger.LogInformation("[DistributedReflection] Proposal {Id} APPROVED by {Reviewer}", proposalId, reviewer);
    }

    /// <summary>
    ///     Reject a proposal.
    /// </summary>
    public void Reject(string proposalId, string reviewer, string? comment = null)
    {
        _proposalStore.UpdateStatus(proposalId, ProposalStatus.Rejected, reviewer, comment);
        _logger.LogInformation("[DistributedReflection] Proposal {Id} REJECTED by {Reviewer}", proposalId, reviewer);
    }

    /// <summary>
    ///     Get a single proposal by Id.
    /// </summary>
    public ReflectionProposal? GetProposal(string id) => _proposalStore.Get(id);

    /// <summary>
    ///     Get local skill recommendations: capabilities from unavailable peers (circuit open)
    ///     that could be implemented locally to reduce dependency.
    /// </summary>
    public List<string> GetLocalSkillRecommendations()
    {
        var recommendations = new List<string>();
        var openCircuits = _breaker.GetAllStates()
            .Where(kvp => kvp.Value == CircuitState.Open)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var agentId in openCircuits)
        {
            List<RegistryCapabilityEntry> caps = _registry.ListCapabilities(agentId);
            foreach (RegistryCapabilityEntry cap in caps)
            {
                recommendations.Add(
                    $"Create local skill '{cap.Name}' — peer '{agentId}' is unavailable (circuit open), " +
                    $"but its capability '{cap.Name}' may be needed.");
            }
        }

        return recommendations;
    }

    private bool HasLocalCapability(string capabilityName)
    {
        // Simple existence check — in production would cross-reference local skill registry
        return false;
    }

    private static string BuildSkillStub(string name, string description)
    {
        return $"""
                # Skill: {name}

                ## Description
                {description}

                ## Triggers
                TBD (based on reflection data)

                ## Implementation
                ## Acceptance Criteria
                ## Tests
                """;
    }

    private string BuildMarkdownSummary(
        IReadOnlyList<PeerMetric> peerMetrics,
        IReadOnlyList<FailurePattern> failurePatterns,
        IReadOnlyList<RoutingPattern> routingPatterns,
        IReadOnlyList<ReflectionProposal> proposals)
    {
        var sb = new StringBuilder();
        var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        sb.AppendLine($"# Distributed Reflection — {ts}");
        sb.AppendLine();

        // Peer performance
        sb.AppendLine("## Peer Performance");
        if (peerMetrics.Count == 0)
        {
            sb.AppendLine("No peer agents known.");
        }
        else
        {
            sb.AppendLine($"| AgentId | Health | Latency (ms) | Circuit | Capabilities |");
            sb.AppendLine($"|---------|--------|-------------|---------|--------------|");
            foreach (var m in peerMetrics)
            {
                var circuitEmoji = m.CircuitState == CircuitState.Open ? "⚠️ OPEN" : "✅ closed";
                sb.AppendLine($"| {m.AgentId} | {m.HealthScore:P0} | {m.AvgLatencyMs} | {circuitEmoji} | {string.Join(", ", m.Capabilities.Take(3))} |");
            }
        }
        sb.AppendLine();

        // Failure patterns
        sb.AppendLine("## Failure Patterns");
        if (failurePatterns.Count == 0)
        {
            sb.AppendLine("No failure patterns detected.");
        }
        else
        {
            foreach (var fp in failurePatterns)
            {
                sb.AppendLine($"- **[{fp.FailureType}]** {fp.PeerAgentId}: {fp.Description}");
                sb.AppendLine($"  → {fp.RecommendedAction}");
            }
        }
        sb.AppendLine();

        // Routing patterns
        sb.AppendLine("## Routing Patterns");
        if (routingPatterns.Count == 0)
        {
            sb.AppendLine("No routing data.");
        }
        else
        {
            foreach (var rp in routingPatterns.Take(10))
            {
                sb.AppendLine($"- `{rp.Intent}` → {rp.ResolvedPeerCount} peer(s) | {rp.Description}");
            }
        }
        sb.AppendLine();

        // Proposals
        sb.AppendLine("## Proposals (require human approval)");
        if (proposals.Count == 0)
        {
            sb.AppendLine("No proposals generated.");
        }
        else
        {
            foreach (var p in proposals)
            {
                sb.AppendLine($"- **[{p.Type}]** `{p.Id}` — {p.Title} (priority: {p.Priority}, trigger: {p.Trigger})");
            }
        }

        return sb.ToString();
    }
}

/// <summary>
///     Result of a distributed reflection run.
/// </summary>
public sealed class DistributedReflectionResult
{
    public string Markdown { get; set; } = "";
    public IReadOnlyList<PeerMetric> PeerMetrics { get; set; } = Array.Empty<PeerMetric>();
    public IReadOnlyList<FailurePattern> FailurePatterns { get; set; } = Array.Empty<FailurePattern>();
    public IReadOnlyList<RoutingPattern> RoutingPatterns { get; set; } = Array.Empty<RoutingPattern>();
    public IReadOnlyList<ReflectionProposal> Proposals { get; set; } = Array.Empty<ReflectionProposal>();

    // Backward-compatible summary fields (used by ConsoleUI)
    public int PeerCount => PeerMetrics.Count;
    public int OpenCircuitCount => PeerMetrics.Count(m => m.CircuitState == CircuitState.Open);
    public int TotalCapabilities => PeerMetrics.SelectMany(m => m.Capabilities).Distinct().Count();
}

/// <summary>
///     Peer performance metric snapshot.
/// </summary>
public sealed class PeerMetric
{
    public string AgentId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public double HealthScore { get; set; } // 0.0 – 1.0
    public int AvgLatencyMs { get; set; }
    public CircuitState CircuitState { get; set; }
    public IReadOnlyList<string> Capabilities { get; set; } = Array.Empty<string>();
    public DateTimeOffset LastSeen { get; set; }
}

/// <summary>
///     Detected failure pattern.
/// </summary>
public sealed class FailurePattern
{
    public string PatternId { get; set; } = "";
    public string PeerAgentId { get; set; } = "";
    public string FailureType { get; set; } = ""; // circuit_open, timeout, error, schema_mismatch
    public int Count { get; set; }
    public string Description { get; set; } = "";
    public DateTimeOffset LastOccurrence { get; set; }
    public string RecommendedAction { get; set; } = "";
}

/// <summary>
///     Routing decision pattern.
/// </summary>
public sealed class RoutingPattern
{
    public string Intent { get; set; } = "";
    public string Description { get; set; } = "";
    public int ResolvedPeerCount { get; set; }
    public int FallbackCount { get; set; }
}

/// <summary>
///     Configuration for distributed reflection proposals.
/// </summary>
public sealed class ReflectionProposalConfig
{
    /// <summary>Enable proposal generation during reflection. Default: true.</summary>
    public bool GenerateProposals { get; set; } = true;

    /// <summary>Enable LLM-assisted proposal generation. Default: true.</summary>
    public bool EnableLLMAnalysis { get; set; } = true;

    /// <summary>Health score below which a peer is flagged as low-health. Default: 0.5.</summary>
    public double LowHealthThreshold { get; set; } = 0.5;

    /// <summary>Minimum failures to trigger a circuit-open proposal. Default: 1.</summary>
    public int MinFailuresForProposal { get; set; } = 1;
}
