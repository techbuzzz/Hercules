using System.Collections.Concurrent;
using System.Threading.Channels;
using HerculesBus.Core;
using Microsoft.Extensions.Logging;

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
    private readonly object _allLock = new();
    private readonly List<Channel<AgentMessage>> _allSubs = new();
    private readonly ConcurrentDictionary<string, List<Channel<AgentMessage>>> _channelSubs = new();
    private readonly ILogger<InMemoryEventBus> _logger;

    public InMemoryEventBus(ILogger<InMemoryEventBus> logger)
    {
        _logger = logger;
    }

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
        Channel<AgentMessage>[] allSnapshot;
        lock (_allLock)
        {
            allSnapshot = _allSubs.ToArray();
        }

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

        var ch = Channel.CreateUnbounded<AgentMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        var subs = _channelSubs.GetOrAdd(channel, _ => new List<Channel<AgentMessage>>());
        lock (subs)
        {
            subs.Add(ch);
        }

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
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Handler error in channel '{Channel}'", channel);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                /* shutdown */
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Subscriber died for channel '{Channel}'", channel);
            }
            finally
            {
                ch.Writer.TryComplete();
                if (_channelSubs.TryGetValue(channel, out var currentSubs))
                {
                    lock (currentSubs)
                    {
                        currentSubs.Remove(ch);
                    }
                }
            }
        }, ct);

        return new SubscriptionToken(this, channel, ch);
    }

    public IAsyncDisposable SubscribeAll(Func<AgentMessage, CancellationToken, ValueTask> handler, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var ch = Channel.CreateUnbounded<AgentMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        lock (_allLock)
        {
            _allSubs.Add(ch);
        }

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
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Global handler error");
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Global subscriber died");
            }
            finally
            {
                ch.Writer.TryComplete();
                lock (_allLock)
                {
                    _allSubs.Remove(ch);
                }
            }
        }, ct);

        return new GlobalSubscriptionToken(this, ch);
    }

    private void Unsubscribe(string channel, Channel<AgentMessage> ch)
    {
        if (_channelSubs.TryGetValue(channel, out var subs))
        {
            lock (subs)
            {
                subs.Remove(ch);
            }
        }

        ch.Writer.TryComplete();
    }

    private void UnsubscribeGlobal(Channel<AgentMessage> ch)
    {
        lock (_allLock)
        {
            _allSubs.Remove(ch);
        }

        ch.Writer.TryComplete();
    }

    private sealed class SubscriptionToken : IAsyncDisposable
    {
        private readonly InMemoryEventBus _bus;
        private readonly Channel<AgentMessage> _ch;
        private readonly string _channel;

        public SubscriptionToken(InMemoryEventBus bus, string channel, Channel<AgentMessage> ch)
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
        private readonly Channel<AgentMessage> _ch;

        public GlobalSubscriptionToken(InMemoryEventBus bus, Channel<AgentMessage> ch)
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
