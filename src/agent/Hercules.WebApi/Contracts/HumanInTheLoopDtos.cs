namespace Hercules.WebApi.Contracts;

/// <summary>
/// Response shapes for the human-in-the-loop endpoints (approvals and escalations).
/// <para>
/// Stage 0: Studio's client is generated from the agent's OpenAPI document. Endpoints
/// returning anonymous objects produce no response schema, so Studio's types were
/// hand-written and could drift silently. These named shapes let the endpoints
/// declare themselves via <c>.Produces&lt;T&gt;(200)</c> without changing the JSON.
/// </para>
/// <para>
/// Non-nullable members use <c>required</c>: without it the emitter omits them from the
/// schema's <c>required</c> array and every field generates as optional, so the client
/// stops reflecting what the agent actually serialises.
/// </para>
/// </summary>
public sealed class PendingApprovalsResponseDto
{
    public required int Count { get; init; }
    public required IReadOnlyList<ApprovalDto> Approvals { get; init; }
}

public sealed class ApprovalDto
{
    public required string RequestId { get; init; }
    public string? SessionId { get; init; }
    public required string ToolName { get; init; }
    public string? ArgumentsJson { get; init; }
    public string? Reason { get; init; }
    public required string RequestedAt { get; init; }
    public required string Status { get; init; }
}

public sealed class PendingEscalationsResponseDto
{
    public required int Count { get; init; }
    public required IReadOnlyList<EscalationDto> Escalations { get; init; }
}

public sealed class EscalationDto
{
    public required string EscalationId { get; init; }
    public string? RequestId { get; init; }
    public string? SessionId { get; init; }
    public string? Type { get; init; }
    public required string Severity { get; init; }
    public required string Status { get; init; }
    public string? ActionPlan { get; init; }
    public string? Context { get; init; }
    public string? PayloadJson { get; init; }
    public string? ToolOrIntentName { get; init; }
    public string? RequestedBy { get; init; }
    public required string CreatedAt { get; init; }
    public string? ResolvedAt { get; init; }
    public string? ResolvedBy { get; init; }
}