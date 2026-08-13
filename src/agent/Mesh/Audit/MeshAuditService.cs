using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hercules.Observability;
using Hercules.Mesh.Policy;
using Hercules.Mesh.Transport;
using HerculesBus.Core;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Audit;

/// <summary>
///     Central inter-agent audit service (task_041).
///     Orchestrates all IAuditSink implementations and records delegation events
///     from IntentRouter and TaskLifecycleProtocol.
///     Called by: IntentRouter (outbound delegation, result), TaskLifecycleProtocol (task state changes).
/// </summary>
public sealed class MeshAuditService : IDisposable
{
    private readonly IReadOnlyList<IAuditSink> _sinks;
    private readonly MeshAuditConfig _config;
    private readonly ILogger<MeshAuditService> _logger;

    public string ServiceName => "MeshAuditService";
    public bool IsEnabled => _config.Enabled;

    public MeshAuditService(
        IEnumerable<IAuditSink> sinks,
        MeshAuditConfig config,
        ILogger<MeshAuditService> logger)
    {
        _sinks = sinks?.ToList() ?? throw new ArgumentNullException(nameof(sinks));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Log an outbound delegation event — intent sent to a peer agent.
    ///     Call after TransportResult is received.
    /// </summary>
    public Task LogOutboundDelegationAsync(
        IntentEnvelope envelope,
        string senderAgentId,
        string receiverAgentId,
        TransportResult transportResult,
        string? policyDecision,
        string? policyReason,
        DataClassification classification,
        CancellationToken ct = default)
    {
        return LogAsync(BuildOutboundRecord(envelope, senderAgentId, receiverAgentId, transportResult,
            policyDecision, policyReason, classification), ct);
    }

    /// <summary>
    ///     Log the result of an outbound delegation (after response is received).
    /// </summary>
    public Task LogDelegationResultAsync(
        InterAgentAuditRecord outboundRecord,
        IntentResponse response,
        double latencyMs,
        CancellationToken ct = default)
    {
        var outcome = response.Status switch
        {
            "ok" => DelegationOutcome.Ok,
            "error" => DelegationOutcome.Error,
            "timeout" => DelegationOutcome.Timeout,
            "rejected" => DelegationOutcome.Rejected,
            "schema_mismatch" => DelegationOutcome.SchemaMismatch,
            _ => DelegationOutcome.Unknown
        };

        var record = new InterAgentAuditRecord
        {
            EventType = "delegation_result",
            TraceId = outboundRecord.TraceId,
            RootRequestId = outboundRecord.RootRequestId,
            RequestId = outboundRecord.RequestId,
            SenderAgentId = outboundRecord.SenderAgentId,
            ReceiverAgentId = outboundRecord.ReceiverAgentId,
            Intent = outboundRecord.Intent,
            PayloadHash = outboundRecord.PayloadHash,
            DataClassification = outboundRecord.DataClassification,
            PolicyDecision = outboundRecord.PolicyDecision,
            LatencyMs = latencyMs,
            ResponseHash = ComputeHash(response.Result),
            Outcome = outcome,
            Error = outcome != DelegationOutcome.Ok ? response.Error : null,
            DelegationDepth = outboundRecord.DelegationDepth,
            HopCount = outboundRecord.HopCount,
            TransportKind = outboundRecord.TransportKind,
            ActivityId = Activity.Current?.TraceId.ToString()
        };

        return LogAsync(record, ct);
    }

    /// <summary>
    ///     Log an inbound delegation event — intent received from a peer.
    /// </summary>
    public Task LogInboundDelegationAsync(
        IntentEnvelope envelope,
        string receiverAgentId,
        string? policyDecision,
        string? policyReason,
        DataClassification classification,
        CancellationToken ct = default)
    {
        var record = new InterAgentAuditRecord
        {
            EventType = "inbound_delegation",
            TraceId = envelope.TraceId,
            RootRequestId = envelope.Auth?.RootRequestId ?? envelope.RequestId,
            RequestId = envelope.RequestId,
            SenderAgentId = envelope.Sender,
            ReceiverAgentId = receiverAgentId,
            Intent = envelope.Intent,
            PayloadHash = _config.RedactPayloads ? null : ComputeHash(envelope.Payload),
            DataClassification = classification,
            PolicyDecision = policyDecision,
            PolicyReason = policyReason,
            DelegationDepth = envelope.Auth?.DelegationDepth ?? 0,
            HopCount = (envelope.Auth?.DelegationDepth ?? 0) + 1,
            TransportKind = null,
            ActivityId = Activity.Current?.TraceId.ToString()
        };

        return LogAsync(record, ct);
    }

    /// <summary>
    ///     Log a delegated task state change from TaskLifecycleProtocol.
    /// </summary>
    public Task LogTaskStateChangeAsync(
        string taskId,
        string requestId,
        string callerAgentId,
        string localAgentId,
        string intent,
        string fromState,
        string toState,
        DelegationOutcome outcome,
        string? error,
        double? latencyMs,
        CancellationToken ct = default)
    {
        var record = new InterAgentAuditRecord
        {
            EventType = "task_state_change",
            RequestId = requestId,
            TraceId = null,
            RootRequestId = null,
            SenderAgentId = callerAgentId,
            ReceiverAgentId = localAgentId,
            Intent = intent,
            Outcome = outcome,
            Error = error,
            LatencyMs = latencyMs,
            DelegationDepth = 0,
            HopCount = 0,
            TransportKind = null,
            ActivityId = Activity.Current?.TraceId.ToString()
        };

        return LogAsync(record, ct);
    }

    private InterAgentAuditRecord BuildOutboundRecord(
        IntentEnvelope envelope,
        string senderAgentId,
        string receiverAgentId,
        TransportResult transportResult,
        string? policyDecision,
        string? policyReason,
        DataClassification classification)
    {
        var traceId = envelope.TraceId ?? Ulid.NewId();
        var rootRequestId = envelope.Auth?.RootRequestId ?? envelope.RequestId;
        var depth = envelope.Auth?.DelegationDepth ?? 0;

        return new InterAgentAuditRecord
        {
            EventType = "outbound_delegation",
            TraceId = traceId,
            RootRequestId = rootRequestId,
            RequestId = envelope.RequestId,
            SenderAgentId = senderAgentId,
            ReceiverAgentId = receiverAgentId,
            Intent = envelope.Intent,
            PayloadHash = _config.RedactPayloads
                ? null
                : (_config.PayloadHashEnabled ? ComputeHash(envelope.Payload) : null),
            DataClassification = classification,
            PolicyDecision = policyDecision,
            PolicyReason = policyReason,
            CostUsd = _config.CostTrackingEnabled ? EstimateCost(envelope) : null,
            LatencyMs = transportResult.LatencyMs,
            ResponseHash = null, // set by LogDelegationResultAsync
            Outcome = ToOutcome(transportResult),
            Error = transportResult.ErrorMessage,
            DelegationDepth = depth,
            HopCount = depth + 1,
            TransportKind = transportResult.Kind.ToString(),
            ActivityId = Activity.Current?.TraceId.ToString()
        };
    }

    private static DelegationOutcome ToOutcome(TransportResult result)
    {
        if (result.IsSuccess) return DelegationOutcome.Ok;
        return result.Kind switch
        {
            TransportErrorKind.Timeout => DelegationOutcome.Timeout,
            TransportErrorKind.Rejected => DelegationOutcome.Rejected,
            TransportErrorKind.Unreachable => DelegationOutcome.Error,
            TransportErrorKind.TransportError => DelegationOutcome.Error,
            TransportErrorKind.SchemaMismatch => DelegationOutcome.SchemaMismatch,
            _ => DelegationOutcome.Unknown
        };
    }

    private static decimal EstimateCost(IntentEnvelope envelope)
    {
        // Rough estimate: ~$0.01 per delegation (LLM + transport)
        return 0.01m;
    }

    private string? ComputeHash(string? value)
    {
        if (!_config.PayloadHashEnabled || string.IsNullOrEmpty(value))
            return null;

        // Truncate before hashing if needed
        var input = value.Length > _config.MaxPayloadLengthForHash
            ? value[.._config.MaxPayloadLengthForHash]
            : value;

        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task LogAsync(InterAgentAuditRecord record, CancellationToken ct)
    {
        if (!_config.Enabled) return;

        // Sampling
        if (_config.SampleRate < 1.0 && Random.Shared.NextDouble() > _config.SampleRate)
        {
            _logger.LogDebug("[MeshAudit] Sampled out record {Id}", record.Id);
            return;
        }

        var tasks = _sinks
            .Where(s => s.IsEnabled)
            .Select(sink => FireAndForget(sink, record, ct));

        await Task.WhenAll(tasks);
    }

    private static async Task FireAndForget(IAuditSink sink, InterAgentAuditRecord record, CancellationToken ct)
    {
        try
        {
            await sink.WriteAsync(record, ct);
        }
        catch
        {
            // Fire-and-forget; sink is responsible for its own fault tolerance
        }
    }

    public void Dispose() { }
}
