using System.Collections.Concurrent;
using System.Threading.Channels;
using Hercules.Config;
using Hercules.Mesh.Abstractions;
using Hercules.Mesh.Transport;
using Hercules.Observability;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.InProcess;

/// <summary>
///     In-process pub/sub bus на базе <see cref="Channel{T}"/>.
///     Работает в single-process mesh без внешних зависимостей.
///     Потокобезопасен. Каждый вызов Subscribe создаёт отдельный reader на топик.
///     Request/reply использует reply-to channel + correlation ID.
///     Спецификация: task_066.
///     task_086: replace fire-and-forget fan-out with a bounded <see cref="SemaphoreSlim"/>
///     concurrency limiter; on overflow we briefly wait for a slot, then drop with metric.
/// </summary>
public sealed class InProcessMeshBus : IMeshBus
{
    private readonly ConcurrentDictionary<string, Channel<IntentEnvelope>> _topicChannels = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<Func<IntentEnvelope, CancellationToken, Task>>> _subscribers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IntentResponse>> _pendingReplies = new();
    private readonly ConcurrentDictionary<string, Channel<IntentEnvelope>> _replyChannels = new();
    private readonly MeshBackpressureConfig _backpressure;
    private readonly ILogger<InProcessMeshBus>? _logger;
    private readonly SemaphoreSlim _handlerGate;
    bool _disposed;

    public InProcessMeshBus()
        : this(new MeshBackpressureConfig(), null)
    {
    }

    public InProcessMeshBus(MeshBackpressureConfig backpressure, ILogger<InProcessMeshBus>? logger = null)
    {
        _backpressure = backpressure ?? new MeshBackpressureConfig();
        _logger = logger;
        var maxConcurrency = Math.Max(1, _backpressure.MaxConcurrentHandlers);
        _handlerGate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    public string BackendKind => "in-process";

    /// <summary>Effective maximum concurrent handler invocations across all topics.</summary>
    public int MaxConcurrentHandlers => Math.Max(1, _backpressure.MaxConcurrentHandlers);

    /// <inheritdoc />
    public async Task PublishAsync(string topic, IntentEnvelope envelope, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        // Ensure topic channel exists (for potential future use / channel-based subscribers)
        _topicChannels.GetOrAdd(
            topic,
            static _ => Channel.CreateUnbounded<IntentEnvelope>(
                new UnboundedChannelOptions { SingleReader = false, SingleWriter = true }));

        // Fan-out to all registered subscribers
        if (_subscribers.TryGetValue(topic, out var subs))
        {
            List<Func<IntentEnvelope, CancellationToken, Task>> snapshot;
            lock (subs)
            {
                snapshot = new List<Func<IntentEnvelope, CancellationToken, Task>>(subs);
            }
            // Bounded fan-out: each handler invocation must acquire a slot on _handlerGate.
            // When the gate is exhausted we Wait briefly, then drop + increment metric.
            foreach (var handler in snapshot)
            {
                _ = TryInvokeHandlerWithGate(handler, envelope, topic, ct);
            }
        }
    }

    /// <inheritdoc />
    public Task<IDisposable> SubscribeAsync(
        string topic,
        Func<IntentEnvelope, CancellationToken, Task> handler,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        // Ensure topic channel exists
        _topicChannels.GetOrAdd(
            topic,
            static _ => Channel.CreateUnbounded<IntentEnvelope>(
                new UnboundedChannelOptions { SingleReader = false, SingleWriter = true }));

        // GetOrAdd can create two lists if called concurrently — use GetValue + GetOrAdd pattern
        if (!_subscribers.TryGetValue(topic, out var subscribers))
        {
            subscribers = new List<Func<IntentEnvelope, CancellationToken, Task>>();
            if (!_subscribers.TryAdd(topic, subscribers))
            {
                // Another thread added it first — use theirs
                subscribers = _subscribers.GetOrAdd(topic, static _ => new List<Func<IntentEnvelope, CancellationToken, Task>>());
            }
        }

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

        // Register pending reply TCS — RouteReply will complete it
        var tcs = new TaskCompletionSource<IntentResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingReplies.TryAdd(correlationId, tcs);

        try
        {
            // Inject reply-to into envelope (IntentEnvelope is sealed class, can't use 'with')
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

            // Publish to the target's intent topic and direct agent topic
            var intentTopic = $"intent/{envelope.Intent}";
            var agentTopic = $"agent/{targetAgentId}";

            // Fan-out synchronously so handlers fire before we await
            await PublishAsync(intentTopic, enriched, ct);
            await PublishAsync(agentTopic, enriched, ct);

            // Wait for RouteReply to complete the TCS, or timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            try
            {
                return await tcs.Task.WaitAsync(cts.Token);
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
    public ValueTask<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        return new ValueTask<bool>(!_disposed);
    }

    /// <summary>
    ///     Маршрутизировать reply envelope обратно к ожидающему вызову.
    ///     Вызывается после обработки request. Завершает TCS в <see cref="RequestReplyAsync"/>.
    /// </summary>
    public void RouteReply(IntentEnvelope replyEnvelope)
    {
        if (string.IsNullOrEmpty(replyEnvelope.ReplyTo))
            return;

        if (_pendingReplies.TryGetValue(replyEnvelope.ReplyTo, out var tcs))
        {
            var response = IntentResponse.Ok(
                replyEnvelope.RequestId,
                replyEnvelope.Sender,
                replyEnvelope.Payload,
                skill: replyEnvelope.Intent,
                traceId: replyEnvelope.TraceId);
            tcs.TrySetResult(response);
        }
    }

    private async Task TryInvokeHandlerWithGate(
        Func<IntentEnvelope, CancellationToken, Task> handler,
        IntentEnvelope envelope,
        string topic,
        CancellationToken ct)
    {
        // Try to acquire a handler slot with a small backpressure window.
        var acquireTimeout = TimeSpan.FromMilliseconds(Math.Max(1, _backpressure.HandlerAcquireTimeoutMs));
        var acquired = false;
        try
        {
            acquired = await _handlerGate.WaitAsync(acquireTimeout, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Local acquire timeout — gate was saturated; drop and instrument.
            RecordDrop(topic);
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!acquired)
        {
            RecordDrop(topic);
            return;
        }

        try
        {
            await handler(envelope, ct).ConfigureAwait(false);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Log and continue — don't kill other subscribers
        }
        finally
        {
            try
            {
                _handlerGate.Release();
            }
            catch (ObjectDisposedException)
            {
                // bus disposed mid-flight; safe to ignore
            }
        }
    }

    private static void RecordDrop(string topic)
    {
        OtelMetrics.MeshHandlerDropCounter.Add(1,
            new KeyValuePair<string, object?>("topic", topic));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(InProcessMeshBus));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true) == true)
            return;

        foreach (var channel in _topicChannels.Values)
            channel.Writer.TryComplete();
        _topicChannels.Clear();
        _subscribers.Clear();
        _replyChannels.Clear();
        _pendingReplies.Clear();
        _handlerGate.Dispose();
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
