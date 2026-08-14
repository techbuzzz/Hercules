namespace Hercules.Mesh;

/// <summary>
///     Сервисный интерфейс для управления capability registry.
///     Абстрагирует низкоуровневый SQLite-реестр от потребителей (Controller, HealthService).
/// </summary>
public interface ICapabilityRegistryService
{
    /// <summary>Получить полную запись агента.</summary>
    RegistryAgentFullEntry? GetEntry(string agentId);

    /// <summary>Список всех агентов с полной информацией.</summary>
    IReadOnlyList<RegistryAgentFullEntry> ListAll();

    /// <summary>Обновить health status агента (optional: specify exact consecutive failures count).</summary>
    void UpdateHealthStatus(string agentId, AgentHealthStatus status, string? error = null, int? consecutiveFailures = null);

    /// <summary>Обновить trust level агента.</summary>
    void UpdateTrustLevel(string agentId, string trustLevel);

    /// <summary>Обновить cost hint агента (USD per call).</summary>
    void UpdateCostHint(string agentId, decimal costUsd);

    /// <summary>Обновить latency hint агента (ms per call).</summary>
    void UpdateLatencyHint(string agentId, int latencyMs);

    /// <summary>Установить TTL (expiry_seconds) для агента.</summary>
    void SetExpiry(string agentId, int expirySeconds);

    /// <summary>Touch — обновить last_seen (heartbeat).</summary>
    void Touch(string agentId);

    /// <summary>Удалить агента из реестра.</summary>
    bool Remove(string agentId);

    /// <summary>Найти агентов по capability.</summary>
    List<RegistryAgentEntry> FindByCapability(string capabilityName);

    /// <summary>Найти агентов по phrase.</summary>
    List<RegistryAgentEntry> FindByPhrase(string phrase);

    /// <summary>Список capabilities агента.</summary>
    List<RegistryCapabilityEntry> ListCapabilities(string agentId);

    /// <summary>Зарегистрировать (или обновить) агента из манифеста.</summary>
    void Register(AgentManifest manifest);

    /// <summary>Удалить просроченных агентов. Возвращает количество.</summary>
    int CleanupExpired();
}
