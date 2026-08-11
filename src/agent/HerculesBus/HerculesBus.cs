using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HerculesBus.Core;

namespace HerculesBus;

/// <summary>
///     Высокоуровневый API для ИИ-агентов: комбинирует registry + channel store + event bus.
///     Используется агентом через DI:
///     <code>
///     var bus = new HerculesBus(new InMemoryChannelStore(), new InMemoryAgentRegistry(), new InMemoryEventBus());
///     await bus.RegisterAsync(identity);
///     await bus.EnsureChannelAsync("main", "General chat", isPrivate: false, createdBy: "hercules");
///     await bus.SendAsync(new AgentMessage(...));
///     await foreach (var msg in bus.SubscribeAsync("main")) { ... }
///     </code>
///     Контракты (AgentMessage, BusChannel, IChannelStore, IEventBus, IAgentRegistry, AgentIdentity, AgentInfo,
///     AgentStatus, MessageKinds, Ulid) лежат в namespace <see cref="HerculesBus.Core" />.
///     Impls в подпапках InMemory/, Http/, и (в V3.2) Sqlite/.
/// </summary>
public sealed class Bus : IAsyncDisposable
{
    private readonly IEventBus _events;
    private readonly Dictionary<string, AgentIdentity> _localAgents = new(StringComparer.OrdinalIgnoreCase);
    private readonly IAgentRegistry _registry;
    private readonly IChannelStore _store;

    public Bus(IChannelStore store, IAgentRegistry registry, IEventBus events)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>Зарегистрировать локального агента в реестре.</summary>
    public async Task<AgentRegistrationResult> RegisterAsync(AgentIdentity identity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        _localAgents[identity.AgentId] = identity;
        return await _registry.RegisterAsync(identity, ct);
    }

    /// <summary>Отправить heartbeat (обновить LastSeen + статус).</summary>
    public Task HeartbeatAsync(string agentId, AgentStatus status, CancellationToken ct = default)
    {
        return _registry.HeartbeatAsync(agentId, status, ct);
    }

    /// <summary>Получить инфо об агенте по ID.</summary>
    public Task<AgentInfo?> GetAgentAsync(string agentId, CancellationToken ct = default)
    {
        return _registry.GetAsync(agentId, ct);
    }

    /// <summary>Список всех агентов.</summary>
    public Task<IReadOnlyList<AgentInfo>> ListAgentsAsync(bool includeOffline = true, CancellationToken ct = default)
    {
        return _registry.ListAsync(includeOffline, ct);
    }

    /// <summary>Создать канал (если уже есть — возвращает существующий).</summary>
    public Task<BusChannel> EnsureChannelAsync(string name, string description = "", bool isPrivate = false, string createdBy = "system", CancellationToken ct = default)
    {
        return _store.EnsureChannelAsync(name, description, isPrivate, createdBy, ct);
    }

    /// <summary>Список каналов.</summary>
    public Task<IReadOnlyList<BusChannel>> ListChannelsAsync(CancellationToken ct = default)
    {
        return _store.ListChannelsAsync(ct);
    }

    /// <summary>Отправить сообщение в канал (сохраняется + публикуется подписчикам).</summary>
    public async Task<AgentMessage> SendAsync(AgentMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Ensure sender is registered (auto-register если нет)
        if (!_localAgents.ContainsKey(message.SenderAgentId))
        {
            await RegisterAsync(new AgentIdentity(
                message.SenderAgentId,
                message.SenderName,
                new[] { "hercules-agent" },
                "auto-generated"), ct);
        }

        AgentMessage stored = await _store.AppendMessageAsync(message, ct);
        await _events.PublishAsync(stored, ct);
        return stored;
    }

    /// <summary>Ответить на сообщение (тред).</summary>
    public async Task<AgentMessage> ReplyAsync(string replyToMessageId, AgentMessage reply, CancellationToken ct = default)
    {
        AgentMessage withReplyTo = reply with { ReplyTo = replyToMessageId };
        return await SendAsync(withReplyTo, ct);
    }

    /// <summary>Получить последние N сообщений канала (для late-join).</summary>
    public Task<IReadOnlyList<AgentMessage>> GetRecentAsync(string channel, int limit = 50, CancellationToken ct = default)
    {
        return _store.GetRecentMessagesAsync(channel, limit, null, ct);
    }

    /// <summary>Получить тред (все ответы на сообщение).</summary>
    public Task<IReadOnlyList<AgentMessage>> GetThreadAsync(string messageId, CancellationToken ct = default)
    {
        return _store.GetThreadAsync(messageId, ct);
    }

    /// <summary>Подписаться на канал (push-доставка новых сообщений).</summary>
    public IAsyncDisposable Subscribe(string channel, Func<AgentMessage, CancellationToken, ValueTask> handler, CancellationToken ct = default)
    {
        return _events.Subscribe(channel, handler, ct);
    }

    /// <summary>Подписаться на все каналы (admin/observability).</summary>
    public IAsyncDisposable SubscribeAll(Func<AgentMessage, CancellationToken, ValueTask> handler, CancellationToken ct = default)
    {
        return _events.SubscribeAll(handler, ct);
    }

    /// <summary>
    ///     Подписаться на канал через IAsyncEnumerable.
    ///     Под капотом — System.Threading.Channels.Channel (signal-based,
    ///     НЕ polling — см. Pitfall #4 в README).
    /// </summary>
    public async IAsyncEnumerable<AgentMessage> SubscribeAsync(
        string channel,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Signal-based очередь: ReadAllAsync ждёт без busy-loop.
        var queue = Channel.CreateUnbounded<AgentMessage>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });

        await using IAsyncDisposable sub = _events.Subscribe(channel, async (msg, _) =>
        {
            // SingleWriter=true → WriteAsync без race
            await queue.Writer.WriteAsync(msg, ct);
        }, ct);

        // ReadAllAsync yields messages и awaiting'ит, когда очередь пуста.
        // CancellationToken ct → корректное завершение без утечки горутин.
        await foreach (AgentMessage msg in queue.Reader.ReadAllAsync(ct))
        {
            yield return msg;
        }
    }
}
