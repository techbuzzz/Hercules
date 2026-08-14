using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Hercules.Mesh;
using Hercules.Mesh.Abstractions;
using Hercules.Mesh.Transport;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using RedisMeshConfig = Hercules.Mesh.Backends.Redis.RedisMeshConfig;

/// <summary>
///     RESP-compatible pub/sub bus using Redis pub/sub (SUBSCRIBE/PUBLISH).
///     Fan-out via channel-based routing. Request/reply uses temporary key + polling.
///     Gracefully degrades to in-process when Redis is unavailable.
///     Spec: task_067.
/// </summary>
public sealed class RedisMeshBus : IMeshBus
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisMeshConfig _config;
    private readonly ILogger<RedisMeshBus> _log;
    private readonly JsonSerializerOptions _json;
    private readonly ConcurrentDictionary<string, List<Func<IntentEnvelope, CancellationToken, Task>>> _localSubscribers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IntentResponse>> _pendingReplies = new();
    private readonly ConcurrentDictionary<string, Channel<IntentEnvelope>> _localChannels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer? _replyCleanupTimer;
    private readonly object _subscribeLock = new();
    private bool _disposed;
    private ISubscriber? _subscriber;

    public string BackendKind => "redis";

    public RedisMeshBus(
        IConnectionMultiplexer redis,
        RedisMeshConfig config,
        ILogger<RedisMeshBus> log)
    {
        _redis = redis;
        _config = config;
        _log = log;
        _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        // Subscribe to Redis channels for inbound messages from other agents
        _ = Task.Run(() => SubscribeToRedisChannelsAsync());

        // Periodic cleanup of stale pending reply TCS
        _replyCleanupTimer = new Timer(
            static state => ((RedisMeshBus)state!).CleanupStaleReplies(),
            this,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(10));
    }

    /// <inheritdoc />
    public async Task PublishAsync(string topic, IntentEnvelope envelope, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(topic);

        try
        {
            var channel = _config.ChannelPrefix + topic;
            var json = JsonSerializer.Serialize(envelope, _json);
            var subscriber = GetSubscriber();
            await subscriber.PublishAsync(RedisChannel.Literal(channel), json).ConfigureAwait(false);
        }
        catch (RedisConnectionException ex)
        {
            _log.LogWarning(ex, "[RedisMeshBus] Failed to publish to {Topic}, falling back to local", topic);
            await PublishLocalAsync(topic, envelope, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task<IDisposable> SubscribeAsync(
        string topic,
        Func<IntentEnvelope, CancellationToken, Task> handler,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(topic);

        var subscribers = _localSubscribers.GetOrAdd(topic, static _ => new List<Func<IntentEnvelope, CancellationToken, Task>>());
        lock (subscribers)
        {
            subscribers.Add(handler);
        }

        return Task.FromResult<IDisposable>(new Subscription(() =>
        {
            lock (subscribers)
            {
                subscribers.Remove(handler);
            }
        }));
    }

    /// <inheritdoc />
    public async Task<IntentResponse> RequestReplyAsync(
        string targetAgentId,
        IntentEnvelope envelope,
        CancellationToken ct = default,
        TimeSpan? defaultTimeout = null)
    {
        ThrowIfDisposed();

        var correlationId = envelope.RequestId;
        var timeout = envelope.Deadline.HasValue
            ? envelope.Deadline.Value - DateTimeOffset.UtcNow
            : (defaultTimeout ?? TimeSpan.FromSeconds(30));

        if (timeout <= TimeSpan.Zero)
        {
            return IntentResponse.TimedOut(envelope.RequestId, targetAgentId, envelope.TraceId);
        }

        var tcs = new TaskCompletionSource<IntentResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingReplies.TryAdd(correlationId, tcs);

        try
        {
            var enriched = new IntentEnvelope
            {
                RequestId = envelope.RequestId,
                TraceId = envelope.TraceId,
                IdempotencyKey = envelope.IdempotencyKey,
                Sender = envelope.Sender,
                Recipient = envelope.Recipient,
                Intent = envelope.Intent,
                Payload = envelope.Payload,
                ResponseSchema = envelope.ResponseSchema,
                ReplyTo = correlationId,
                Deadline = envelope.Deadline,
                Auth = envelope.Auth,
                Version = envelope.Version
            };

            // Fan-out to both intent topic and agent-specific topic
            var intentTopic = $"intent/{envelope.Intent}";
            var agentTopic = $"agent/{targetAgentId}";
            await PublishAsync(intentTopic, enriched, ct).ConfigureAwait(false);
            await PublishAsync(agentTopic, enriched, ct).ConfigureAwait(false);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            try
            {
                return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return IntentResponse.TimedOut(envelope.RequestId, targetAgentId, envelope.TraceId);
            }
            catch (OperationCanceledException)
            {
                return IntentResponse.TimedOut(envelope.RequestId, targetAgentId, envelope.TraceId);
            }
        }
        finally
        {
            _pendingReplies.TryRemove(correlationId, out _);
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        if (_disposed) return false;
        try
        {
            var db = _redis.GetDatabase();
            await db.PingAsync().WaitAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private ISubscriber GetSubscriber()
    {
        return _subscriber ??= _redis.GetSubscriber();
    }

    private async Task SubscribeToRedisChannelsAsync()
    {
        while (!_disposed)
        {
            try
            {
                var subscriber = GetSubscriber();
                // Subscribe to the wildcard pattern for all mesh messages
                var pattern = _config.ChannelPrefix + "*";
                await subscriber.SubscribeAsync(
                    RedisChannel.Pattern(pattern),
                    async (ch, message) =>
                    {
                        if (message.IsNullOrEmpty) return;
                        await HandleRedisMessageAsync(ch, message).ConfigureAwait(false);
                    }).ConfigureAwait(false);

                _log.LogInformation("[RedisMeshBus] Subscribed to Redis channel pattern {Pattern}", pattern);
                break;
            }
            catch (ObjectDisposedException) when (_disposed)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[RedisMeshBus] Failed to subscribe to Redis channels, retrying in 5s");
                await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleRedisMessageAsync(RedisChannel ch, RedisValue message)
    {
        if (string.IsNullOrEmpty(message))
            return;

        try
        {
            var envelope = JsonSerializer.Deserialize<IntentEnvelope>(message.ToString(), _json);
            if (envelope is null) return;

            var topic = ch.ToString().Replace(_config.ChannelPrefix, "");

            // Route reply if this has a ReplyTo
            if (!string.IsNullOrEmpty(envelope.ReplyTo) &&
                _pendingReplies.TryGetValue(envelope.ReplyTo, out var tcs))
            {
                var response = IntentResponse.Ok(
                    envelope.RequestId,
                    envelope.Sender,
                    envelope.Payload,
                    skill: envelope.Intent,
                    traceId: envelope.TraceId);
                tcs.TrySetResult(response);
                return;
            }

            // Fan-out to local subscribers
            if (_localSubscribers.TryGetValue(topic, out var subs))
            {
                List<Func<IntentEnvelope, CancellationToken, Task>> snapshot;
                lock (subs)
                {
                    snapshot = new List<Func<IntentEnvelope, CancellationToken, Task>>(subs);
                }

                foreach (var handler in snapshot)
                {
                    _ = Task.Run(async () =>
                    {
                        try { await handler(envelope, CancellationToken.None).ConfigureAwait(false); }
                        catch { /* Log and continue */ }
                    });
                }
            }
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "[RedisMeshBus] Failed to deserialize Redis message from {Channel}", ch);
        }
    }

    private Task PublishLocalAsync(string topic, IntentEnvelope envelope, CancellationToken ct)
    {
        if (_localSubscribers.TryGetValue(topic, out var subs))
        {
            List<Func<IntentEnvelope, CancellationToken, Task>> snapshot;
            lock (subs)
            {
                snapshot = new List<Func<IntentEnvelope, CancellationToken, Task>>(subs);
            }

            foreach (var handler in snapshot)
            {
                _ = TryInvokeHandler(handler, envelope, ct);
            }
        }

        return Task.CompletedTask;
    }

    private async Task TryInvokeHandler(
        Func<IntentEnvelope, CancellationToken, Task> handler,
        IntentEnvelope envelope,
        CancellationToken ct)
    {
        try
        {
            await handler(envelope, ct).ConfigureAwait(false);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Log and continue
        }
    }

    private void CleanupStaleReplies()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _pendingReplies)
        {
            if (kvp.Value.Task.IsCompleted)
            {
                _pendingReplies.TryRemove(kvp.Key, out _);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RedisMeshBus));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true) == true)
            return;

        _replyCleanupTimer?.Dispose();

        foreach (var channel in _localChannels.Values)
            channel.Writer.TryComplete();
        _localChannels.Clear();
        _localSubscribers.Clear();

        // Don't dispose the IConnectionMultiplexer — owned by the DI container
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action _unsubscribe;
        private bool _disposed;

        public Subscription(Action unsubscribe) => _unsubscribe = unsubscribe;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, true) == false)
                _unsubscribe();
        }
    }
}
