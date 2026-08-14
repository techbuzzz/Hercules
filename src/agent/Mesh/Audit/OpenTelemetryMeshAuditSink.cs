using System.Diagnostics;
using Hercules.Observability;

namespace Hercules.Mesh.Audit;

/// <summary>
///     OpenTelemetry Activity span sink for inter-agent audit (task_041).
///     Creates a child Activity for each delegation event using the shared OtelSetup.Source.
///     Tags are set for all non-sensitive fields; raw payloads are never added as tags.
/// </summary>
public sealed class OpenTelemetryMeshAuditSink : IAuditSink
{
    private readonly ActivitySource _source;
    private readonly MeshAuditConfig _config;

    public string Name => "OpenTelemetryMeshAuditSink";
    public bool IsEnabled => _config.Enabled && _source.HasListeners();

    public OpenTelemetryMeshAuditSink(MeshAuditConfig config)
    {
        _source = OtelSetup.Source;
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public Task WriteAsync(InterAgentAuditRecord record, CancellationToken ct = default)
    {
        if (!IsEnabled) return Task.CompletedTask;

        using var activity = _source.StartActivity(
            $"mesh.{record.EventType}",
            ActivityKind.Internal);

        if (activity is null) return Task.CompletedTask;

        if (!string.IsNullOrEmpty(record.TraceId))
            activity.SetTag("mesh.trace_id", record.TraceId);

        activity.SetTag("mesh.event_type", record.EventType);
        activity.SetTag("mesh.request_id", record.RequestId);
        activity.SetTag("mesh.sender_agent_id", record.SenderAgentId);
        activity.SetTag("mesh.receiver_agent_id", record.ReceiverAgentId ?? "");
        activity.SetTag("mesh.intent", record.Intent);
        activity.SetTag("mesh.data_classification", record.DataClassification.ToString());
        activity.SetTag("mesh.outcome", record.Outcome.ToString());
        activity.SetTag("mesh.delegation_depth", record.DelegationDepth);
        activity.SetTag("mesh.hop_count", record.HopCount);

        if (record.LatencyMs.HasValue)
            activity.SetTag("mesh.latency_ms", record.LatencyMs.Value);

        if (record.CostUsd.HasValue)
            activity.SetTag("mesh.cost_usd", (double)record.CostUsd.Value);

        if (record.PolicyDecision is not null)
            activity.SetTag("mesh.policy_decision", record.PolicyDecision);

        if (record.PolicyReason is not null)
            activity.SetTag("mesh.policy_reason", record.PolicyReason);

        if (record.PayloadHash is not null)
            activity.SetTag("mesh.payload_hash", record.PayloadHash);

        if (record.ResponseHash is not null)
            activity.SetTag("mesh.response_hash", record.ResponseHash);

        if (record.TransportKind is not null)
            activity.SetTag("mesh.transport_kind", record.TransportKind);

        if (record.Error is not null)
        {
            activity.SetTag("mesh.error", record.Error);
            activity.SetStatus(ActivityStatusCode.Error, record.Error);
        }
        else if (record.Outcome == DelegationOutcome.Ok)
        {
            activity.SetStatus(ActivityStatusCode.Ok);
        }

        return Task.CompletedTask;
    }

    public void Dispose() { }
}
