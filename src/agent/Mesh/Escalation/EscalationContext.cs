namespace Hercules.Mesh.Escalation;

/// <summary>
///     Severity level of an escalation.
/// </summary>
public enum EscalationSeverity
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
///     Category of operation that triggered the escalation.
/// </summary>
public enum EscalationType
{
    /// <summary>Response confidence below threshold.</summary>
    LowConfidence,

    /// <summary>Budget or guardrail limit exceeded.</summary>
    BudgetExceeded,

    /// <summary>Policy denial (trust admission, tool policy, capability constraint).</summary>
    PolicyDenial,

    /// <summary>Ambiguous intent — could not route reliably.</summary>
    AmbiguousIntent,

    /// <summary>Destructive or irreversible operation requested.</summary>
    DestructiveOperation,

    /// <summary>Inter-agent delegation with insufficient trust level.</summary>
    DelegationTrustLow,

    /// <summary>High-cost LLM operation.</summary>
    HighCostOperation
}

/// <summary>
///     Context passed to the escalation service when creating an escalation.
/// </summary>
public sealed record EscalationContext
{
    /// <summary>Unique ID assigned by the service.</summary>
    public string EscalationId { get; init; } = "";

    /// <summary>Session or request ID that triggered the escalation.</summary>
    public string RequestId { get; init; } = "";

    /// <summary>Agent that originated the escalation.</summary>
    public string AgentId { get; init; } = "";

    /// <summary>Session ID for this conversation/request.</summary>
    public string SessionId { get; init; } = "";

    /// <summary>What triggered this escalation.</summary>
    public EscalationType Type { get; init; }

    /// <summary>How severe is this escalation.</summary>
    public EscalationSeverity Severity { get; init; }

    /// <summary>Human-readable description of what would happen if approved.</summary>
    public string ActionPlan { get; init; } = "";

    /// <summary>Additional context (LLM response text, tool name, budget details, etc.).</summary>
    public string Context { get; init; } = "";

    /// <summary>JSON payload of the pending action (tool arguments, delegation envelope, etc.).</summary>
    public string? PayloadJson { get; init; }

    /// <summary>Name of the tool or intent being escalated.</summary>
    public string? ToolOrIntentName { get; init; }

    /// <summary>Who or what requested this escalation (agent ID, "system", "budget-guard", etc.).</summary>
    public string RequestedBy { get; init; } = "agent";

    /// <summary>UTC timestamp when the escalation was created.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
