using System.Diagnostics;
using System.Text.Json.Serialization;
using HerculesBus.Core;
using Hercules.Mesh.Policy;

namespace Hercules.Mesh.Audit;

/// <summary>
///     Inter-agent audit record for mesh delegation events (task_041).
///     Each delegation step — outbound send, inbound receive, task state change —
///     produces one record linked by TraceId / RootRequestId.
///     Sensitive fields (payload, tokens) are stored as hashes or omitted based on config.
/// </summary>
public sealed class InterAgentAuditRecord
{
    /// <summary>ULID-like unique ID for this audit entry.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = Ulid.NewId();

    /// <summary>Event type: outbound_delegation | inbound_delegation | task_state_change | delegation_result.</summary>
    [JsonPropertyName("event_type")]
    public string EventType { get; init; } = "";

    /// <summary>TraceId — correlates all events in a single user request across the mesh.</summary>
    [JsonPropertyName("trace_id")]
    public string? TraceId { get; init; }

    /// <summary>RootRequestId — the RequestId of the original top-level request.</summary>
    [JsonPropertyName("root_request_id")]
    public string? RootRequestId { get; init; }

    /// <summary>RequestId of this delegation step.</summary>
    [JsonPropertyName("request_id")]
    public string RequestId { get; init; } = "";

    /// <summary>AgentId of the sender (this agent).</summary>
    [JsonPropertyName("sender_agent_id")]
    public string SenderAgentId { get; init; } = "";

    /// <summary>AgentId of the receiver peer (null for inbound events).</summary>
    [JsonPropertyName("receiver_agent_id")]
    public string? ReceiverAgentId { get; init; }

    /// <summary>Intent / capability name being delegated.</summary>
    [JsonPropertyName("intent")]
    public string Intent { get; init; } = "";

    /// <summary>
    ///     SHA-256 hash of the payload (not the raw payload).
    ///     Omitted when payload redaction is enabled.
    /// </summary>
    [JsonPropertyName("payload_hash")]
    public string? PayloadHash { get; init; }

    /// <summary>Data sensitivity classification of the payload.</summary>
    [JsonPropertyName("data_classification")]
    public DataClassification DataClassification { get; init; } = DataClassification.Public;

    /// <summary>
    ///     Trust admission decision: Allowed | Denied | NeedsApproval.
    ///     Null for task state change events.
    /// </summary>
    [JsonPropertyName("policy_decision")]
    public string? PolicyDecision { get; init; }

    /// <summary>Reason for denial or restriction (null when Allowed).</summary>
    [JsonPropertyName("policy_reason")]
    public string? PolicyReason { get; init; }

    /// <summary>Estimated cost in USD (LLM tokens, transport, etc.).</summary>
    [JsonPropertyName("cost_usd")]
    public decimal? CostUsd { get; init; }

    /// <summary>Round-trip latency in milliseconds.</summary>
    [JsonPropertyName("latency_ms")]
    public double? LatencyMs { get; init; }

    /// <summary>
    ///     SHA-256 hash of the response body (not the raw response).
    ///     Omitted when payload redaction is enabled.
    /// </summary>
    [JsonPropertyName("response_hash")]
    public string? ResponseHash { get; init; }

    /// <summary>Final outcome of the delegation step.</summary>
    [JsonPropertyName("outcome")]
    public DelegationOutcome Outcome { get; init; } = DelegationOutcome.Unknown;

    /// <summary>Human-readable error message when outcome is not Ok.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>Delegation depth at this step (0 = root request).</summary>
    [JsonPropertyName("delegation_depth")]
    public int DelegationDepth { get; init; }

    /// <summary>Hop count from root request (1 = first delegation).</summary>
    [JsonPropertyName("hop_count")]
    public int HopCount { get; init; }

    /// <summary>UTC timestamp of this event.</summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Transport kind used: Http | Grpc | Bus.</summary>
    [JsonPropertyName("transport_kind")]
    public string? TransportKind { get; init; }

    /// <summary>Activity Id from System.Diagnostics.Activity (OTel correlation).</summary>
    [JsonPropertyName("activity_id")]
    public string? ActivityId { get; init; }
}

/// <summary>
///     Outcome of a delegation step.
/// </summary>
public enum DelegationOutcome
{
    Unknown,
    Ok,
    Error,
    Timeout,
    Rejected,
    PolicyDenied,
    SchemaMismatch,
    CircuitOpen,
    Expired
}
