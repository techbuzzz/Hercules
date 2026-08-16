using System.Collections.Concurrent;
using System.Threading.Channels;
using Hercules.Config;
using Hercules.Observability;
using HerculesBus.Core;
using Microsoft.Extensions.Logging;

namespace HerculesBus.InMemory;

/// <summary>
///     In-process pub/sub event bus на базе System.Threading.Channels.
///     Используется для mono-node setup (один процесс, много агентов внутри).
///     V3.2: заменяется на WebSocket / gRPC fan-out для multi-node.
///     Handlers вызываются последовательно (в порядке подписки); каждый handler в своём Task.
///     Errors в handler'е логируются, но НЕ пробрасываются (publisher не должен падать из-за подписчика).
///     task_086: bounded channels (default 1024) + drop-on-backpressure policy + counter metric.
/// </summary>
public sealed class InMemoryEventBus : IEventBus
{
    private readonly BusConfig _config;
    private readonly object _allLock = new();
    private readonly List<Channel<AgentMessage>> _allSubs = new();
    private readonly ConcurrentDictionary<string, List<Channel<AgentMessage>>> _channelSubs = new();
    private readonly ILogger<InMemoryEventBus> _logger;

    /// <summary>
    ///     Создаёт bus с дефолтной конфигурацией (<see cref="BusConfig"/>) — 1024 элемента,
    ///     backpressure-first с timeout 100 ms.
    /// </summary>
    public InMemoryEventBus(ILogger<InMemoryEventBus> logger)
        : this(logger, new BusConfig())
    {
    }

    /// <summary>
    ///     Создаёт bus с явной конфигурацией backpressure.
    ///     При <paramref name="config"/>=null используются дефолты.
    /// </summary>
    public InMemoryEventBus(ILogger<InMemoryEventBus> logger, BusConfig? config)
    {
        _logger = logger;
        _config = config ?? new BusConfig();
    }

    /// <summary>Effective channel capacity for diagnostics and tests.</summary>
    public int ChannelCapacity => _config.MaxChannelCapacity;

    public async ValueTask PublishAsync(AgentMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // 1. Канальные подписчики
        if (_channelSubs.TryGetValue(message.Channel, out var subs))
        {
            foreach (var ch in subs.ToArray())
            {
                if (ch.Writer.TryWrite(message)) continue;
                await WriteWithBackpressureAsync(ch, message.Channel, message, ct).ConfigureAwait(false);
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
            if (ch.Writer.TryWrite(message)) continue;
            await WriteWithBackpressureAsync(ch, "*", message, ct).ConfigureAwait(false);
        }
    }

    public IAsyncDisposable Subscribe(string channel, Func<AgentMessage, CancellationToken, ValueTask> handler, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(handler);

        var ch = CreateBoundedChannel(_config.MaxChannelCapacity);

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

        var ch = CreateBoundedChannel(_config.MaxChannelCapacity);

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

    private async ValueTask WriteWithBackpressureAsync(
        Channel<AgentMessage> ch,
        string channelName,
        AgentMessage message,
        CancellationToken ct)
    {
        if (_config.DropOnBackpressure)
        {
            RecordDrop(channelName);
            return;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(_config.BackpressureTimeoutMs));
        try
        {
            await ch.Writer.WriteAsync(message, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Local timeout elapsed — backpressure exhausted, drop the message.
            RecordDrop(channelName);
            _logger.LogWarning(
                "Bus channel '{Channel}' full; dropped message after {TimeoutMs}ms backpressure",
                channelName, _config.BackpressureTimeoutMs);
        }
    }

    private static void RecordDrop(string channelName)
    {
        OtelMetrics.BusChannelDropCounter.Add(1,
            new KeyValuePair<string, object?>("channel", channelName));
    }

    private static Channel<AgentMessage> CreateBoundedChannel(int capacity)
    {
        // Capacity is clamped to >=1 to satisfy Channel.CreateBounded invariant.
        var cap = Math.Max(1, capacity);
        return Channel.CreateBounded<AgentMessage>(new BoundedChannelOptions(cap)
        {
            SingleReader = true,
            SingleWriter = false,
            // Wait mode: TryWrite returns false when full, WriteAsync awaits free slot.
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
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
