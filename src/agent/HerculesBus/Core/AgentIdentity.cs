namespace HerculesBus.Core;

/// <summary>
///     Identity агента в HerculesBus. Генерируется при первом подключении,
///     хранится локально (config или secret store), передаётся при аутентификации.
/// </summary>
/// <param name="AgentId">Стабильный ID (e.g. "hercules", "contract-auditor").</param>
/// <param name="DisplayName">Human-readable имя для UI.</param>
/// <param name="Roles">
///     Роли: "hercules-agent" (может читать каналы), "hercules-broadcaster" (может broadcast),
///     "hercules-admin" (управление каналами/агентами).
/// </param>
/// <param name="Token">Bearer token для аутентификации на сервере.</param>
/// <param name="SubscribedChannels">Каналы, на которые агент подписан.</param>
public sealed record AgentIdentity(
    string AgentId,
    string DisplayName,
    IReadOnlyList<string> Roles,
    string Token,
    IReadOnlyList<string>? SubscribedChannels = null);

/// <summary>
///     Статус агента — для UI badges и для routing (busy agents не получают broadcast).
/// </summary>
public enum AgentStatus
{
    Online,
    Busy,
    Idle,
    Offline
}

public sealed record AgentInfo(
    string AgentId,
    string DisplayName,
    IReadOnlyList<string> Roles,
    AgentStatus Status,
    DateTimeOffset RegisteredAt,
    DateTimeOffset LastSeen,
    IReadOnlyList<string>? SubscribedChannels = null)
{
    public bool IsOnline => Status != AgentStatus.Offline
        && (DateTimeOffset.UtcNow - LastSeen).TotalSeconds < 60;
}

public sealed record AgentRegistrationResult(
    bool IsNew,
    AgentInfo Info);

/// <summary>
///     Реестр агентов на сервере.
/// </summary>
public interface IAgentRegistry
{
    Task<AgentRegistrationResult> RegisterAsync(AgentIdentity identity, CancellationToken ct = default);
    Task HeartbeatAsync(string agentId, AgentStatus status, CancellationToken ct = default);
    Task<IReadOnlyList<AgentInfo>> ListAsync(bool includeOffline = true, CancellationToken ct = default);
    Task<AgentInfo?> GetAsync(string agentId, CancellationToken ct = default);
}
