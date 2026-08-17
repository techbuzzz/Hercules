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
///     At-least-once delivery via JetStream acks (AckAsync / NakAsync / AckTerminateAsync).
///     On retry exhaustion the message is terminated and appended to a local JSONL DLQ
///     (<see cref="NatsTaskDlqStore"/>) for manual or automatic requeue.
///     Spec: task_068 (initial); task_074 (ack/fail + DLQ).
/// </summary>
public sealed class NatsTaskQueue : ITaskQueue
{
    private readonly NatsConnection? _connection;
    private readonly NatsMeshConfig _config;
    private readonly ILogger<NatsTaskQueue> _log;
    private readonly JsonSerializerOptions _json;
    private readonly ConcurrentDictionary<string, string> _consumerIds = new();
    private readonly Timer _ensureTimer;
    private readonly string _streamName;
    private readonly NatsTaskInFlightTracker _inFlight = new();
    private readonly NatsTaskDlqStore? _dlq;
    private readonly string _dlqFilePath;
    private bool _disposed;

    public string BackendKind => "nats";

    public NatsTaskQueue(
        NatsConnection? connection,
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

        // Resolve DLQ file path (task_074). Default: {DataRoot}/{StreamPrefix}-dlq.jsonl
        var dataRoot = string.IsNullOrEmpty(config.DataRoot)
            ? Hercules.BuiltIn.ResolveDataRoot()
            : config.DataRoot;
        var dlqFileName = string.IsNullOrEmpty(config.DlqFileName)
            ? $"{config.StreamPrefix}-dlq.jsonl"
            : config.DlqFileName;
        _dlqFilePath = Path.IsPathRooted(dlqFileName)
            ? dlqFileName
            : Path.Combine(dataRoot, dlqFileName);
        _dlq = new NatsTaskDlqStore(_dlqFilePath, log);

        _ensureTimer = new Timer(
            static state => ((NatsTaskQueue)state!).EnsureStreamAndConsumersAsync().ConfigureAwait(false).GetAwaiter().GetResult(),
            this,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(30));
    }

    /// <summary>Absolute path of the local JSONL DLQ file (task_074).</summary>
    public string DlqFilePath => _dlqFilePath;

    /// <summary>Exposed for tests/diagnostics: current in-flight tracker (task_074).</summary>
    internal NatsTaskInFlightTracker InFlight => _inFlight;

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
            if (_connection is null)
            {
                _log.LogWarning("[NatsTaskQueue] No connection available; enqueue of {TaskId} is a no-op", taskId);
            }
            else if (_config.JetStreamEnabled)
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

        if (_connection is null)
        {
            _log.LogWarning("[NatsTaskQueue] DequeueAsync called with no NATS connection");
            return null;
        }

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
                            MaxDeliver = _config.MaxDeliveryAttempts,
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

                            // Track for AckAsync / FailAsync (task_074).
                            // Capture action delegates because the original NatsJSMsg may
                            // be disposed when the fetch loop exits; JetStream ack/nak/term
                            // is a separate publish and does not need the live message.
                            var meshTask = new MeshTask
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
                            };
                            var entry = new NatsTaskInFlightTracker.Entry
                            {
                                TaskId = payload.Id,
                                QueueName = payload.QueueName,
                                NumDelivered = deliveryCount,
                                Task = meshTask,
                                AckAsync = innerCt => msg.AckAsync(null, innerCt),
                                NakAsync = (opts, innerCt) => msg.NakAsync(opts, innerCt),
                                TerminateAsync = (opts, innerCt) => msg.AckTerminateAsync(opts, innerCt)
                            };
                            _inFlight.AddOrReplace(entry);

                            return new QueuedTask
                            {
                                Id = payload.Id,
                                ReceiptHandle = payload.Id,
                                Task = meshTask,
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

        var entry = _inFlight.Get(taskId);
        if (entry is null)
        {
            _log.LogDebug("[NatsTaskQueue] Ack for unknown or already-completed task {TaskId}", taskId);
            return;
        }

        try
        {
            await entry.AckAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning(ex, "[NatsTaskQueue] AckAsync failed for {TaskId}", taskId);
        }
        finally
        {
            _inFlight.Remove(taskId);
        }
    }

    /// <inheritdoc />
    public async Task FailAsync(string taskId, string reason, int retry, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var entry = _inFlight.Get(taskId);
        if (entry is null)
        {
            _log.LogDebug("[NatsTaskQueue] Fail for unknown or already-completed task {TaskId}", taskId);
            return;
        }

        // Decision: Nak for retry, or Terminate + DLQ when budget exhausted.
        // We honor both the worker's MaxRetries and JetStream's MaxDeliveryAttempts
        // (whichever trips first) to guarantee no message is silently lost.
        var maxRetries = entry.Task.MaxRetries;
        var maxAttempts = _config.MaxDeliveryAttempts;
        var shouldTerminate = retry >= maxRetries || entry.NumDelivered >= maxAttempts;

        try
        {
            if (shouldTerminate)
            {
                await TerminateToDlqAsync(entry, reason, retry, ct).ConfigureAwait(false);
            }
            else
            {
                await entry.NakAsync(null, ct).ConfigureAwait(false);
                _log.LogInformation(
                    "[NatsTaskQueue] Task {TaskId} nacked for retry {Retry}/{MaxRetries} (delivery {Delivery}/{MaxAttempts}): {Reason}",
                    taskId, retry + 1, maxRetries, entry.NumDelivered, maxAttempts, reason);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning(ex, "[NatsTaskQueue] FailAsync failed for {TaskId}", taskId);
        }
        finally
        {
            _inFlight.Remove(taskId);
        }
    }

    private async Task TerminateToDlqAsync(
        NatsTaskInFlightTracker.Entry entry,
        string reason,
        int retry,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // 1. Append to local DLQ file first (so a failure to terminate doesn't lose the task).
        if (_dlq is not null)
        {
            try
            {
                await _dlq.AppendAsync(entry.Task, reason, entry.NumDelivered, entry.QueueName, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[NatsTaskQueue] Failed to write DLQ entry for {TaskId}", entry.TaskId);
            }
        }

        // 2. Terminate the message so JetStream stops redelivering.
        try
        {
            await entry.TerminateAsync(
                new AckOpts { TerminateReason = TruncateReason(reason) },
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning(ex, "[NatsTaskQueue] TerminateAsync failed for {TaskId}", entry.TaskId);
        }

        _log.LogWarning(
            "[NatsTaskQueue] Task {TaskId} moved to DLQ after {Retries} retries / {Deliveries} deliveries: {Reason}",
            entry.TaskId, retry, entry.NumDelivered, reason);
    }

    private static string TruncateReason(string reason) =>
        reason.Length <= 200 ? reason : reason[..200];

    /// <inheritdoc />
    public async Task<IReadOnlyList<QueuedTask>> GetDeadLetterQueueAsync(string queueName, int limit = 100, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (_dlq is null)
            return Array.Empty<QueuedTask>();

        try
        {
            var entries = await _dlq.ListAsync(queueName, limit, ct).ConfigureAwait(false);
            var result = new List<QueuedTask>(entries.Count);
            foreach (var e in entries)
            {
                result.Add(new QueuedTask
                {
                    Id = e.TaskId,
                    ReceiptHandle = e.TaskId,
                    Task = new MeshTask
                    {
                        Id = e.TaskId,
                        QueueName = e.QueueName,
                        Intent = e.Intent,
                        Payload = e.Payload,
                        MaxRetries = e.MaxRetries,
                        Metadata = new Dictionary<string, string>()
                    },
                    EnqueuedAt = e.FailedAt,
                    RetryCount = e.RetryCount,
                    DeliveryCount = e.DeliveryCount
                });
            }
            return result;
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

        if (_dlq is null || _connection is null)
            return;

        // 1. Read DLQ file to find the entry.
        var entries = await _dlq.ListAsync(queueName: null, limit: int.MaxValue, ct).ConfigureAwait(false);
        var entry = entries.FirstOrDefault(e => string.Equals(e.TaskId, taskId, StringComparison.Ordinal));
        if (entry is null)
        {
            _log.LogDebug("[NatsTaskQueue] RequeueDeadLetterAsync: {TaskId} not in DLQ", taskId);
            return;
        }

        // 2. Republish into the working stream.
        try
        {
            var payload = new NatsTaskPayload
            {
                Id = entry.TaskId,
                QueueName = entry.QueueName,
                Intent = entry.Intent,
                Payload = entry.Payload,
                MaxRetries = entry.MaxRetries,
                EnqueuedAt = DateTimeOffset.UtcNow,
                RetryCount = 0
            };
            var json = JsonSerializer.Serialize(payload, _json);
            var subject = $"{_config.StreamPrefix}.queue.{entry.QueueName}";

            if (_config.JetStreamEnabled)
            {
                var js = new NatsJSContext(_connection);
                await js.PublishAsync(subject, json, cancellationToken: ct).ConfigureAwait(false);
            }
            else
            {
                await _connection.PublishAsync(subject, json, cancellationToken: ct).ConfigureAwait(false);
            }

            // 3. Remove the entry from the local DLQ file.
            await _dlq.RemoveAsync(taskId, ct).ConfigureAwait(false);
            _log.LogInformation("[NatsTaskQueue] Requeued DLQ task {TaskId} to queue {Queue}", taskId, entry.QueueName);
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
        if (_connection is null) return false;
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
        if (_connection is null) return;

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
            // task_074: the local JSONL DLQ file is created on demand by NatsTaskDlqStore.
            // The legacy JetStream DLQ stream (subjects: {prefix}.dlq.*) is no longer
            // created or consumed — see NatsTaskDlqStore for the replacement.
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
