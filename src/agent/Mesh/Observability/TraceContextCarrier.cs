namespace Hercules.Mesh.Observability;

/// <summary>
///     Carries distributed trace context across agent hops.
///     Serialized into HTTP headers for propagation and deserialized on receive.
///     Spec: task_054 — centralized mesh observability.
/// </summary>
public sealed class TraceContextCarrier
{
    /// <summary>16-byte hex-encoded trace ID (W3C TraceContext) or 32-char hex (B3).</summary>
    public string? TraceId { get; set; }

    /// <summary>8-byte hex-encoded span ID (W3C TraceContext) or 16-char hex (B3).</summary>
    public string? SpanId { get; set; }

    /// <summary>W3C traceparent header value (version-traceId-spanId-traceFlags).</summary>
    public string? TraceParent { get; set; }

    /// <summary>W3C tracestate header value (key=value,key=value).</summary>
    public string? TraceState { get; set; }

    /// <summary>B3 sampled flag: "1" = sampled, "0" = not sampled, "d" = debug.</summary>
    public string? B3Sampled { get; set; }

    /// <summary>Whether this carrier holds a valid trace context (not just empty values).</summary>
    public bool HasTrace => !string.IsNullOrWhiteSpace(TraceId);
}
