using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh;

/// <summary>
///     Stores structured reflection proposals as JSON files in the skills directory.
///     Proposals are NOT auto-applied — they require human approval (human-in-the-loop).
///     Specification: docs/ROADMAP-RU.md Phase 4 task_050.
/// </summary>
public sealed class ReflectionProposalStore
{
    private readonly string _storeDir;
    private readonly ILogger<ReflectionProposalStore> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ReflectionProposalStore(string dataRoot, ILogger<ReflectionProposalStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _storeDir = Path.Combine(dataRoot, "reflection_proposals");
        Directory.CreateDirectory(_storeDir);
    }

    /// <summary>
    ///     Save a proposal. Idempotent — overwrites if exists.
    /// </summary>
    public void Save(ReflectionProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        var path = GetPath(proposal.Id);
        var json = JsonSerializer.Serialize(proposal, JsonOpts);
        File.WriteAllText(path, json);
        _logger.LogDebug("[ProposalStore] Saved proposal {Id} ({Type})", proposal.Id, proposal.Type);
    }

    /// <summary>
    ///     Load all proposals, optionally filtered by status.
    /// </summary>
    public IReadOnlyList<ReflectionProposal> LoadAll(ProposalStatus? filter = null)
    {
        var files = Directory.GetFiles(_storeDir, "*.proposal.json");
        var proposals = new List<ReflectionProposal>();

        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var p = JsonSerializer.Deserialize<ReflectionProposal>(json, JsonOpts);
                if (p is not null && (filter is null || p.Status == filter))
                {
                    proposals.Add(p);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ProposalStore] Failed to deserialize {File}", file);
            }
        }

        return proposals.OrderByDescending(p => p.CreatedAt).ToList();
    }

    /// <summary>
    ///     Load a single proposal by Id.
    /// </summary>
    public ReflectionProposal? Get(string id)
    {
        var path = GetPath(id);
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ReflectionProposal>(json, JsonOpts);
    }

    /// <summary>
    ///     Update proposal status (approve / reject).
    /// </summary>
    public void UpdateStatus(string id, ProposalStatus status, string? reviewer = null, string? comment = null)
    {
        var p = Get(id);
        if (p is null)
        {
            _logger.LogWarning("[ProposalStore] Proposal {Id} not found for status update", id);
            return;
        }

        p.Status = status;
        p.Reviewer = reviewer;
        p.ReviewComment = comment;
        p.ReviewedAt = DateTimeOffset.UtcNow;
        Save(p);
        _logger.LogInformation("[ProposalStore] Proposal {Id} → {Status} by {Reviewer}", id, status, reviewer ?? "system");
    }

    /// <summary>
    ///     Delete a proposal (only pending ones).
    /// </summary>
    public bool Delete(string id)
    {
        var path = GetPath(id);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        _logger.LogDebug("[ProposalStore] Deleted proposal {Id}", id);
        return true;
    }

    private string GetPath(string id) => Path.Combine(_storeDir, $"{id}.proposal.json");
}

/// <summary>
///     A structured reflection proposal — NOT auto-applied, requires human approval.
///     Types: new_skill, routing_rule, peer_connection, trust_update, capability_declare.
/// </summary>
public sealed class ReflectionProposal
{
    /// <summary>ULID-like unique identifier.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..16];

    /// <summary>Proposal type.</summary>
    public ProposalType Type { get; init; }

    /// <summary>Proposal title for human review.</summary>
    public string Title { get; init; } = "";

    /// <summary>Human-readable rationale for this proposal.</summary>
    public string Rationale { get; init; } = "";

    /// <summary>Proposed content (skill content, routing rule JSON, etc.).</summary>
    public string Content { get; init; } = "";

    /// <summary>
    ///     For new_skill proposals: suggested skill Id.
    ///     For routing_rule proposals: rule name.
    ///     For peer_connection proposals: peer agentId.
    /// </summary>
    public string Target { get; init; } = "";

    /// <summary>Priority: low, medium, high, critical.</summary>
    public ProposalPriority Priority { get; init; } = ProposalPriority.Medium;

    /// <summary>Current status.</summary>
    public ProposalStatus Status { get; set; } = ProposalStatus.Pending;

    /// <summary>Who/what triggered this proposal (e.g. "peer circuit open", "routing failure").</summary>
    public string Trigger { get; init; } = "";

    /// <summary>Evidence supporting this proposal (structured JSON).</summary>
    public string EvidenceJson { get; init; } = "";

    /// <summary>UTC timestamp when proposal was generated.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>AgentId that generated this proposal.</summary>
    public string GeneratedBy { get; set; } = "";

    /// <summary>Who reviewed (or null if pending).</summary>
    public string? Reviewer { get; set; }

    /// <summary>Review comment.</summary>
    public string? ReviewComment { get; set; }

    /// <summary>UTC timestamp of review (or null if pending).</summary>
    public DateTimeOffset? ReviewedAt { get; set; }
}

/// <summary>Proposal types.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProposalType
{
    /// <summary>Create a new local skill.</summary>
    NewSkill,
    /// <summary>Update an existing routing rule.</summary>
    RoutingRule,
    /// <summary>Add or update a peer connection.</summary>
    PeerConnection,
    /// <summary>Update trust level for a peer.</summary>
    TrustUpdate,
    /// <summary>Declare or update a capability in the registry.</summary>
    CapabilityDeclare
}

/// <summary>Proposal priority.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProposalPriority
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>Proposal status.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProposalStatus
{
    Pending,
    Approved,
    Rejected,
    Expired
}
