namespace HerculesBus;

/// <summary>
///     Канал — аналог Slack channel. Может быть public (любой агент может subscribe)
///     или private (только по invite). История хранится в IChannelStore.
/// </summary>
public sealed record Channel(
    string Name,
    string Description,
    bool IsPrivate,
    DateTimeOffset CreatedAt,
    string CreatedBy);

/// <summary>
///     Хранилище каналов и истории сообщений.
///     V3.1: SQLite через Microsoft.Data.Sqlite (in-process).
///     V3.2: можно вынести в отдельный сервис + Postgres для multi-node.
/// </summary>
public interface IChannelStore
{
    Task<Channel> EnsureChannelAsync(string name, string description, bool isPrivate, string createdBy, CancellationToken ct = default);
    Task<Channel?> GetChannelAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<Channel>> ListChannelsAsync(CancellationToken ct = default);
    Task<AgentMessage> AppendMessageAsync(AgentMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<AgentMessage>> GetRecentMessagesAsync(string channel, int limit = 50, string? beforeId = null, CancellationToken ct = default);
    Task<AgentMessage?> GetMessageAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<AgentMessage>> GetThreadAsync(string messageId, CancellationToken ct = default);
}
