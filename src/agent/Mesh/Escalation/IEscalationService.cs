namespace Hercules.Mesh.Escalation;

/// <summary>
///     Service for escalating ambiguous, low-confidence, policy-sensitive,
///     destructive or budget-exceeding operations to a human for confirmation.
/// </summary>
public interface IEscalationService
{
    /// <summary>Create a pending escalation.</summary>
    Task<EscalationResult> EscalateAsync(EscalationContext ctx, CancellationToken ct = default);

    /// <summary>Approve a pending escalation.</summary>
    Task<bool> ApproveAsync(string escalationId, string? resolvedBy = null, CancellationToken ct = default);

    /// <summary>Deny a pending escalation.</summary>
    Task<bool> DenyAsync(string escalationId, string? resolvedBy = null, CancellationToken ct = default);

    /// <summary>Batch-approve multiple escalations. Returns count of successfully approved.</summary>
    Task<int> BatchApproveAsync(IEnumerable<string> escalationIds, string? resolvedBy = null, CancellationToken ct = default);

    /// <summary>Get all pending escalations, optionally filtered by session and minimum severity.</summary>
    IReadOnlyList<EscalationResult> GetPending(string? sessionId = null, EscalationSeverity? minSeverity = null);

    /// <summary>Get a specific escalation by ID.</summary>
    EscalationResult? Get(string escalationId);

    /// <summary>Check whether a specific escalation has been approved.</summary>
    bool IsApproved(string escalationId);

    /// <summary>Check whether a specific escalation has been denied.</summary>
    bool IsDenied(string escalationId);

    /// <summary>Expire old pending escalations beyond the configured TTL.</summary>
    Task ExpireOldAsync(CancellationToken ct = default);
}
