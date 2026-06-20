namespace HerculesBus.Core;

/// <summary>
///     Канал в HerculesBus — аналог Slack channel. Может быть public (любой агент может subscribe)
///     или private (только по invite). История хранится в IChannelStore.
///     Названо <see cref="BusChannel"/> чтобы не конфликтовать с System.Threading.Channels.Channel.
/// </summary>
public sealed record BusChannel(
    string Name,
    string Description,
    bool IsPrivate,
    DateTimeOffset CreatedAt,
    string CreatedBy);

/// <summary>
///     Хранилище каналов и истории сообщений.
///     V3.1: in-memory (теряется при перезапуске) или SQLite (опционально).
///     V3.2: можно вынести в отдельный сервис + Postgres для multi-node.
/// </summary>
public interface IChannelStore
{
    Task<BusChannel> EnsureChannelAsync(string name, string description, bool isPrivate, string createdBy, CancellationToken ct = default);
    Task<BusChannel?> GetChannelAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<BusChannel>> ListChannelsAsync(CancellationToken ct = default);
    Task<AgentMessage> AppendMessageAsync(AgentMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<AgentMessage>> GetRecentMessagesAsync(string channel, int limit = 50, string? beforeId = null, CancellationToken ct = default);
    Task<AgentMessage?> GetMessageAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<AgentMessage>> GetThreadAsync(string messageId, CancellationToken ct = default);
}
