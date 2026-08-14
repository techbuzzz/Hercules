using System.Collections.Concurrent;
using System.Linq;

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Hercules.Mesh;
using Hercules.Mesh.Abstractions;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NatsMeshConfig = Hercules.Mesh.Backends.Nats.NatsMeshConfig;

/// <summary>
///     NATS core pub/sub bus: subject-based routing, request/reply with correlation ID.
///     Fan-out: each subscriber on a subject gets a copy.
///     Request/reply: uses the built-in RequestAsync with NATS inbox.
///     Spec: task_068.
/// </summary>
public sealed class NatsMeshBus : IMeshBus
{
    private readonly NatsMeshConfig _config;
    private readonly ILogger<NatsMeshBus> _log;
    private readonly JsonSerializerOptions _json;
    private readonly ConcurrentDictionary<string, List<Func<IntentEnvelope, CancellationToken, Task>>> _localSubscribers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IntentResponse>> _pendingReplies = new();
    private readonly Timer _replyCleanupTimer;
    private readonly string[] _servers;
    private readonly NatsOpts _opts;
    private readonly CancellationTokenSource _cts;
    private readonly Channel<(string topic, IntentEnvelope envelope)> _localInbox;
    private volatile NatsConnection? _connection;
    private readonly Task? _connectTask;
    private bool _disposed;

    public string BackendKind => "nats";

    public NatsMeshBus(NatsMeshConfig config, ILogger<NatsMeshBus> log)
    {
        _config = config;
        _log = log;
        _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        var servers = config.Servers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (servers.Length == 0) servers = new[] { "nats://localhost:4222" };
        _servers = servers;

        _opts = new NatsOpts
        {
            Name = config.Name,
            Url = servers[0], // Primary server; NATS client handles failover automatically
        };

        _cts = new CancellationTokenSource();
        _localInbox = Channel.CreateUnbounded<(string, IntentEnvelope)>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        _replyCleanupTimer = new Timer(
            static state => ((NatsMeshBus)state!).CleanupStaleReplies(),
            this,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(10));

        _connectTask = Task.Run(() => ConnectAndSubscribeAsync(), _cts.Token);
    }

    private async Task ConnectAndSubscribeAsync()
    {
        while (!_disposed)
        {
            try
            {
                var nc = new NatsConnection(_opts);
                _connection = nc;

                _log.LogInformation("[NatsMeshBus] Connecting to NATS at {Servers}", _config.Servers);
                await nc.ConnectAsync().ConfigureAwait(false);
                _log.LogInformation("[NatsMeshBus] Connected to NATS");

                // Subscribe to wildcard pattern for all mesh messages
                var pattern = _config.SubjectPrefix + ">";

                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Use built-in SubscribeAsync → IAsyncEnumerable pattern
                        await foreach (var msg in nc.SubscribeAsync<string>(pattern, cancellationToken: _cts.Token).WithCancellation(_cts.Token))
                        {
                            await HandleMessageAsync(msg).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) when (!_disposed)
                    {
                        _log.LogWarning(ex, "[NatsMeshBus] Subscription loop exited unexpectedly");
                    }
                }, _cts.Token);

                _log.LogInformation("[NatsMeshBus] Subscribed to NATS subject pattern {Pattern}", pattern);
                break;
            }
            catch (Exception ex) when (!_disposed)
            {
                _log.LogWarning(ex, "[NatsMeshBus] Failed to connect/subscribe to NATS, retrying in 5s");
                try { await Task.Delay(TimeSpan.FromSeconds(5), _cts.Token).ConfigureAwait(false); }
                catch { }
            }
        }
    }

    private async Task HandleMessageAsync(NatsMsg<string> msg)
    {
        if (string.IsNullOrEmpty(msg.Data))
            return;

        try
        {
            var envelope = JsonSerializer.Deserialize<IntentEnvelope>(msg.Data, _json);
            if (envelope is null) return;

            // Strip prefix to get logical topic
            var topic = msg.Subject.StartsWith(_config.SubjectPrefix, StringComparison.OrdinalIgnoreCase)
                ? msg.Subject[_config.SubjectPrefix.Length..]
                : msg.Subject;

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
            _log.LogWarning(ex, "[NatsMeshBus] Failed to deserialize NATS message on {Subject}", msg.Subject);
        }
    }

    /// <inheritdoc />
    public async Task PublishAsync(string topic, IntentEnvelope envelope, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(topic);

        var nc = _connection;
        if (nc is null)
        {
            _log.LogWarning("[NatsMeshBus] Not connected, publishing locally to {Topic}", topic);
            await PublishLocalAsync(topic, envelope, ct).ConfigureAwait(false);
            return;
        }

        try
        {
            var subject = _config.SubjectPrefix + topic;
            var json = JsonSerializer.Serialize(envelope, _json);
            await nc.PublishAsync(subject, json, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[NatsMeshBus] Failed to publish to NATS {Topic}, falling back to local", topic);
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

        var nc = _connection;
        if (nc is null)
        {
            return IntentResponse.TimedOut(envelope.RequestId, targetAgentId, envelope.TraceId);
        }

        var timeout = envelope.Deadline.HasValue
            ? envelope.Deadline.Value - DateTimeOffset.UtcNow
            : (defaultTimeout ?? TimeSpan.FromSeconds(30));

        if (timeout <= TimeSpan.Zero)
        {
            return IntentResponse.TimedOut(envelope.RequestId, targetAgentId, envelope.TraceId);
        }

        var correlationId = envelope.RequestId;
        var tcs = new TaskCompletionSource<IntentResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingReplies.TryAdd(correlationId, tcs);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            // Use NATS built-in request/reply via inbox
            var replyTo = $"_INBOX.{Guid.NewGuid():N}";
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
                Version = envelope.Version,
                Timestamp = envelope.Timestamp
            };

            var json = JsonSerializer.Serialize(enriched, _json);
            var intentSubject = _config.SubjectPrefix + $"intent/{envelope.Intent}";
            var agentSubject = _config.SubjectPrefix + $"agent/{targetAgentId}";

            // Subscribe to reply inbox — SubscribeAsync returns IAsyncEnumerable<NatsMsg<string>>
            IAsyncEnumerable<NatsMsg<string>> replySub = nc.SubscribeAsync<string>(replyTo);

            // Publish request to both intent and agent topics
            await nc.PublishAsync(intentSubject, json, replyTo: replyTo, cancellationToken: ct).ConfigureAwait(false);
            await nc.PublishAsync(agentSubject, json, replyTo: replyTo, cancellationToken: ct).ConfigureAwait(false);

            // Wait for reply — await foreach with linked CTS handles timeout cancellation
            try
            {
                await foreach (var replyMsg in replySub.WithCancellation(cts.Token))
                {
                    if (string.IsNullOrEmpty(replyMsg.Data)) continue;
                    var respEnvelope = JsonSerializer.Deserialize<IntentEnvelope>(replyMsg.Data, _json);
                    if (respEnvelope is not null && _pendingReplies.TryGetValue(correlationId, out var pendingTcs))
                    {
                        var response = IntentResponse.Ok(
                            respEnvelope.RequestId,
                            respEnvelope.Sender,
                            respEnvelope.Payload,
                            skill: respEnvelope.Intent,
                            traceId: respEnvelope.TraceId);
                        pendingTcs.TrySetResult(response);
                    }
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout — reply not received
            }

            if (tcs.Task.IsCompleted)
                return tcs.Task.Result;
            return IntentResponse.TimedOut(envelope.RequestId, targetAgentId, envelope.TraceId);
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
            var nc = _connection;
            if (nc is null)
                return false;
            var rtt = await nc.PingAsync(ct).ConfigureAwait(false);
            return rtt.TotalMilliseconds >= 0;
        }
        catch
        {
            return false;
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
        catch { /* Log and continue */ }
    }

    private void CleanupStaleReplies()
    {
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
            throw new ObjectDisposedException(nameof(NatsMeshBus));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, true) == true)
            return;

        _cts.Cancel();
        _replyCleanupTimer?.Dispose();

        if (_connection is not null)
        {
            try { await _connection.DisposeAsync().ConfigureAwait(false); }
            catch { /* ignore */ }
        }

        _cts.Dispose();
        _localSubscribers.Clear();
        _localInbox.Writer.TryComplete();
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
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
