using System.Diagnostics;
using System.Security.Cryptography;
using Hercules.Observability;

namespace Hercules.Mesh.Observability;

/// <summary>
///     W3C TraceContext + B3 Single Header propagation for inter-agent HTTP calls.
///     Supports inject (outbound) and extract (inbound) operations.
///     Spec: task_054 — centralized mesh observability.
/// </summary>
public sealed class TraceContextPropagator
{
    /// <summary>W3C TraceContext version (always 00 for current spec).</summary>
    private const string W3CVersion = "00";

    /// <summary>Valid W3C traceparent length (without null terminator).</summary>
    private const int W3CTraceParentLen = 55; // "00" + "-" + 32 + "-" + 16 + "-" + 2

    /// <summary>Valid W3C trace flags length.</summary>
    private const int W3CTraceFlagsLen = 2;

    private readonly bool _enableW3C;
    private readonly bool _enableB3;

    public TraceContextPropagator(string? propagationFormat)
    {
        var fmt = (propagationFormat ?? "w3c").ToLowerInvariant().Trim();
        _enableW3C = fmt is "w3c" or "both";
        _enableB3 = fmt is "b3" or "both";
        // Default to W3C for unknown/empty formats (safe fallback)
        if (!_enableW3C && !_enableB3) _enableW3C = true;
    }

    /// <summary>
    ///     Inject trace context from an Activity into outbound HTTP headers.
    ///     Returns headers dict (lowercase keys) for HttpRequestMessage.Headers.
    /// </summary>
    public Dictionary<string, string> Inject(Activity? activity, string? traceId = null, string? spanId = null)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Resolve IDs from Activity or use provided fallbacks
        var resolvedTraceId = !string.IsNullOrWhiteSpace(traceId)
            ? traceId
            : activity?.TraceId.ToHexString();
        var resolvedSpanId = !string.IsNullOrWhiteSpace(spanId)
            ? spanId
            : activity?.SpanId.ToHexString();

        if (string.IsNullOrWhiteSpace(resolvedTraceId))
        {
            // Generate a new trace ID if none exists
            resolvedTraceId = GenerateTraceId();
            resolvedSpanId = GenerateSpanId();
        }

        if (string.IsNullOrWhiteSpace(resolvedSpanId))
        {
            resolvedSpanId = GenerateSpanId();
        }

        // W3C TraceContext: traceparent = 00-{TraceId}-{SpanId}-{TraceFlags}
        if (_enableW3C)
        {
            var traceFlags = activity?.Context.TraceFlags == ActivityTraceFlags.Recorded ? "01" : "00";
            var traceParent = $"{W3CVersion}-{resolvedTraceId}-{resolvedSpanId}-{traceFlags}";
            headers["traceparent"] = traceParent;

            // Optionally add tracestate with agent identity
            // headers["tracestate"] = "hercules=local";
        }

        // B3 Single Header: {TraceId}-{SpanId}-{SamplingState}-{ParentSpanId?}
        if (_enableB3)
        {
            headers["x-b3-traceid"] = resolvedTraceId;
            headers["x-b3-spanid"] = resolvedSpanId;
            headers["x-b3-sampled"] = activity?.Context.TraceFlags == ActivityTraceFlags.Recorded ? "1" : "0";
        }

        return headers;
    }

    /// <summary>
    ///     Extract trace context from inbound HTTP response headers.
    /// </summary>
    public TraceContextCarrier Extract(IDictionary<string, string> headers)
    {
        var carrier = new TraceContextCarrier();

        if (headers is null || headers.Count == 0)
            return carrier;

        // Try W3C TraceContext first (preferred)
        if (_enableW3C && headers.TryGetValue("traceparent", out var traceparent))
        {
            ParseW3CTraceParent(traceparent, carrier);
        }

        // Fall back to or augment with B3
        if (_enableB3)
        {
            // B3 augments W3C — only fill missing values
            if (string.IsNullOrWhiteSpace(carrier.TraceId) &&
                headers.TryGetValue("x-b3-traceid", out var b3TraceId) &&
                !string.IsNullOrWhiteSpace(b3TraceId))
                carrier.TraceId = b3TraceId.Trim();

            if (string.IsNullOrWhiteSpace(carrier.SpanId) &&
                headers.TryGetValue("x-b3-spanid", out var b3SpanId) &&
                !string.IsNullOrWhiteSpace(b3SpanId))
                carrier.SpanId = b3SpanId.Trim();

            // B3 sampled augments W3C flags — B3 debug/sampled values override W3C
            // W3C sampled (01) is stronger than B3 debug (d); B3 sampled (1) is equivalent to W3C 01
            if (headers.TryGetValue("x-b3-sampled", out var b3Sampled) && !string.IsNullOrWhiteSpace(b3Sampled))
            {
                var sampled = b3Sampled.Trim();
                // "d" (debug) overrides "0" (not sampled); "1" (sampled) overrides "0"
                if (sampled is "d" or "1" && carrier.B3Sampled == "0")
                    carrier.B3Sampled = sampled;
                else if (string.IsNullOrWhiteSpace(carrier.B3Sampled))
                    carrier.B3Sampled = sampled;
            }

            // tracestate is B3-specific, no conflict with W3C
            if (string.IsNullOrWhiteSpace(carrier.TraceState) &&
                headers.TryGetValue("tracestate", out var traceState))
                carrier.TraceState = traceState;
        }

        return carrier;
    }

    /// <summary>
    ///     Extract trace context from inbound HTTP request headers.
    ///     Same as Extract but uses lowercase header names from HttpRequest.Headers.
    /// </summary>
    public TraceContextCarrier ExtractFromRequest(IDictionary<string, string> requestHeaders)
    {
        // Normalize keys to lowercase for lookup
        var normalized = new Dictionary<string, string>(requestHeaders.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in requestHeaders)
            normalized[kv.Key] = kv.Value;

        return Extract(normalized);
    }

    private static void ParseW3CTraceParent(string traceparent, TraceContextCarrier carrier)
    {
        if (string.IsNullOrWhiteSpace(traceparent))
            return;

        var traceparentTrimmed = traceparent.Trim();
        var parts = traceparentTrimmed.Split('-');

        // Valid W3C traceparent: version(2)-traceId(32)-spanId(16)-traceFlags(2)
        if (parts.Length < 4)
            return;

        var version = parts[0];
        var traceId = parts[1];
        var spanId = parts[2];
        var traceFlags = parts[3];

        // Reject future versions
        if (version != W3CVersion)
            return;

        // Validate lengths
        if (traceId.Length != 32 || spanId.Length != 16 || traceFlags.Length != W3CTraceFlagsLen)
            return;

        carrier.TraceParent = traceparentTrimmed;
        carrier.TraceId = traceId;
        carrier.SpanId = spanId;

        // Map W3C flags to B3 sampled
        carrier.B3Sampled = traceFlags == "01" ? "1" : "0";
    }

    /// <summary>
    ///     Create an ActivityContext from a TraceContextCarrier for child span creation.
    /// </summary>
    public static ActivityContext ToActivityContext(TraceContextCarrier carrier, ActivityTraceFlags flags = ActivityTraceFlags.None)
    {
        if (string.IsNullOrWhiteSpace(carrier.TraceId))
            return default;

        if (!string.IsNullOrWhiteSpace(carrier.SpanId))
        {
            var traceId = ActivityTraceId.CreateFromString(carrier.TraceId.AsSpan());
            var spanId = ActivitySpanId.CreateFromString(carrier.SpanId.AsSpan());
            return new ActivityContext(traceId, spanId, flags, carrier.TraceState);
        }

        var tid = ActivityTraceId.CreateFromString(carrier.TraceId.AsSpan());
        return new ActivityContext(tid, ActivitySpanId.CreateRandom(), flags, null);
    }

    private static string GenerateTraceId()
    {
        Span<byte> bytes = stackalloc byte[16];
        Random.Shared.NextBytes(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string GenerateSpanId()
    {
        Span<byte> bytes = stackalloc byte[8];
        Random.Shared.NextBytes(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
