namespace HerculesBus.Core;

/// <summary>
///     Event bus — push-доставка сообщений подписанным агентам.
///     V3.1: in-process pub/sub через Channel&lt;T&gt; (mono-node).
///     V3.2: WebSocket / SignalR / gRPC для multi-node.
/// </summary>
public interface IEventBus
{
    ValueTask PublishAsync(AgentMessage message, CancellationToken ct = default);
    IAsyncDisposable Subscribe(string channel, Func<AgentMessage, CancellationToken, ValueTask> handler, CancellationToken ct = default);
    IAsyncDisposable SubscribeAll(Func<AgentMessage, CancellationToken, ValueTask> handler, CancellationToken ct = default);
}
