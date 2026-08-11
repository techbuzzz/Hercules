using System.Text;
using Hercules.LLM;

namespace Hercules.Mesh;

/// <summary>
///     Distributed Reflection — расширение ReflectionEngine для mesh-окружения.
///     Анализирует производительность peer-агентов в дополнение к локальным метрикам,
///     и предлагает новые навыки/связи на основе межагентного трафика.
///     Спецификация: docs/ROADMAP-RU.md Phase 4 #20.
/// </summary>
public sealed class DistributedReflection
{
    private readonly CircuitBreaker _breaker;
    private readonly ILLMClient _llm;
    private readonly AgentManifestService _manifestService;
    private readonly CapabilityRegistry _registry;

    public DistributedReflection(
        ILLMClient llm,
        CapabilityRegistry registry,
        CircuitBreaker breaker,
        AgentManifestService manifestService)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _breaker = breaker ?? throw new ArgumentNullException(nameof(breaker));
        _manifestService = manifestService ?? throw new ArgumentNullException(nameof(manifestService));
    }

    /// <summary>
    ///     Сгенерировать отчёт о состоянии mesh: peer'ы, их circuit breaker-статусы,
    ///     рекомендации по новым связям и навыкам.
    /// </summary>
    public async Task<DistributedReflectionResult> ReflectAsync(CancellationToken ct = default)
    {
        var ownAgentId = _manifestService.Current.AgentId;
        var agents = _registry.ListAgents()
            .Where(a => !a.AgentId.Equals(ownAgentId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        IReadOnlyDictionary<string, CircuitState> circuitStates = _breaker.GetAllStates();
        var openCircuits = circuitStates
            .Where(kvp => kvp.Value == CircuitState.Open)
            .Select(kvp => kvp.Key)
            .ToList();

        // Собираем capabilities всех peer'ов для анализа покрытия
        var allCapabilities = new List<(string AgentId, string Capability, string Description)>();
        foreach (RegistryAgentEntry agent in agents)
        {
            List<RegistryCapabilityEntry> caps = _registry.ListCapabilities(agent.AgentId);
            foreach (RegistryCapabilityEntry cap in caps)
            {
                allCapabilities.Add((agent.AgentId, cap.Name, cap.Description));
            }
        }

        // Готовим сводку для LLM
        var agentsSummary = agents.Count == 0
            ? "Известных peer-агентов нет."
            : string.Join("\n", agents.Select(a =>
                $"- {a.AgentId} ({a.DisplayName}) → {a.Endpoint} | last_seen: {a.LastSeen}"));

        var circuitsSummary = openCircuits.Count == 0
            ? "Все circuit breakers замкнуты (все peer'ы доступны)."
            : string.Join("\n", openCircuits.Select(id => $"- {id}: РАЗОМКНУТ (недоступен)"));

        var capabilitiesSummary = allCapabilities.Count == 0
            ? "Capabilities у peer'ов не декларированы."
            : string.Join("\n", allCapabilities.GroupBy(c => c.Capability)
                .Select(g => $"- {g.Key}: {string.Join(", ", g.Select(c => c.AgentId))}"));

        var prompt = $"""
                      Проведи distributed reflection mesh-узла Hercules. Ответь на русском, кратко,
                      строго по структуре с заголовками:

                      ## Состояние peer-агентов
                      ## Circuit Breakers
                      ## Покрытие capabilities
                      ## Рекомендации

                      Данные mesh:
                      - Мой agentId: {ownAgentId}
                      - Всего peer-агентов: {agents.Count}
                      - Circuit breakers разомкнуто: {openCircuits.Count}

                      Peer-агенты:
                      {agentsSummary}

                      Circuit breakers:
                      {circuitsSummary}

                      Распределение capabilities:
                      {capabilitiesSummary}

                      В разделе "Рекомендации" предложи:
                      1. Какие новые связи с peer'ами стоит установить (если есть gap в coverage).
                      2. Какие новые навыки стоит создать локально, чтобы не зависеть от peer'ов.
                      3. Какие упавшие peer'ы требуют внимания (circuit open).
                      """;

        string analysis;
        try
        {
            LlmResponse resp = await _llm.CompleteAsync(Roles.Reflector, [
                new ChatTurn(ChatRole.System, "Ты — модуль distributed reflection mesh-системы."),
                new ChatTurn(ChatRole.User, prompt)
            ], ct);
            analysis = resp.Text;
        }
        catch (Exception ex)
        {
            analysis = $"_LLM недоступна для distributed reflection: {ex.Message}_";
        }

        // Формируем структурированный результат
        var sb = new StringBuilder();
        sb.AppendLine($"# Distributed Reflection — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine($"- Узел: `{ownAgentId}`");
        sb.AppendLine($"- Peer-агентов: {agents.Count}");
        sb.AppendLine($"- Circuit breakers open: {openCircuits.Count}");
        sb.AppendLine($"- Total capabilities в mesh: {allCapabilities.Count}");
        sb.AppendLine();
        sb.AppendLine(analysis);

        return new DistributedReflectionResult
        {
            Markdown = sb.ToString(),
            PeerCount = agents.Count,
            OpenCircuitCount = openCircuits.Count,
            TotalCapabilities = allCapabilities.Count,
            OpenCircuits = openCircuits,
            PeerAgents = agents
        };
    }

    /// <summary>
    ///     Проверить, нужно ли предложить создание нового локального навыка:
    ///     если intent часто перенаправляется peer'у, но circuit breaker разомкнут —
    ///     значит мы зависим от упавшего peer'а и стоит иметь локальный навык.
    /// </summary>
    public List<string> GetLocalSkillRecommendations()
    {
        var recommendations = new List<string>();
        var openCircuits = _breaker.GetAllStates()
            .Where(kvp => kvp.Value == CircuitState.Open)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var agentId in openCircuits)
        {
            List<RegistryCapabilityEntry> caps = _registry.ListCapabilities(agentId);
            foreach (RegistryCapabilityEntry cap in caps)
            {
                recommendations.Add(
                    $"Создать локальный навык '{cap.Name}' — peer '{agentId}' недоступен (circuit open), " +
                    $"но его capability '{cap.Name}' может быть нужна.");
            }
        }

        return recommendations;
    }
}

/// <summary>Результат distributed reflection.</summary>
public sealed class DistributedReflectionResult
{
    public string Markdown { get; set; } = "";
    public int PeerCount { get; set; }
    public int OpenCircuitCount { get; set; }
    public int TotalCapabilities { get; set; }
    public List<string> OpenCircuits { get; set; } = new();
    public List<RegistryAgentEntry> PeerAgents { get; set; } = new();
}
