namespace Hercules.Mesh.Escalation;

/// <summary>
///     Resolution status of an escalation.
/// </summary>
public enum EscalationStatus
{
    Pending,
    Approved,
    Denied,
    Expired
}

/// <summary>
///     Result of an escalation request — stored and returned to callers.
/// </summary>
public sealed record EscalationResult
{
    /// <summary>Unique escalation ID.</summary>
    public string EscalationId { get; init; } = "";

    public string RequestId { get; init; } = "";
    public string SessionId { get; init; } = "";
    public EscalationType Type { get; init; }
    public EscalationSeverity Severity { get; init; }
    public EscalationStatus Status { get; init; } = EscalationStatus.Pending;
    public string ActionPlan { get; init; } = "";
    public string Context { get; init; } = "";
    public string? PayloadJson { get; init; }
    public string? ToolOrIntentName { get; init; }
    public string RequestedBy { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime? ResolvedAt { get; init; }
    public string? ResolvedBy { get; init; }
}
