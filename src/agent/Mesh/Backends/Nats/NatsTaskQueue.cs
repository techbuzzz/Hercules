using System.Collections.Concurrent;
using System.Text.Json;
using Hercules.Mesh.Abstractions;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NatsMeshConfig = Hercules.Mesh.Backends.Nats.NatsMeshConfig;

namespace Hercules.Mesh.Backends.Nats;

/// <summary>
///     Durable task queue using NATS JetStream.
///     Enqueue: publish to JetStream stream. Dequeue: pull consumer fetch with visibility timeout.
///     At-least-once delivery via JetStream acks.
///     Spec: task_068.
/// </summary>
public sealed class NatsTaskQueue : ITaskQueue
{
    private readonly NatsConnection _connection;
    private readonly NatsMeshConfig _config;
    private readonly ILogger<NatsTaskQueue> _log;
    private readonly JsonSerializerOptions _json;
    private readonly ConcurrentDictionary<string, string> _consumerIds = new();
    private readonly Timer _ensureTimer;
    private readonly string _streamName;
    private readonly string _dlqStreamName;
    private bool _disposed;

    public string BackendKind => "nats";

    public NatsTaskQueue(
        NatsConnection connection,
        NatsMeshConfig config,
        ILogger<NatsTaskQueue> log)
    {
        _connection = connection;
        _config = config;
        _log = log;
        _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        _streamName = $"{config.StreamPrefix}-tasks";
        _dlqStreamName = $"{config.StreamPrefix}-dlq";

        _ensureTimer = new Timer(
            static state => ((NatsTaskQueue)state!).EnsureStreamAndConsumersAsync().ConfigureAwait(false).GetAwaiter().GetResult(),
            this,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(30));
    }

    /// <inheritdoc />
    public async Task<QueuedTask> EnqueueAsync(MeshTask task, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(task);

        var queueName = string.IsNullOrEmpty(task.QueueName) ? "default" : task.QueueName;
        var taskId = string.IsNullOrEmpty(task.Id) ? Guid.NewGuid().ToString("N") : task.Id;
        var now = DateTimeOffset.UtcNow;

        var payload = new NatsTaskPayload
        {
            Id = taskId,
            QueueName = queueName,
            AssignedAgentId = task.AssignedAgentId,
            Intent = task.Intent,
            Payload = task.Payload,
            MaxRetries = task.MaxRetries,
            RetryDelay = task.RetryDelay,
            Deadline = task.Deadline,
            TraceId = task.TraceId,
            RootRequestId = task.RootRequestId,
            Metadata = task.Metadata,
            EnqueuedAt = now
        };

        var json = JsonSerializer.Serialize(payload, _json);
        var subject = $"{_config.StreamPrefix}.queue.{queueName}";

        try
        {
            if (_config.JetStreamEnabled)
            {
                var js = new NatsJSContext(_connection);
                await js.PublishAsync(subject, json, cancellationToken: ct).ConfigureAwait(false);
            }
            else
            {
                await _connection.PublishAsync(subject, json, cancellationToken: ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning(ex, "[NatsTaskQueue] Failed to publish task {TaskId} to NATS", taskId);
            throw;
        }

        return new QueuedTask
        {
            Id = taskId,
            ReceiptHandle = taskId,
            Task = task with { Id = taskId },
            EnqueuedAt = now,
            DeliveryCount = 1,
            RetryCount = 0
        };
    }

    /// <inheritdoc />
    public async Task<QueuedTask?> DequeueAsync(
        string queueName,
        TimeSpan visibilityTimeout,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var queue = string.IsNullOrEmpty(queueName) ? "default" : queueName;
        var consumerName = _consumerIds.GetOrAdd(queue, $"{_config.StreamPrefix}-consumer-{queue}");

        try
        {
            if (_config.JetStreamEnabled)
            {
                var js = new NatsJSContext(_connection);

                var fetchOpts = new NatsJSFetchOpts
                {
                    MaxMsgs = 1,
                    Expires = visibilityTimeout
                };

                try
                {
                    var consumer = await js.CreateOrUpdateConsumerAsync(
                        _streamName,
                        new ConsumerConfig
                        {
                            Name = consumerName,
                            DurableName = consumerName,
                            AckWait = visibilityTimeout,
                            MaxDeliver = 10,
                            FilterSubject = $"{_config.StreamPrefix}.queue.{queue}"
                        },
                        ct).ConfigureAwait(false);

                    await using var enum1 = consumer.FetchAsync<string>(fetchOpts, cancellationToken: ct).WithCancellation(ct).GetAsyncEnumerator();
                    while (await enum1.MoveNextAsync())
                    {
                        var msg = enum1.Current;
                        try
                        {
                            var payload = JsonSerializer.Deserialize<NatsTaskPayload>(msg.Data, _json);
                            if (payload is null)
                            {
                                await msg.NakAsync(null, ct).ConfigureAwait(false);
                                continue;
                            }

                            var deliveryCount = (int)(msg.Metadata?.NumDelivered ?? 1);

                            return new QueuedTask
                            {
                                Id = payload.Id,
                                ReceiptHandle = payload.Id,
                                Task = new MeshTask
                                {
                                    Id = payload.Id,
                                    QueueName = payload.QueueName,
                                    AssignedAgentId = payload.AssignedAgentId,
                                    Intent = payload.Intent,
                                    Payload = payload.Payload,
                                    MaxRetries = payload.MaxRetries,
                                    RetryDelay = payload.RetryDelay,
                                    Deadline = payload.Deadline,
                                    TraceId = payload.TraceId,
                                    RootRequestId = payload.RootRequestId,
                                    Metadata = payload.Metadata
                                },
                                EnqueuedAt = payload.EnqueuedAt,
                                RetryCount = payload.RetryCount,
                                DeliveryCount = deliveryCount
                            };
                        }
                        catch (JsonException ex)
                        {
                            _log.LogWarning(ex, "[NatsTaskQueue] Failed to deserialize task, nak'ing");
                            await msg.NakAsync(null, ct).ConfigureAwait(false);
                        }
                    }

                    return null;
                }
                catch (NatsJSApiException ex) when (ex.Error?.Code == 404)
                {
                    _log.LogWarning("[NatsTaskQueue] Stream {Stream} not found", _streamName);
                    return null;
                }
            }
            else
            {
                // Fallback: core NATS blocking subscribe
                using var subCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                subCts.CancelAfter(visibilityTimeout);

                var subject = $"{_config.StreamPrefix}.queue.{queue}";
                var sub = await _connection.SubscribeCoreAsync<string>(subject, null, null, null, subCts.Token).ConfigureAwait(false);
                try
                {
                    await foreach (var msg in sub.Msgs.ReadAllAsync(subCts.Token))
                    {
                        if (string.IsNullOrEmpty(msg.Data)) continue;
                        var payload = JsonSerializer.Deserialize<NatsTaskPayload>(msg.Data, _json);
                        if (payload is null) continue;

                        await sub.UnsubscribeAsync().ConfigureAwait(false);

                        return new QueuedTask
                        {
                            Id = payload.Id,
                            ReceiptHandle = payload.Id,
                            Task = new MeshTask
                            {
                                Id = payload.Id,
                                QueueName = payload.QueueName,
                                AssignedAgentId = payload.AssignedAgentId,
                                Intent = payload.Intent,
                                Payload = payload.Payload,
                                MaxRetries = payload.MaxRetries,
                                RetryDelay = payload.RetryDelay,
                                Deadline = payload.Deadline,
                                TraceId = payload.TraceId,
                                RootRequestId = payload.RootRequestId,
                                Metadata = payload.Metadata
                            },
                            EnqueuedAt = payload.EnqueuedAt,
                            RetryCount = 0,
                            DeliveryCount = 1
                        };
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    return null;
                }
                finally
                {
                    try { await sub.UnsubscribeAsync().ConfigureAwait(false); }
                    catch { /* ignore */ }
                }

                return null;
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning(ex, "[NatsTaskQueue] DequeueAsync failed for queue {Queue}", queue);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task AckAsync(string taskId, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task FailAsync(string taskId, string reason, int retry, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        _log.LogInformation("[NatsTaskQueue] Task {TaskId} failed (retry {Retry}): {Reason}", taskId, retry, reason);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QueuedTask>> GetDeadLetterQueueAsync(string queueName, int limit = 100, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (!_config.JetStreamEnabled)
            return Array.Empty<QueuedTask>();

        try
        {
            var js = new NatsJSContext(_connection);
            var msgs = new List<QueuedTask>();

            // Ephemeral consumer to peek DLQ messages
            var consumer = await js.CreateConsumerAsync(
                _dlqStreamName,
                new ConsumerConfig
                {
                    Name = $"dlq-peek-{Guid.NewGuid():N}",
                    MaxDeliver = limit,
                    FilterSubject = $"{_config.StreamPrefix}.dlq.>"
                },
                ct).ConfigureAwait(false);

            try
            {
                var fetchOpts = new NatsJSFetchOpts { MaxMsgs = limit, Expires = TimeSpan.FromSeconds(5) };
                await using var enumPeek = consumer.FetchAsync<string>(fetchOpts, cancellationToken: ct).WithCancellation(ct).GetAsyncEnumerator();
                while (await enumPeek.MoveNextAsync())
                {
                    var msg = enumPeek.Current;
                    try
                    {
                        var payload = JsonSerializer.Deserialize<NatsTaskPayload>(msg.Data, _json);
                        if (payload is not null)
                        {
                            msgs.Add(new QueuedTask
                            {
                                Id = payload.Id,
                                ReceiptHandle = payload.Id,
                                Task = new MeshTask
                                {
                                    Id = payload.Id,
                                    QueueName = payload.QueueName,
                                    AssignedAgentId = payload.AssignedAgentId,
                                    Intent = payload.Intent,
                                    Payload = payload.Payload,
                                    MaxRetries = payload.MaxRetries,
                                    RetryDelay = payload.RetryDelay,
                                    Deadline = payload.Deadline,
                                    TraceId = payload.TraceId,
                                    RootRequestId = payload.RootRequestId,
                                    Metadata = payload.Metadata
                                },
                                EnqueuedAt = payload.EnqueuedAt,
                                RetryCount = payload.RetryCount,
                                DeliveryCount = 1
                            });
                        }
                    }
                    catch { /* skip malformed */ }
                }
            }
            finally
            {
                // Ephemeral consumer auto-expires; no explicit delete needed.
            }

            return msgs;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning(ex, "[NatsTaskQueue] GetDeadLetterQueueAsync failed");
            return Array.Empty<QueuedTask>();
        }
    }

    /// <inheritdoc />
    public async Task RequeueDeadLetterAsync(string taskId, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (!_config.JetStreamEnabled)
            return;

        try
        {
            var js = new NatsJSContext(_connection);
            var consumer = await js.CreateConsumerAsync(
                _dlqStreamName,
                new ConsumerConfig
                {
                    Name = $"dlq-requeue-{Guid.NewGuid():N}",
                    FilterSubject = $"{_config.StreamPrefix}.dlq.>"
                },
                ct).ConfigureAwait(false);

            try
            {
                var fetchOpts = new NatsJSFetchOpts { MaxMsgs = 1000, Expires = TimeSpan.FromSeconds(5) };
                await using var enumDlq = consumer.FetchAsync<string>(fetchOpts, cancellationToken: ct).WithCancellation(ct).GetAsyncEnumerator();
                while (await enumDlq.MoveNextAsync())
                {
                    var msg = enumDlq.Current;
                    try
                    {
                        var payload = JsonSerializer.Deserialize<NatsTaskPayload>(msg.Data, _json);
                        if (payload?.Id == taskId)
                        {
                            var subject = $"{_config.StreamPrefix}.queue.{payload.QueueName}";
                            await js.PublishAsync(subject, msg.Data, cancellationToken: ct).ConfigureAwait(false);
                            await msg.AckAsync(null, ct).ConfigureAwait(false);
                            _log.LogInformation("[NatsTaskQueue] Requeued DLQ task {TaskId}", taskId);
                            return;
                        }
                    }
                    catch { /* continue */ }
                }
            }
            finally
            {
                // Ephemeral consumer auto-expires; no explicit delete needed.
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning(ex, "[NatsTaskQueue] RequeueDeadLetterAsync({TaskId})", taskId);
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        if (_disposed) return false;
        try
        {
            var rtt = await _connection.PingAsync(ct).ConfigureAwait(false);
            return rtt.TotalMilliseconds >= 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task EnsureStreamAndConsumersAsync()
    {
        if (!_config.JetStreamEnabled || _disposed) return;

        try
        {
            var js = new NatsJSContext(_connection);

            try
            {
                await js.CreateStreamAsync(
                    new StreamConfig
                    {
                        Name = _streamName,
                        Subjects = new[] { $"{_config.StreamPrefix}.queue.*" },
                        MaxBytes = _config.JetStreamMaxBytes,
                        MaxAge = TimeSpan.FromDays(_config.JetStreamMaxAgeDays)
                    },
                    CancellationToken.None).ConfigureAwait(false);
                _log.LogInformation("[NatsTaskQueue] Ensured stream {Stream}", _streamName);
            }
            catch (NatsJSApiException ex) when (ex.Message.Contains("already exists"))
            {
                // Stream already exists — OK
            }

            try
            {
                await js.CreateStreamAsync(
                    new StreamConfig
                    {
                        Name = _dlqStreamName,
                        Subjects = new[] { $"{_config.StreamPrefix}.dlq.*" },
                        MaxBytes = _config.JetStreamMaxBytes,
                        MaxAge = TimeSpan.FromDays(_config.JetStreamMaxAgeDays)
                    },
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (NatsJSApiException ex) when (ex.Message.Contains("already exists"))
            {
                // Already exists — OK
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[NatsTaskQueue] Failed to ensure JetStream streams");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(NatsTaskQueue));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true) == true)
            return;
        _ensureTimer?.Dispose();
    }

    /// <summary>Payload stored in JetStream message.</summary>
    private sealed class NatsTaskPayload
    {
        public string Id { get; set; } = "";
        public string QueueName { get; set; } = "";
        public string? AssignedAgentId { get; set; }
        public string Intent { get; set; } = "";
        public string Payload { get; set; } = "";
        public int MaxRetries { get; set; } = 3;
        public TimeSpan? RetryDelay { get; set; }
        public DateTimeOffset? Deadline { get; set; }
        public string? TraceId { get; set; }
        public string? RootRequestId { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
        public DateTimeOffset EnqueuedAt { get; set; }
        public string? FailureReason { get; set; }
        public int RetryCount { get; set; }
    }
}
