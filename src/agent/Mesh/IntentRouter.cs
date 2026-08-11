using Hercules.Agent;

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
    private readonly IntentTransport _transport;

    public IntentRouter(
        AgentCore agent,
        CapabilityRegistry registry,
        IntentTransport transport,
        AgentManifestService manifestService)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _manifestService = manifestService ?? throw new ArgumentNullException(nameof(manifestService));
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

        // 4. Отправляем intent peer'у
        return await _transport.SendToAsync(peer.AgentId, envelope, ct);
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
    ///     Зарегистрировать себя в capability registry (опубликовать свой манифест).
    /// </summary>
    public void PublishSelf()
    {
        AgentManifest manifest = _manifestService.Save();
        _registry.Register(manifest);
    }
}
