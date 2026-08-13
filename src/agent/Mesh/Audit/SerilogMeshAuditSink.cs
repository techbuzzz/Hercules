using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Audit;

/// <summary>
///     Serilog-structured log sink for inter-agent audit (task_041).
///     Writes structured log events using the standard ILogger&lt;MeshAuditService&gt; abstraction.
///     Template: <c>[MeshAudit] event_type={EventType} trace_id={TraceId} sender={Sender} receiver={Receiver} ...</c>
/// </summary>
public sealed class SerilogMeshAuditSink : IAuditSink
{
    private readonly ILogger<MeshAuditService> _logger;
    private readonly MeshAuditConfig _config;

    public string Name => "SerilogMeshAuditSink";
    public bool IsEnabled => true;

    public SerilogMeshAuditSink(ILogger<MeshAuditService> logger, MeshAuditConfig config)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public Task WriteAsync(InterAgentAuditRecord record, CancellationToken ct = default)
    {
        if (!_config.Enabled) return Task.CompletedTask;

        switch (record.Outcome)
        {
            case DelegationOutcome.Ok:
                _logger.LogInformation(
                    "[MeshAudit] {EventType} trace_id={TraceId} request_id={RequestId} " +
                    "sender={SenderAgentId} receiver={ReceiverAgentId} intent={Intent} " +
                    "outcome={Outcome} latency_ms={LatencyMs} cost_usd={CostUsd}",
                    record.EventType, record.TraceId, record.RequestId,
                    record.SenderAgentId, record.ReceiverAgentId, record.Intent,
                    record.Outcome, record.LatencyMs, record.CostUsd);
                break;

            case DelegationOutcome.PolicyDenied:
                _logger.LogWarning(
                    "[MeshAudit] {EventType} request_id={RequestId} sender={SenderAgentId} " +
                    "receiver={ReceiverAgentId} intent={Intent} outcome={Outcome} " +
                    "policy_decision={PolicyDecision} policy_reason={PolicyReason}",
                    record.EventType, record.RequestId, record.SenderAgentId,
                    record.ReceiverAgentId, record.Intent, record.Outcome,
                    record.PolicyDecision, record.PolicyReason);
                break;

            case DelegationOutcome.Timeout:
            case DelegationOutcome.CircuitOpen:
                _logger.LogWarning(
                    "[MeshAudit] {EventType} request_id={RequestId} sender={SenderAgentId} " +
                    "receiver={ReceiverAgentId} outcome={Outcome} latency_ms={LatencyMs}",
                    record.EventType, record.RequestId, record.SenderAgentId,
                    record.ReceiverAgentId, record.Outcome, record.LatencyMs);
                break;

            case DelegationOutcome.Error:
                _logger.LogError(
                    "[MeshAudit] {EventType} request_id={RequestId} sender={SenderAgentId} " +
                    "receiver={ReceiverAgentId} outcome={Outcome} error={Error}",
                    record.EventType, record.RequestId, record.SenderAgentId,
                    record.ReceiverAgentId, record.Outcome, record.Error);
                break;

            default:
                _logger.LogInformation(
                    "[MeshAudit] {EventType} request_id={RequestId} sender={SenderAgentId} " +
                    "receiver={ReceiverAgentId} intent={Intent} outcome={Outcome}",
                    record.EventType, record.RequestId, record.SenderAgentId,
                    record.ReceiverAgentId, record.Intent, record.Outcome);
                break;
        }

        return Task.CompletedTask;
    }

    public void Dispose() { }
}
