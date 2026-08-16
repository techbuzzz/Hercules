using System.Collections.Concurrent;
using System.Text.Json;
using Hercules.Mesh.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using RedisMeshConfig = Hercules.Mesh.Backends.Redis.RedisMeshConfig;

/// <summary>
///     Distributed durable task queue using Redis lists.
///     Enqueue: RPUSH. Dequeue: BLPOP (blocking). Visibility timeout: sorted set + background requeue.
///     Dead-letter queue: separate list per queue. At-least-once delivery.
///     Spec: task_067.
/// </summary>
public sealed class RedisTaskQueue : ITaskQueue
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisMeshConfig _config;
    private readonly ILogger<RedisTaskQueue> _log;
    private readonly JsonSerializerOptions _json;
    private readonly Timer _visibilityTimer;
    private readonly ConcurrentDictionary<string, Task<RedisValue>> _dequeueTasks = new();
    private bool _disposed;

    public string BackendKind => "redis";

    public RedisTaskQueue(
        IConnectionMultiplexer redis,
        RedisMeshConfig config,
        ILogger<RedisTaskQueue> log)
    {
        _redis = redis;
        _config = config;
        _log = log;
        _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        // Background timer: scan for timed-out in-flight tasks and re-enqueue them
        _visibilityTimer = new Timer(
            static state => ((RedisTaskQueue)state!).RequeueTimedOutTasksTimer(),
            this,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
    }

    /// <inheritdoc />
    public async Task<QueuedTask> EnqueueAsync(MeshTask task, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(task);

        var queueName = string.IsNullOrEmpty(task.QueueName) ? "default" : task.QueueName;
        var taskId = string.IsNullOrEmpty(task.Id) ? Guid.NewGuid().ToString("N") : task.Id;
        var receipt = $"{taskId}:{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;

        var payload = new RedisTaskPayload
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
        var db = _redis.GetDatabase();
        var queueKey = QueueKey(queueName);
        var receiptKey = ReceiptKey(receipt);

        // Store receipt metadata with TTL = max visibility timeout * max retries
        var receiptTtl = TimeSpan.FromSeconds(_config.DefaultVisibilityTimeoutSec * Math.Max(task.MaxRetries, 1) * 2);
        await db.StringSetAsync(receiptKey, json, receiptTtl).ConfigureAwait(false);

        // Add to queue
        await db.ListRightPushAsync(queueKey, receipt).ConfigureAwait(false);

        return new QueuedTask
        {
            Id = taskId,
            ReceiptHandle = receipt,
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

        var queueKey = QueueKey(queueName);
        var db = _redis.GetDatabase();

        try
        {
            // BLPOP — blocking left pop with timeout
            var popTask = db.ListLeftPopAsync(queueKey);
            var timeout = TimeSpan.FromMilliseconds(Math.Max(100, visibilityTimeout.TotalMilliseconds * 0.9));
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            var result = await popTask.WaitAsync(cts.Token).ConfigureAwait(false);

            if (result.IsNullOrEmpty)
                return null;

            var receipt = result.ToString();
            return await LoadAndTrackTaskAsync(db, queueName, receipt, visibilityTimeout, ct).ConfigureAwait(false);
        }
        catch (RedisConnectionException ex)
        {
            _log.LogWarning(ex, "[RedisTaskQueue] Redis unavailable for DequeueAsync({Queue})", queueName);
            return null;
        }
    }

    private async Task<QueuedTask?> LoadAndTrackTaskAsync(
        IDatabase db,
        string queueName,
        string receipt,
        TimeSpan visibilityTimeout,
        CancellationToken ct)
    {
        var receiptKey = ReceiptKey(receipt);

        // Load task metadata from receipt key
        var json = await db.StringGetAsync(receiptKey).WaitAsync(ct).ConfigureAwait(false);
        if (json.IsNullOrEmpty)
        {
            _log.LogWarning("[RedisTaskQueue] Receipt {Receipt} metadata not found", receipt);
            return null;
        }

        var payload = JsonSerializer.Deserialize<RedisTaskPayload>(json.ToString(), _json);
        if (payload is null) return null;

        // Add to in-flight sorted set: score = expiry timestamp
        var expiryMs = DateTimeOffset.UtcNow.Add(visibilityTimeout).ToUnixTimeMilliseconds();
        var inFlightKey = InFlightKey(queueName);
        await db.SortedSetAddAsync(inFlightKey, receipt, expiryMs).ConfigureAwait(false);

        // [task_086] Maintain a global hash index from taskId → "{receiptHandle}|{queueName}"
        // so AckAsync/FailAsync can locate the in-flight entry in O(1) without
        // scanning every in-flight key via server.Keys(pattern:...).
        var lookupKey = InFlightLookupKey();
        await db.HashSetAsync(lookupKey, payload.Id, $"{receipt}|{queueName}").ConfigureAwait(false);

        // Set expiry on receipt key (refresh to avoid premature expiry during processing)
        var newTtl = TimeSpan.FromSeconds(_config.DefaultVisibilityTimeoutSec * 2);
        await db.KeyExpireAsync(receiptKey, newTtl).ConfigureAwait(false);

        return new QueuedTask
        {
            Id = payload.Id,
            ReceiptHandle = receipt,
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

    /// <inheritdoc />
    public async Task AckAsync(string taskId, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        try
        {
            var db = _redis.GetDatabase();
            // [task_086] O(1) lookup: taskId → "{receiptHandle}|{queueName}"
            // via the global in-flight hash index (no server.Keys scan).
            var lookupKey = InFlightLookupKey();
            var entry = await db.HashGetAsync(lookupKey, taskId).WaitAsync(ct).ConfigureAwait(false);
            if (entry.IsNullOrEmpty)
            {
                _log.LogDebug("[RedisTaskQueue] Ack for unknown task {TaskId} (not in in-flight index)", taskId);
                return;
            }

            var parsed = ParseLookupEntry(entry.ToString());
            if (parsed is null)
            {
                _log.LogWarning("[RedisTaskQueue] Ack: malformed in-flight lookup entry for {TaskId}", taskId);
                await db.HashDeleteAsync(lookupKey, taskId).ConfigureAwait(false);
                return;
            }

            var (receipt, queueName) = parsed.Value;
            var inFlightKey = InFlightKey(queueName);
            var receiptKey = ReceiptKey(receipt);

            await db.SortedSetRemoveAsync(inFlightKey, receipt).ConfigureAwait(false);
            await db.KeyDeleteAsync(receiptKey).ConfigureAwait(false);
            await db.HashDeleteAsync(lookupKey, taskId).ConfigureAwait(false);
        }
        catch (RedisConnectionException ex)
        {
            _log.LogWarning(ex, "[RedisTaskQueue] Redis unavailable for AckAsync({TaskId})", taskId);
        }
    }

    /// <inheritdoc />
    public async Task FailAsync(string taskId, string reason, int retry, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        try
        {
            var db = _redis.GetDatabase();
            // [task_086] O(1) lookup: taskId → "{receiptHandle}|{queueName}".
            var lookupKey = InFlightLookupKey();
            var entry = await db.HashGetAsync(lookupKey, taskId).WaitAsync(ct).ConfigureAwait(false);
            if (entry.IsNullOrEmpty)
            {
                _log.LogDebug("[RedisTaskQueue] Fail for unknown task {TaskId} (not in in-flight index)", taskId);
                return;
            }

            var parsed = ParseLookupEntry(entry.ToString());
            if (parsed is null)
            {
                _log.LogWarning("[RedisTaskQueue] Fail: malformed in-flight lookup entry for {TaskId}", taskId);
                await db.HashDeleteAsync(lookupKey, taskId).ConfigureAwait(false);
                return;
            }

            var (receipt, queueName) = parsed.Value;
            var inFlightKey = InFlightKey(queueName);
            var receiptKey = ReceiptKey(receipt);

            var json = await db.StringGetAsync(receiptKey).WaitAsync(ct).ConfigureAwait(false);
            if (json.IsNullOrEmpty)
            {
                // Receipt expired — clean up the lookup and bail.
                _log.LogWarning("[RedisTaskQueue] Fail: receipt {Receipt} missing for task {TaskId}", receipt, taskId);
                await db.SortedSetRemoveAsync(inFlightKey, receipt).ConfigureAwait(false);
                await db.HashDeleteAsync(lookupKey, taskId).ConfigureAwait(false);
                return;
            }

            var payload = JsonSerializer.Deserialize<RedisTaskPayload>(json.ToString(), _json);
            if (payload is null)
            {
                _log.LogWarning("[RedisTaskQueue] Fail: cannot deserialize receipt for {TaskId}", taskId);
                await db.SortedSetRemoveAsync(inFlightKey, receipt).ConfigureAwait(false);
                await db.KeyDeleteAsync(receiptKey).ConfigureAwait(false);
                await db.HashDeleteAsync(lookupKey, taskId).ConfigureAwait(false);
                return;
            }

            await HandleTaskFailureAsync(db, inFlightKey, receipt, receiptKey, payload, reason, retry).ConfigureAwait(false);
            await db.HashDeleteAsync(lookupKey, taskId).ConfigureAwait(false);
        }
        catch (RedisConnectionException ex)
        {
            _log.LogWarning(ex, "[RedisTaskQueue] Redis unavailable for FailAsync({TaskId})", taskId);
        }
    }

    private async Task HandleTaskFailureAsync(
        IDatabase db,
        RedisKey inFlightKey,
        string receipt,
        string receiptKey,
        RedisTaskPayload payload,
        string reason,
        int retry)
    {
        // Remove from in-flight
        await db.SortedSetRemoveAsync(inFlightKey, receipt).ConfigureAwait(false);
        await db.KeyDeleteAsync(receiptKey).ConfigureAwait(false);

        var queueName = payload.QueueName;
        var dlqName = DlqKey(queueName);

        if (retry < payload.MaxRetries)
        {
            // Re-enqueue with delay
            var delay = payload.RetryDelay ?? TimeSpan.Zero;
            if (delay > TimeSpan.Zero)
            {
                // Schedule re-enqueue via Redis sorted set (score = expiry time)
                var requeueAt = DateTimeOffset.UtcNow.Add(delay).ToUnixTimeMilliseconds();
                var pendingKey = PendingRequeueKey(queueName);
                var newReceipt = $"{payload.Id}:{Guid.NewGuid():N}";
                var updatedPayload = payload with { EnqueuedAt = DateTimeOffset.UtcNow };
                var newJson = JsonSerializer.Serialize(updatedPayload, _json);
                var newReceiptKey = ReceiptKey(newReceipt);
                await db.StringSetAsync(newReceiptKey, newJson, TimeSpan.FromHours(1)).ConfigureAwait(false);
                await db.SortedSetAddAsync(pendingKey, newReceipt, requeueAt).ConfigureAwait(false);
            }
            else
            {
                // Re-enqueue immediately
                var newReceipt = $"{payload.Id}:{Guid.NewGuid():N}";
                var updatedPayload = payload with { EnqueuedAt = DateTimeOffset.UtcNow };
                var newJson = JsonSerializer.Serialize(updatedPayload, _json);
                var newReceiptKey = ReceiptKey(newReceipt);
                await db.StringSetAsync(newReceiptKey, newJson, TimeSpan.FromHours(1)).ConfigureAwait(false);
                await db.ListRightPushAsync(QueueKey(queueName), newReceipt).ConfigureAwait(false);
            }

            _log.LogInformation("[RedisTaskQueue] Task {TaskId} re-enqueued for retry {Retry}/{MaxRetries}",
                payload.Id, retry + 1, payload.MaxRetries);
        }
        else
        {
            // Move to DLQ
            var dlqReceipt = $"{payload.Id}:{Guid.NewGuid():N}";
            var dlqPayload = new RedisTaskPayload
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
                Metadata = payload.Metadata,
                EnqueuedAt = payload.EnqueuedAt,
                FailureReason = reason,
                RetryCount = retry
            };
            var dlqJson = JsonSerializer.Serialize(dlqPayload, _json);
            var dlqReceiptKey = ReceiptKey(dlqReceipt);
            await db.StringSetAsync(dlqReceiptKey, dlqJson, TimeSpan.FromDays(7)).ConfigureAwait(false);
            await db.ListRightPushAsync(dlqName, dlqReceipt).ConfigureAwait(false);

            _log.LogWarning("[RedisTaskQueue] Task {TaskId} moved to DLQ after {Retries} retries: {Reason}",
                payload.Id, retry, reason);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QueuedTask>> GetDeadLetterQueueAsync(string queueName, int limit = 100, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        try
        {
            var db = _redis.GetDatabase();
            var dlqName = DlqKey(queueName);
            var items = await db.ListRangeAsync(dlqName, 0, limit - 1).ConfigureAwait(false);

            var result = new List<QueuedTask>();
            foreach (var item in items)
            {
                var receiptKey = ReceiptKey(item.ToString());
                var json = await db.StringGetAsync(receiptKey).WaitAsync(ct).ConfigureAwait(false);
                if (!json.IsNullOrEmpty)
                {
                    var payload = JsonSerializer.Deserialize<RedisTaskPayload>(json.ToString(), _json);
                    if (payload != null)
                    {
                        result.Add(new QueuedTask
                        {
                            Id = payload.Id,
                            ReceiptHandle = item.ToString(),
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
                            DeliveryCount = payload.MaxRetries - payload.RetryCount
                        });
                    }
                }
            }

            return result;
        }
        catch (RedisConnectionException ex)
        {
            _log.LogWarning(ex, "[RedisTaskQueue] Redis unavailable for GetDeadLetterQueueAsync({Queue})", queueName);
            return Array.Empty<QueuedTask>();
        }
    }

    /// <inheritdoc />
    public async Task RequeueDeadLetterAsync(string taskId, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        try
        {
            var db = _redis.GetDatabase();
            var dlqKeys = new List<RedisKey>();

            foreach (var endpoint in _redis.GetEndPoints())
            {
                var server = _redis.GetServer(endpoint);
                if (server.IsConnected && !server.IsReplica)
                {
                    dlqKeys.AddRange(server.Keys(pattern: $"{_config.KeyPrefix}queue:*-dlq"));
                }
            }

            foreach (var dlqKey in dlqKeys)
            {
                var items = await db.ListRangeAsync(dlqKey).ConfigureAwait(false);
                foreach (var item in items)
                {
                    var receiptKey = ReceiptKey(item.ToString());
                    var json = await db.StringGetAsync(receiptKey).WaitAsync(ct).ConfigureAwait(false);
                    if (!json.IsNullOrEmpty)
                    {
                        var payload = JsonSerializer.Deserialize<RedisTaskPayload>(json.ToString(), _json);
                        if (payload?.Id == taskId)
                        {
                            // Remove from DLQ
                            await db.ListRemoveAsync(dlqKey, item).ConfigureAwait(false);

                            // Re-enqueue
                            var newReceipt = $"{payload.Id}:{Guid.NewGuid():N}";
                            var updatedPayload = payload with { EnqueuedAt = DateTimeOffset.UtcNow, RetryCount = 0 };
                            var newJson = JsonSerializer.Serialize(updatedPayload, _json);
                            var newReceiptKey = ReceiptKey(newReceipt);
                            await db.StringSetAsync(newReceiptKey, newJson, TimeSpan.FromHours(1)).ConfigureAwait(false);

                            var queueName = dlqKey.ToString().Replace(_config.KeyPrefix, "").Replace("-dlq", "");
                            await db.ListRightPushAsync(QueueKey(queueName), newReceipt).ConfigureAwait(false);

                            _log.LogInformation("[RedisTaskQueue] Task {TaskId} requeued from DLQ", taskId);
                            return;
                        }
                    }
                }
            }
        }
        catch (RedisConnectionException ex)
        {
            _log.LogWarning(ex, "[RedisTaskQueue] Redis unavailable for RequeueDeadLetterAsync({TaskId})", taskId);
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

    // task_077: Timer callbacks must be async-void + try/catch. We re-enter the loop via
    // _ = RequeueTimedOutTasksAsync(); exceptions are caught inside the async method.
    private void RequeueTimedOutTasksTimer()
    {
        if (_disposed) return;
        _ = RequeueTimedOutTasksAsync();
    }

    private async Task RequeueTimedOutTasksAsync()
    {
        try
        {
            var db = _redis.GetDatabase();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            foreach (var endpoint in _redis.GetEndPoints())
            {
                var server = _redis.GetServer(endpoint);
                if (!server.IsConnected || server.IsReplica) continue;

                var inFlightKeys = server.Keys(pattern: InFlightKey("*")).ToArray();
                foreach (var inFlightKey in inFlightKeys)
                {
                    // Get expired tasks (score <= now)
                    var expired = await db.SortedSetRangeByScoreAsync(inFlightKey, 0, now).ConfigureAwait(false);
                    if (expired.Length == 0) continue;

                    var queueName = inFlightKey.ToString().Replace(_config.KeyPrefix, "").Replace(":inflight", "");

                    foreach (var receipt in expired)
                    {
                        var receiptKey = ReceiptKey(receipt.ToString());
                        var json = await db.StringGetAsync(receiptKey).ConfigureAwait(false);

                        if (!json.IsNullOrEmpty)
                        {
                            // [task_086] Recover taskId from the receipt JSON and
                            // remove the in-flight lookup entry so AckAsync does
                            // not try to act on a stale receipt handle.
                            string? requeuedTaskId = null;
                            try
                            {
                                var payload = JsonSerializer.Deserialize<RedisTaskPayload>(json.ToString(), _json);
                                requeuedTaskId = payload?.Id;
                            }
                            catch (JsonException)
                            {
                                // Ignore — we'll just leave the lookup entry.
                            }

                            // Re-enqueue with a fresh receipt
                            var newReceipt = $"{Guid.NewGuid():N}:{Guid.NewGuid():N}";
                            var newReceiptKey = ReceiptKey(newReceipt);
                            var newTtl = TimeSpan.FromHours(1);
                            await db.StringSetAsync(newReceiptKey, json, newTtl).ConfigureAwait(false);
                            await db.ListRightPushAsync(QueueKey(queueName), newReceipt).ConfigureAwait(false);

                            if (!string.IsNullOrEmpty(requeuedTaskId))
                            {
                                await db.HashDeleteAsync(InFlightLookupKey(), requeuedTaskId).ConfigureAwait(false);
                            }
                        }

                        // Remove from in-flight
                        await db.SortedSetRemoveAsync(inFlightKey, receipt).ConfigureAwait(false);
                    }
                }

                // Also handle pending re-enqueue sorted sets
                var pendingKeys = server.Keys(pattern: $"{_config.KeyPrefix}queue:*:pending").ToArray();
                foreach (var pendingKey in pendingKeys)
                {
                    var ready = await db.SortedSetRangeByScoreAsync(pendingKey, 0, now).ConfigureAwait(false);
                    foreach (var receipt in ready)
                    {
                        var receiptKey = ReceiptKey(receipt.ToString());
                        var queueName = pendingKey.ToString().Replace(_config.KeyPrefix, "").Replace(":pending", "");
                        await db.ListRightPushAsync(QueueKey(queueName), receipt).ConfigureAwait(false);
                        await db.SortedSetRemoveAsync(pendingKey, receipt).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (RedisConnectionException ex)
        {
            _log.LogDebug(ex, "[RedisTaskQueue] Redis unavailable for visibility timeout requeue");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[RedisTaskQueue] Error in visibility timeout requeue");
        }
    }

    private string QueueKey(string queueName) =>
        $"{_config.KeyPrefix}queue:{queueName}";

    private string DlqKey(string queueName) =>
        $"{_config.KeyPrefix}queue:{queueName}-dlq";

    private string InFlightKey(string queueName) =>
        $"{_config.KeyPrefix}queue:{queueName}:inflight";

    private string PendingRequeueKey(string queueName) =>
        $"{_config.KeyPrefix}queue:{queueName}:pending";

    /// <summary>
    ///     [task_086] Global hash index mapping taskId → "{receiptHandle}|{queueName}".
    ///     Maintained on Dequeue, removed on Ack/Fail/DLQ. Enables O(1) lookup
    ///     without scanning every in-flight sorted set via <c>server.Keys</c>.
    /// </summary>
    private string InFlightLookupKey() =>
        $"{_config.KeyPrefix}inflight:lookup";

    /// <summary>
    ///     Parse "{receiptHandle}|{queueName}" into a tuple. Returns null if
    ///     the entry is malformed (missing separator or empty parts).
    /// </summary>
    private static (string Receipt, string QueueName)? ParseLookupEntry(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var sep = raw.IndexOf('|');
        if (sep <= 0 || sep == raw.Length - 1) return null;
        var receipt = raw[..sep];
        var queueName = raw[(sep + 1)..];
        if (string.IsNullOrEmpty(receipt) || string.IsNullOrEmpty(queueName)) return null;
        return (receipt, queueName);
    }

    private static string ReceiptKey(string receipt) =>
        $"hercules:receipt:{receipt}";

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RedisTaskQueue));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true) == true)
            return;

        _visibilityTimer.Dispose();
    }

    /// <summary>
    ///     JSON payload stored in Redis for task queue receipts.
    /// </summary>
    private sealed record RedisTaskPayload
    {
        public string Id { get; init; } = "";
        public string QueueName { get; init; } = "";
        public string? AssignedAgentId { get; init; }
        public string Intent { get; init; } = "";
        public string Payload { get; init; } = "";
        public int MaxRetries { get; init; } = 3;
        public TimeSpan? RetryDelay { get; init; }
        public DateTimeOffset? Deadline { get; init; }
        public string? TraceId { get; init; }
        public string? RootRequestId { get; init; }
        public Dictionary<string, string> Metadata { get; init; } = new();
        public DateTimeOffset EnqueuedAt { get; init; }
        public string? FailureReason { get; init; }
        public int RetryCount { get; init; }
    }
}
