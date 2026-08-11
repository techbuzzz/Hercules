using System.Collections.Concurrent;
using HerculesBus.Core;

namespace HerculesBus.InMemory;

/// <summary>
///     In-memory реализация IChannelStore (для тестов и одноразовых запусков).
///     Данные теряются при перезапуске процесса. Для персистентности — SqliteChannelStore.
///     Thread-safe через ConcurrentDictionary + lock для упорядоченных операций.
///     Поддерживает eviction: при превышении MaxMessagesPerChannel автоматически
///     удаляет самые старые сообщения.
/// </summary>
public sealed class InMemoryChannelStore : IChannelStore
{
    private readonly ConcurrentDictionary<string, BusChannel> _channels = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, AgentMessage> _messages = new(StringComparer.Ordinal);
    private readonly object _appendLock = new();
    private readonly ConcurrentDictionary<string, List<string>> _channelMessages = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Максимальное количество сообщений в канале. При превышении удаляются самые старые.</summary>
    public int MaxMessagesPerChannel { get; set; } = 10_000;

    public Task<BusChannel> EnsureChannelAsync(string name, string description, bool isPrivate, string createdBy, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var existing = _channels.GetOrAdd(name, _ => new BusChannel(
            Name: name,
            Description: description,
            IsPrivate: isPrivate,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedBy: createdBy));

        return Task.FromResult(existing);
    }

    public Task<BusChannel?> GetChannelAsync(string name, CancellationToken ct = default)
    {
        _channels.TryGetValue(name, out var ch);
        return Task.FromResult(ch);
    }

    public Task<IReadOnlyList<BusChannel>> ListChannelsAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<BusChannel>>(_channels.Values.OrderBy(c => c.Name).ToList());
    }

    public Task<AgentMessage> AppendMessageAsync(AgentMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Server-assigned ID + timestamp, если не заданы
        var withMeta = message with
        {
            Id = string.IsNullOrEmpty(message.Id) ? Ulid.NewId() : message.Id,
            Timestamp = message.Timestamp ?? DateTimeOffset.UtcNow
        };

        lock (_appendLock)
        {
            _messages[withMeta.Id] = withMeta;
            var list = _channelMessages.GetOrAdd(withMeta.Channel, _ => new List<string>());
            list.Add(withMeta.Id);

            if (list.Count > MaxMessagesPerChannel)
            {
                var evictCount = list.Count - MaxMessagesPerChannel;
                for (int i = 0; i < evictCount; i++)
                {
                    _messages.TryRemove(list[i], out _);
                }
                list.RemoveRange(0, evictCount);
            }
        }

        return Task.FromResult(withMeta);
    }

    public Task<IReadOnlyList<AgentMessage>> GetRecentMessagesAsync(string channel, int limit = 50, string? beforeId = null, CancellationToken ct = default)
    {
        if (!_channelMessages.TryGetValue(channel, out var ids))
            return Task.FromResult<IReadOnlyList<AgentMessage>>(Array.Empty<AgentMessage>());

        IEnumerable<string> filtered = ids;
        if (beforeId != null)
        {
            var idx = ids.IndexOf(beforeId);
            if (idx > 0) filtered = ids.Take(idx);
        }

        var result = filtered
            .Reverse()
            .Take(limit)
            .Reverse()
            .Select(id => _messages.TryGetValue(id, out var m) ? m : null)
            .Where(m => m != null)
            .Cast<AgentMessage>()
            .ToList();

        return Task.FromResult<IReadOnlyList<AgentMessage>>(result);
    }

    public Task<AgentMessage?> GetMessageAsync(string id, CancellationToken ct = default)
    {
        _messages.TryGetValue(id, out var m);
        return Task.FromResult(m);
    }

    public Task<IReadOnlyList<AgentMessage>> GetThreadAsync(string messageId, CancellationToken ct = default)
    {
        var result = _messages.Values
            .Where(m => m.ReplyTo == messageId)
            .OrderBy(m => m.TimestampOrUtc)
            .ToList();
        return Task.FromResult<IReadOnlyList<AgentMessage>>(result);
    }
}
