using System.Diagnostics;
using System.Diagnostics.Metrics;
using Hercules.Config;
using Hercules.Observability;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Observability;

/// <summary>
///     Centralized mesh observability service (task_054).
///     - Trace context propagation across agent hops (W3C TraceContext + B3)
///     - Mesh-specific span enrichment (intent, hop_count, routing_decision, transport)
///     - Per-hop metrics (counters, histograms)
///     - Structured redacted logs (never logs raw payloads)
///     Built on top of existing OtelService / OtelSetup infrastructure.
/// </summary>
public sealed class MeshObservabilityService : IMeshObservabilityService
{
    /// <summary>Metric name prefix for mesh observability metrics.</summary>
    private const string MetricPrefix = "hercules.mesh";

    private readonly MeshCentralizedObservabilityConfig _config;
    private readonly IOtelService _otel;
    private readonly ILogger<MeshObservabilityService> _logger;
    private readonly TraceContextPropagator _propagator;
    private readonly MeshDiagnosticsService? _diagnostics;

    // Metrics instruments (registered with OtelSetup.Meter)
    private readonly Counter<long>? _meshDelegationCounter;
    private readonly Counter<long>? _meshRoutingCounter;
    private readonly Histogram<double>? _meshLatencyHistogram;
    private readonly Histogram<double>? _meshHopHistogram;

    public bool IsEnabled => _config.Enabled;

    public MeshObservabilityService(
        MeshCentralizedObservabilityConfig config,
        IOtelService otel,
        ILogger<MeshObservabilityService> logger,
        MeshDiagnosticsService? diagnostics = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _otel = otel ?? throw new ArgumentNullException(nameof(otel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _diagnostics = diagnostics;

        _propagator = new TraceContextPropagator(_config.PropagationFormat);

        if (_config.Enabled && _config.EnableMetrics)
        {
            _meshDelegationCounter = OtelSetup.Meter.CreateCounter<long>(
                $"{MetricPrefix}.delegations_total",
                description: "Total number of inter-agent delegations");

            _meshRoutingCounter = OtelSetup.Meter.CreateCounter<long>(
                $"{MetricPrefix}.routing_decisions_total",
                description: "Total number of mesh routing decisions");

            _meshLatencyHistogram = OtelSetup.Meter.CreateHistogram<double>(
                $"{MetricPrefix}.delegation_latency_ms",
                unit: "ms",
                description: "Latency of inter-agent delegation calls");

            _meshHopHistogram = OtelSetup.Meter.CreateHistogram<double>(
                $"{MetricPrefix}.hop_count",
                description: "Number of hops in a delegation chain");
        }
    }

    /// <inheritdoc />
    public Activity? StartMeshSpan(string operationName, string? peerAgentId = null, string? intent = null)
    {
        if (!_config.Enabled) return null;

        // Try to use current Activity as parent if exists
        var parentActivity = Activity.Current;
        Activity? span;

        if (parentActivity is not null)
        {
            span = _otel.StartActivity(operationName, parentActivity.Context, ActivityKind.Internal);
        }
        else
        {
            span = _otel.StartActivity(operationName, ActivityKind.Internal);
        }

        if (span is not null)
        {
            span.SetTag("mesh.operation", operationName);
            if (!string.IsNullOrWhiteSpace(peerAgentId))
                span.SetTag("mesh.peer_agent_id", peerAgentId);
            if (!string.IsNullOrWhiteSpace(intent))
                span.SetTag("mesh.intent", Truncate(intent, _config.MaxTagValueLength));
            span.SetTag("mesh.local_agent_span", true);
        }

        return span;
    }

    /// <inheritdoc />
    public Activity? StartMeshSpanFromContext(string operationName, TraceContextCarrier context, string? intent = null)
    {
        if (!_config.Enabled || !context.HasTrace) return null;

        var activityContext = TraceContextPropagator.ToActivityContext(context);
        if (activityContext == default) return null;

        var span = _otel.StartActivity(operationName, activityContext, ActivityKind.Internal);
        if (span is not null)
        {
            span.SetTag("mesh.operation", operationName);
            span.SetTag("mesh.trace_id", context.TraceId ?? "");
            span.SetTag("mesh.span_id", context.SpanId ?? "");
            span.SetTag("mesh.inbound_hop", true);
            if (!string.IsNullOrWhiteSpace(intent))
                span.SetTag("mesh.intent", Truncate(intent, _config.MaxTagValueLength));
        }

        return span;
    }

    /// <inheritdoc />
    public Dictionary<string, string> InjectTraceContext(Activity? activity, string? traceId = null, string? spanId = null)
    {
        if (!_config.Enabled || !_config.EnableTraceContextPropagation)
            return new Dictionary<string, string>();

        return _propagator.Inject(activity, traceId, spanId);
    }

    /// <inheritdoc />
    public TraceContextCarrier ExtractTraceContext(IDictionary<string, string> responseHeaders)
    {
        if (!_config.Enabled || !_config.EnableTraceContextExtraction)
            return new TraceContextCarrier();

        return _propagator.Extract(responseHeaders);
    }

    /// <inheritdoc />
    public TraceContextCarrier ExtractFromRequestHeaders(IDictionary<string, string> requestHeaders)
    {
        if (!_config.Enabled || !_config.EnableTraceContextExtraction)
            return new TraceContextCarrier();

        return _propagator.ExtractFromRequest(requestHeaders);
    }

    /// <inheritdoc />
    public void RecordMeshEvent(Activity? activity, string eventName, string? intent = null,
        string? senderAgentId = null, string? receiverAgentId = null,
        string? routingDecision = null, int? hopCount = null,
        double? latencyMs = null, string? error = null)
    {
        if (!_config.Enabled) return;

        var tags = new List<KeyValuePair<string, object?>>(8);
        if (!string.IsNullOrWhiteSpace(intent))
            tags.Add(new("mesh.intent", Truncate(intent, _config.MaxTagValueLength)));
        if (!string.IsNullOrWhiteSpace(senderAgentId))
            tags.Add(new("mesh.sender_agent_id", senderAgentId));
        if (!string.IsNullOrWhiteSpace(receiverAgentId))
            tags.Add(new("mesh.receiver_agent_id", receiverAgentId));
        if (!string.IsNullOrWhiteSpace(routingDecision))
            tags.Add(new("mesh.routing_decision", routingDecision));
        if (hopCount.HasValue)
            tags.Add(new("mesh.hop_count", (object)hopCount.Value));
        if (latencyMs.HasValue)
            tags.Add(new("mesh.latency_ms", latencyMs.Value));
        if (!string.IsNullOrWhiteSpace(error))
        {
            tags.Add(new("mesh.error", Truncate(error, _config.MaxTagValueLength)));
            _otel.SetErrorStatus(activity, error);
        }

        _otel.AddEvent(activity, eventName, tags.ToArray());
    }

    /// <inheritdoc />
    public void EnrichSpanWithMeshTags(Activity? activity, string? intent, string? senderAgentId,
        string? receiverAgentId, string? transportKind, int? delegationDepth,
        int? hopCount, string? routingDecision)
    {
        if (!_config.Enabled || !_config.EnableSpanEnrichment || activity is null)
            return;

        var tags = new List<KeyValuePair<string, object?>>(8);
        if (!string.IsNullOrWhiteSpace(intent))
            tags.Add(new("mesh.intent", Truncate(intent, _config.MaxTagValueLength)));
        if (!string.IsNullOrWhiteSpace(senderAgentId))
            tags.Add(new("mesh.sender_agent_id", senderAgentId));
        if (!string.IsNullOrWhiteSpace(receiverAgentId))
            tags.Add(new("mesh.receiver_agent_id", receiverAgentId));
        if (!string.IsNullOrWhiteSpace(transportKind))
            tags.Add(new("mesh.transport_kind", transportKind));
        if (delegationDepth.HasValue)
            tags.Add(new("mesh.delegation_depth", (object)delegationDepth.Value));
        if (hopCount.HasValue)
            tags.Add(new("mesh.hop_count", (object)hopCount.Value));
        if (!string.IsNullOrWhiteSpace(routingDecision))
            tags.Add(new("mesh.routing_decision", routingDecision));

        if (tags.Count > 0)
            _otel.SetTags(activity, tags.ToArray());
    }

    /// <inheritdoc />
    public void RecordMeshMetric(string metricName, double value, string? peerAgentId = null,
        string? intent = null, string? outcome = null)
    {
        if (!_config.Enabled || !_config.EnableMetrics) return;

        var tags = new List<KeyValuePair<string, object?>>(3);
        if (!string.IsNullOrWhiteSpace(peerAgentId)) tags.Add(new("peer_agent_id", peerAgentId));
        if (!string.IsNullOrWhiteSpace(intent)) tags.Add(new("intent", Truncate(intent, _config.MaxTagValueLength)));
        if (!string.IsNullOrWhiteSpace(outcome)) tags.Add(new("outcome", outcome));
        var tagArray = tags.ToArray();

        // task_093: forward counter events to the in-memory diagnostics sink so
        // the web UI can display totals and per-capability / per-peer breakdowns.
        // Histogram metrics (latency, hop_count) are not counted.
        _diagnostics?.RecordMetric(metricName, value, intent: intent, peerAgentId: peerAgentId);

        switch (metricName)
        {
            case "delegation_latency_ms":
                _meshLatencyHistogram?.Record(value, tagArray);
                break;
            case "hop_count":
                _meshHopHistogram?.Record(value, tagArray);
                break;
            case "delegation":
                _meshDelegationCounter?.Add((long)value, tagArray);
                break;
            case "routing_decision":
                _meshRoutingCounter?.Add((long)value, tagArray);
                break;
            case "retry_attempt":
                // retry_attempt counter — uses delegation counter with retry tag
                _meshDelegationCounter?.Add((long)value, tagArray);
                break;
            case "circuit_breaker_state_change":
                // task_093: not wired to a dedicated OTel counter (we have no
                // circuit_breaker_state_change counter on the meter), but the
                // diagnostics sink above already incremented the in-memory total.
                break;
            default:
                _logger.LogDebug("[MeshObs] Unknown metric {MetricName}={Value}", metricName, value);
                break;
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;
        return value[..maxLength] + "…";
    }
}
