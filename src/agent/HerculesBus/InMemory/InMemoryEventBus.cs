using System.Collections.Concurrent;
using SysChannel = System.Threading.Channels.Channel;
using SysChannelReader = System.Threading.Channels;

namespace HerculesBus.InMemory;

/// <summary>
///     In-process pub/sub event bus на базе System.Threading.Channels.
///     Используется для mono-node setup (один процесс, много агентов внутри).
///     V3.2: заменяется на WebSocket / gRPC fan-out для multi-node.
///     Handlers вызываются последовательно (в порядке подписки); каждый handler в своём Task.
///     Errors в handler'е логируются, но НЕ пробрасываются (publisher не должен падать из-за подписчика).
/// </summary>
public sealed class InMemoryEventBus : IEventBus
{
    private readonly ConcurrentDictionary<string, List<System.Threading.Channels.Channel<AgentMessage>>> _channelSubs = new();
    private readonly List<System.Threading.Channels.Channel<AgentMessage>> _allSubs = new();
    private readonly object _allLock = new();

    public ValueTask PublishAsync(AgentMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // 1. Канальные подписчики
        if (_channelSubs.TryGetValue(message.Channel, out var subs))
        {
            foreach (var ch in subs.ToArray())
            {
                ch.Writer.TryWrite(message);
            }
        }

        // 2. Глобальные подписчики (admin/observability)
        System.Threading.Channels.Channel<AgentMessage>[] allSnapshot;
        lock (_allLock) { allSnapshot = _allSubs.ToArray(); }
        foreach (var ch in allSnapshot)
        {
            ch.Writer.TryWrite(message);
        }

        return ValueTask.CompletedTask;
    }

    public IAsyncDisposable Subscribe(string channel, Func<AgentMessage, CancellationToken, ValueTask> handler, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(handler);

        var ch = System.Threading.Channels.Channel.CreateUnbounded<AgentMessage>(new System.Threading.Channels.UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        var subs = _channelSubs.GetOrAdd(channel, _ => new List<System.Threading.Channels.Channel<AgentMessage>>());
        lock (subs) { subs.Add(ch); }

        // Запускаем reader в fire-and-forget Task
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in ch.Reader.ReadAllAsync(ct))
                {
                    try
                    {
                        await handler(msg, ct);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[HerculesBus] handler error in channel '{channel}': {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException) { /* shutdown */ }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[HerculesBus] subscriber died for '{channel}': {ex.GetType().Name}: {ex.Message}");
            }
        }, ct);

        return new SubscriptionToken(this, channel, ch);
    }

    public IAsyncDisposable SubscribeAll(Func<AgentMessage, CancellationToken, ValueTask> handler, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var ch = System.Threading.Channels.Channel.CreateUnbounded<AgentMessage>(new System.Threading.Channels.UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        lock (_allLock) { _allSubs.Add(ch); }

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in ch.Reader.ReadAllAsync(ct))
                {
                    try
                    {
                        await handler(msg, ct);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[HerculesBus] global handler error: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[HerculesBus] global subscriber died: {ex.GetType().Name}: {ex.Message}");
            }
        }, ct);

        return new GlobalSubscriptionToken(this, ch);
    }

    private void Unsubscribe(string channel, System.Threading.Channels.Channel<AgentMessage> ch)
    {
        if (_channelSubs.TryGetValue(channel, out var subs))
        {
            lock (subs) { subs.Remove(ch); }
        }
        ch.Writer.TryComplete();
    }

    private void UnsubscribeGlobal(System.Threading.Channels.Channel<AgentMessage> ch)
    {
        lock (_allLock) { _allSubs.Remove(ch); }
        ch.Writer.TryComplete();
    }

    private sealed class SubscriptionToken : IAsyncDisposable
    {
        private readonly InMemoryEventBus _bus;
        private readonly string _channel;
        private readonly System.Threading.Channels.Channel<AgentMessage> _ch;

        public SubscriptionToken(InMemoryEventBus bus, string channel, System.Threading.Channels.Channel<AgentMessage> ch)
        {
            _bus = bus;
            _channel = channel;
            _ch = ch;
        }

        public ValueTask DisposeAsync()
        {
            _bus.Unsubscribe(_channel, _ch);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class GlobalSubscriptionToken : IAsyncDisposable
    {
        private readonly InMemoryEventBus _bus;
        private readonly System.Threading.Channels.Channel<AgentMessage> _ch;

        public GlobalSubscriptionToken(InMemoryEventBus bus, System.Threading.Channels.Channel<AgentMessage> ch)
        {
            _bus = bus;
            _ch = ch;
        }

        public ValueTask DisposeAsync()
        {
            _bus.UnsubscribeGlobal(_ch);
            return ValueTask.CompletedTask;
        }
    }
}
