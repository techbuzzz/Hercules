using System.Diagnostics;

namespace Hercules.Mesh.Observability;

/// <summary>
///     Centralized mesh observability: trace context propagation, mesh-specific span enrichment,
///     per-hop metrics, and structured redacted logs.
///     Spec: task_054.
/// </summary>
public interface IMeshObservabilityService
{
    /// <summary>Whether mesh observability is enabled.</summary>
    bool IsEnabled { get; }

    /// <summary>
    ///     Start a mesh span for an outbound delegation call.
    ///     Uses current Activity if available; creates a new one otherwise.
    /// </summary>
    Activity? StartMeshSpan(string operationName, string? peerAgentId = null, string? intent = null);

    /// <summary>
    ///     Start a mesh span as child of an incoming trace context (inbound delegation).
    /// </summary>
    Activity? StartMeshSpanFromContext(string operationName, TraceContextCarrier context, string? intent = null);

    /// <summary>
    ///     Inject trace context into HTTP request headers for outbound calls.
    ///     Returns headers dict with propagation format set in config (W3C, B3, or both).
    /// </summary>
    Dictionary<string, string> InjectTraceContext(Activity? activity, string? traceId = null, string? spanId = null);

    /// <summary>
    ///     Extract trace context from HTTP response headers.
    /// </summary>
    TraceContextCarrier ExtractTraceContext(IDictionary<string, string> responseHeaders);

    /// <summary>
    ///     Extract trace context from inbound HTTP request headers.
    /// </summary>
    TraceContextCarrier ExtractFromRequestHeaders(IDictionary<string, string> requestHeaders);

    /// <summary>
    ///     Record a structured mesh event on a span with enriched mesh tags.
    /// </summary>
    void RecordMeshEvent(Activity? activity, string eventName, string? intent = null,
        string? senderAgentId = null, string? receiverAgentId = null,
        string? routingDecision = null, int? hopCount = null,
        double? latencyMs = null, string? error = null);

    /// <summary>
    ///     Enrich a span with mesh-specific tags (safe: never includes raw payload).
    /// </summary>
    void EnrichSpanWithMeshTags(Activity? activity, string? intent, string? senderAgentId,
        string? receiverAgentId, string? transportKind, int? delegationDepth,
        int? hopCount, string? routingDecision);

    /// <summary>
    ///     Record a mesh metric (counter or gauge) for observability backends.
    ///     Supported metrics: delegation_latency_ms, hop_count, delegation, routing_decision, retry_attempt.
    /// </summary>
    void RecordMeshMetric(string metricName, double value, string? peerAgentId = null,
        string? intent = null, string? outcome = null);
}
