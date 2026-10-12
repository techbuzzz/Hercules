using System.Diagnostics;
using Hercules.Agent;
using HerculesBus.Core;
using Hercules.Mesh.Audit;
using Hercules.Mesh.Escalation;
using Hercules.Mesh.Observability;
using Hercules.Mesh.Policy;
using Hercules.Mesh.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hercules.Mesh;

/// <summary>
///     Маршрутизатор intent'ов: решает, кто должен обработать запрос —
///     локальный агент или peer-агент из mesh.
///     Алгоритм:
///     1. Проверить локальные capabilities (навыки агента).
///     2. Если локальный навык найден — обработать локально.
///     3. Если нет — найти peer'а в CapabilityRegistry по intent.
///     4. Отправить intent через IntentTransport.
///     Спецификация: docs/AGENT-MESH-RU.md §5, docs/ROADMAP-RU.md Phase 3.
/// </summary>
public sealed class IntentRouter
{
    private readonly AgentCore _agent;
    private readonly AgentManifestService _manifestService;
    private readonly CapabilityRegistry _registry;
    private readonly ITransport _transport;
    private readonly MeshAuditService? _auditService;
    private readonly ITrustAdmissionPolicy? _trustPolicy;
    private readonly IEscalationService? _escalationService;
    private readonly IMeshObservabilityService? _observability;
    private readonly ILogger<IntentRouter> _logger;

    public IntentRouter(
        AgentCore agent,
        CapabilityRegistry registry,
        ITransport transport,
        AgentManifestService manifestService,
        MeshAuditService? auditService = null,
        ITrustAdmissionPolicy? trustPolicy = null,
        IEscalationService? escalationService = null,
        IMeshObservabilityService? observability = null,
        ILogger<IntentRouter>? logger = null)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _manifestService = manifestService ?? throw new ArgumentNullException(nameof(manifestService));
        _auditService = auditService;
        _trustPolicy = trustPolicy;
        _escalationService = escalationService;
        _observability = observability;
        // [task_087] optional logger — defaults to a NullLogger so existing DI
        // wiring (positional args in MeshServiceExtensions) keeps working.
        _logger = logger ?? NullLogger<IntentRouter>.Instance;
    }

    /// <summary>
    ///     Минимальная уверенность локального навыка, при которой intent обрабатывается локально.
    ///     Если ниже — intent пересылается peer'у (если есть).
    /// </summary>
    public double LocalConfidenceThreshold { get; set; } = 0.5;

    /// <summary>
    ///     Маршрутизировать intent: локально или peer-агенту.
    ///     Возвращает ответ (локальный или от peer'а).
    /// </summary>
    public async Task<IntentResponse> RouteAsync(IntentEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.RequestId);

        // 1. Проверяем локальные capabilities — есть ли у нас навык для этого intent
        ManifestCapability? localCap = FindLocalCapability(envelope.Intent);
        if (localCap is not null)
        {
            // Обрабатываем локально
            return await ProcessLocallyAsync(envelope, ct);
        }

        // 2. Ищем peer'а в registry по capability name
        List<RegistryAgentEntry> peers = _registry.FindByCapability(envelope.Intent);
        var ownAgentId = _manifestService.Current.AgentId;
        RegistryAgentEntry? peer = peers.FirstOrDefault(p => !p.AgentId.Equals(ownAgentId, StringComparison.OrdinalIgnoreCase));

        if (peer is null)
        {
            // 3. Ищем peer'а по фразе-приёмнику (semantic lookup)
            List<RegistryAgentEntry> phrasePeers = _registry.FindByPhrase(envelope.Intent);
            peer = phrasePeers.FirstOrDefault(p => !p.AgentId.Equals(ownAgentId, StringComparison.OrdinalIgnoreCase));
        }

        if (peer is null)
        {
            // Никто в mesh не умеет обрабатывать этот intent — fallback на локальную обработку
            return await ProcessLocallyAsync(envelope, ct);
        }

        // 4. Trust admission check before delegation
        string? policyDecision = null;
        string? policyReason = null;
        if (_trustPolicy is not null)
        {
            var ctx = new TrustAdmissionContext
            {
                CallerIdentity = null,
                Envelope = envelope,
                TargetAgentId = peer.AgentId
            };
            var admission = _trustPolicy.Evaluate(ctx);
            policyDecision = admission.IsAllowed ? "Allowed" : "Denied";
            policyReason = admission.DenialReason;

            // [task_049] Escalate trust admission denials
            if (!admission.IsAllowed && _escalationService is not null)
            {
                var escCtx = new EscalationContext
                {
                    RequestId = envelope.RequestId,
                    AgentId = ownAgentId,
                    SessionId = envelope.RequestId,
                    Type = EscalationType.DelegationTrustLow,
                    Severity = EscalationSeverity.High,
                    ActionPlan = $"Delegate intent '{envelope.Intent}' to peer '{peer.AgentId}'",
                    Context = $"Trust admission denied: {admission.DenialReason}",
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(envelope),
                    ToolOrIntentName = envelope.Intent,
                    RequestedBy = "mesh"
                };
                // [task_087] Was `_ = _escalationService.EscalateAsync(...)` —
                // fire-and-forget that silently swallowed exceptions. Wrap the
                // call so that any failure inside the escalation pipeline is
                // surfaced through the logger instead of disappearing. The
                // escalation is still non-blocking (background Task) so the
                // routing hot path is not slowed down.
                _ = EscalateSafelyAsync(escCtx, ct, envelope.RequestId);
            }
        }

        // 5. Start mesh delegation span with trace context propagation
        var span = _observability?.StartMeshSpan("IntentRouter.Delegate",
            peerAgentId: peer.AgentId, intent: envelope.Intent);
        try
        {
            // Enrich span with mesh tags
            if (span is not null)
            {
                _ = _observability!.InjectTraceContext(span); // inject for outbound headers
                _observability.EnrichSpanWithMeshTags(span,
                    intent: envelope.Intent,
                    senderAgentId: ownAgentId,
                    receiverAgentId: peer.AgentId,
                    transportKind: null,
                    delegationDepth: envelope.Auth?.DelegationDepth,
                    hopCount: (envelope.Auth?.DelegationDepth ?? 0) + 1,
                    routingDecision: policyDecision);
            }

            var sendStarted = DateTimeOffset.UtcNow;
            var result = await _transport.SendAsync(peer.AgentId, envelope, ct);
            var latencyMs = (DateTimeOffset.UtcNow - sendStarted).TotalMilliseconds;

            // Build outbound record for audit
            var outboundRecord = BuildOutboundRecord(envelope, ownAgentId, peer.AgentId,
                result, policyDecision, policyReason);

            // Log outbound delegation (fire-and-forget)
            if (_auditService is not null)
            {
                await _auditService.LogOutboundDelegationAsync(
                    envelope, ownAgentId, peer.AgentId, result,
                    policyDecision, policyReason, Policy.DataClassification.Public, ct);
            }

            // Emit delegation metric
            _observability?.RecordMeshMetric("delegation", 1,
                peerAgentId: peer.AgentId, intent: envelope.Intent,
                outcome: result.Response?.IsSuccess == true ? "success" : "failure");

            if (result.Response is not null)
            {
                // Log delegation result (includes response hash)
                if (_auditService is not null)
                {
                    await _auditService.LogDelegationResultAsync(outboundRecord, result.Response, latencyMs, ct);
                }
                _observability?.RecordMeshEvent(span, "delegation.success",
                    intent: envelope.Intent, senderAgentId: ownAgentId,
                    receiverAgentId: peer.AgentId, latencyMs: latencyMs,
                    routingDecision: policyDecision);
                _observability?.RecordMeshMetric("delegation_latency_ms", latencyMs,
                    peerAgentId: peer.AgentId, intent: envelope.Intent,
                    outcome: "success");
                return result.Response;
            }

            _observability?.RecordMeshEvent(span, "delegation.failure",
                intent: envelope.Intent, senderAgentId: ownAgentId,
                receiverAgentId: peer.AgentId, latencyMs: latencyMs,
                error: result.ErrorMessage);
            _observability?.RecordMeshMetric("delegation_latency_ms", latencyMs,
                peerAgentId: peer.AgentId, intent: envelope.Intent,
                outcome: "failure");

            return IntentResponse.Failed(
                envelope.RequestId,
                peer.AgentId,
                result.ErrorMessage ?? "Transport error",
                envelope.TraceId);
        }
        finally
        {
            span?.Stop();
        }
    }

    /// <summary>
    ///     Обработать intent локально (через AgentCore).
    /// </summary>
    private async Task<IntentResponse> ProcessLocallyAsync(IntentEnvelope envelope, CancellationToken ct)
    {
        var ownAgentId = _manifestService.Current.AgentId;

        try
        {
            // Payload — это текст запроса пользователя
            var message = envelope.Payload;
            AgentResponse response = await _agent.ProcessMessageAsync(message, ct);

            var confidence = response.Confidence switch
            {
                "high" => 0.9,
                "medium" => 0.5,
                _ => 0.2
            };

            return IntentResponse.Ok(
                envelope.RequestId,
                ownAgentId,
                response.Answer,
                response.Mode,
                response.UsedSkill?.Meta.Name,
                confidence,
                envelope.TraceId);
        }
        catch (OperationCanceledException)
        {
            return IntentResponse.TimedOut(envelope.RequestId, ownAgentId, envelope.TraceId);
        }
        catch (Exception ex)
        {
            return IntentResponse.Failed(envelope.RequestId, ownAgentId,
                $"{ex.GetType().Name}: {ex.Message}", envelope.TraceId);
        }
    }

    /// <summary>
    ///     Найти локальную capability по имени intent.
    ///     Проверяет, есть ли у агента навык, чьё имя совпадает с intent.
    /// </summary>
    private ManifestCapability? FindLocalCapability(string intent)
    {
        if (string.IsNullOrWhiteSpace(intent))
        {
            return null;
        }

        return _manifestService.Current.Capabilities.FirstOrDefault(c => c.Name.Equals(intent, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     [task_087] Fire-and-forget wrapper around <see cref="IEscalationService.EscalateAsync"/>
    ///     that logs any exception. Replaces the previous
    ///     <c>_ = _escalationService.EscalateAsync(escCtx, ct)</c> pattern that
    ///     silently swallowed failures. The escalation itself remains non-blocking
    ///     so the routing hot path is unaffected.
    /// </summary>
    private async Task EscalateSafelyAsync(EscalationContext escCtx, CancellationToken ct, string requestId)
    {
        try
        {
            await _escalationService!.EscalateAsync(escCtx, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Agent is shutting down — escalation cancelled, nothing to do.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[IntentRouter] Escalation failed for request {RequestId} (type={Type}, severity={Severity})",
                requestId, escCtx.Type, escCtx.Severity);
        }
    }

    /// <summary>
    ///     Зарегистрировать себя в capability registry (опубликовать свой манифест).
    /// </summary>
    public void PublishSelf()
    {
        AgentManifest manifest = _manifestService.Save();
        _registry.Register(manifest);
    }

    /// <summary>
    ///     Build an InterAgentAuditRecord for an outbound delegation event (for correlation with result).
    /// </summary>
    private InterAgentAuditRecord BuildOutboundRecord(
        IntentEnvelope envelope,
        string senderAgentId,
        string receiverAgentId,
        TransportResult transportResult,
        string? policyDecision,
        string? policyReason)
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
            PolicyDecision = policyDecision,
            PolicyReason = policyReason,
            LatencyMs = transportResult.LatencyMs,
            Outcome = transportResult.IsSuccess ? DelegationOutcome.Ok : DelegationOutcome.Error,
            Error = transportResult.ErrorMessage,
            DelegationDepth = depth,
            HopCount = depth + 1,
            TransportKind = transportResult.TransportKind.ToString(),
        };
    }
}
