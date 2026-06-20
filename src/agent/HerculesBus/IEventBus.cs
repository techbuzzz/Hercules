namespace HerculesBus;

/// <summary>
///     Event bus — push-доставка сообщений подписанным агентам.
///     V3.1: in-process pub/sub через Channel&lt;T&gt; (для mono-node setup).
///     V3.2: WebSocket / SignalR / gRPC для remote subscribers + multi-node.
/// </summary>
public interface IEventBus
{
    /// <summary>Опубликовать сообщение (все подписчики канала получат async).</summary>
    ValueTask PublishAsync(AgentMessage message, CancellationToken ct = default);

    /// <summary>Подписаться на канал. Возвращает IDisposable для отписки.</summary>
    IAsyncDisposable Subscribe(string channel, Func<AgentMessage, CancellationToken, ValueTask> handler, CancellationToken ct = default);

    /// <summary>Подписаться на ВСЕ каналы (для admin/observability).</summary>
    IAsyncDisposable SubscribeAll(Func<AgentMessage, CancellationToken, ValueTask> handler, CancellationToken ct = default);
}
