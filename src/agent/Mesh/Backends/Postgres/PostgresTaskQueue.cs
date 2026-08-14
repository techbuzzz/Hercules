using System.Text.Json;
using Hercules.Mesh.Abstractions;
using Microsoft.Extensions.Logging;
using Npgsql;
using PostgresMeshConfig = Hercules.Mesh.Backends.Postgres.PostgresMeshConfig;

namespace Hercules.Mesh.Backends.Postgres;

/// <summary>
///     Distributed durable task queue backed by PostgreSQL.
///     Uses <c>SELECT ... FOR UPDATE SKIP LOCKED</c> for safe concurrent dequeues,
///     a <c>tasks</c> table for queued work and a <c>tasks_dlq</c> table for dead letters.
///     Visibility timeouts are enforced via a periodic background timer that requeues timed-out tasks.
///     Spec: task_069.
/// </summary>
public sealed class PostgresTaskQueue : ITaskQueue
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly PostgresMeshConfig _config;
    private readonly ILogger<PostgresTaskQueue> _log;
    private readonly JsonSerializerOptions _json;
    private readonly Timer _requeueTimer;
    private int _schemaInitialized;
    private bool _disposed;

    public string BackendKind => "postgres";

    public PostgresTaskQueue(
        NpgsqlDataSource dataSource,
        PostgresMeshConfig config,
        ILogger<PostgresTaskQueue> log)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        _requeueTimer = new Timer(
            static state => ((PostgresTaskQueue)state!).RequeueTimedOutTasks(),
            this,
            TimeSpan.FromMilliseconds(_config.RequeueTimerIntervalMs),
            TimeSpan.FromMilliseconds(_config.RequeueTimerIntervalMs));
    }

    /// <inheritdoc />
    public async Task<QueuedTask> EnqueueAsync(MeshTask task, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(task);

        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        var queueName = string.IsNullOrEmpty(task.QueueName) ? "default" : task.QueueName;
        var taskId = string.IsNullOrEmpty(task.Id) ? Guid.NewGuid().ToString("N") : task.Id;
        var now = DateTimeOffset.UtcNow;

        var payload = new PostgresTaskPayload
        {
            Id = taskId,
            QueueName = queueName,
            AssignedAgentId = task.AssignedAgentId,
            Intent = task.Intent,
            Payload = task.Payload,
            MaxRetries = task.MaxRetries > 0 ? task.MaxRetries : _config.MaxDeliveryAttempts,
            RetryDelay = task.RetryDelay,
            Deadline = task.Deadline,
            TraceId = task.TraceId,
            RootRequestId = task.RootRequestId,
            Metadata = task.Metadata,
            EnqueuedAt = now
        };

        var json = JsonSerializer.Serialize(payload, _json);

        const string sql = @"
            INSERT INTO tasks (id, queue_name, status, payload, enqueued_at, delivery_count, retry_count, max_retries)
            VALUES (@id, @queue, 'queued', @payload::jsonb, @enqueuedAt, 0, 0, @maxRetries)
            ON CONFLICT (id) DO NOTHING";

        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", taskId);
        cmd.Parameters.AddWithValue("queue", queueName);
        cmd.Parameters.AddWithValue("payload", json);
        cmd.Parameters.AddWithValue("enqueuedAt", now);
        cmd.Parameters.AddWithValue("maxRetries", payload.MaxRetries);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        return new QueuedTask
        {
            Id = taskId,
            ReceiptHandle = taskId,
            Task = task with { Id = taskId },
            EnqueuedAt = now,
            RetryCount = 0,
            DeliveryCount = 1
        };
    }

    /// <inheritdoc />
    public async Task<QueuedTask?> DequeueAsync(
        string queueName,
        TimeSpan visibilityTimeout,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        var queue = string.IsNullOrEmpty(queueName) ? "default" : queueName;
        var visibilityExpiry = DateTimeOffset.UtcNow.Add(visibilityTimeout);

        // SELECT ... FOR UPDATE SKIP LOCKED atomically picks one available task and locks it
        // for the duration of the transaction. We then update its status to 'in_flight' and
        // visibility expiry; on commit, no other worker can grab the same row.
        const string sql = @"
            WITH next_task AS (
                SELECT id FROM tasks
                WHERE queue_name = @queue
                  AND status = 'queued'
                  AND (not_before IS NULL OR not_before <= NOW())
                ORDER BY enqueued_at
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            UPDATE tasks t
            SET status = 'in_flight',
                delivery_count = t.delivery_count + 1,
                visibility_expires_at = @visibility,
                receipt_handle = @receipt
            FROM next_task
            WHERE t.id = next_task.id
            RETURNING t.id, t.payload, t.enqueued_at, t.delivery_count, t.retry_count, t.queue_name";

        var receipt = Guid.NewGuid().ToString("N");

        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        QueuedTask? result = null;

        await using (var cmd = new NpgsqlCommand(sql, conn, tx))
        {
            cmd.Parameters.AddWithValue("queue", queue);
            cmd.Parameters.AddWithValue("visibility", visibilityExpiry);
            cmd.Parameters.AddWithValue("receipt", receipt);

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var payloadJson = reader.GetString(1);
                var payload = JsonSerializer.Deserialize<PostgresTaskPayload>(payloadJson, _json);
                if (payload is not null)
                {
                    var deliveryCount = reader.GetInt32(3);
                    var retryCount = reader.GetInt32(4);
                    var queueNameDb = reader.GetString(5);
                    var enqueuedAt = reader.GetFieldValue<DateTime>(2);

                    result = new QueuedTask
                    {
                        Id = reader.GetString(0),
                        ReceiptHandle = receipt,
                        Task = new MeshTask
                        {
                            Id = payload.Id,
                            QueueName = queueNameDb,
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
                        EnqueuedAt = new DateTimeOffset(enqueuedAt, TimeSpan.Zero),
                        RetryCount = retryCount,
                        DeliveryCount = deliveryCount
                    };
                }
            }
        }

        if (result is null)
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            return null;
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public async Task AckAsync(string taskId, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        const string sql = "DELETE FROM tasks WHERE id = @id";
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", taskId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task FailAsync(string taskId, string reason, int retry, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        // Lock the task row, read its payload + retry budget
        const string selectSql = @"
            SELECT payload::text, queue_name, max_retries
            FROM tasks WHERE id = @id FOR UPDATE";
        string? payloadJson = null;
        string? queueName = null;
        int maxRetries = 0;
        await using (var cmd = new NpgsqlCommand(selectSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("id", taskId);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                payloadJson = reader.GetString(0);
                queueName = reader.GetString(1);
                maxRetries = reader.GetInt32(2);
            }
        }

        if (payloadJson is null || queueName is null)
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            return;
        }

        var payload = JsonSerializer.Deserialize<PostgresTaskPayload>(payloadJson, _json);
        if (payload is null)
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            return;
        }

        if (retry < maxRetries)
        {
            // Re-queue with retry counter
            var notBefore = payload.RetryDelay.HasValue
                ? DateTimeOffset.UtcNow.Add(payload.RetryDelay.Value)
                : (DateTimeOffset?)null;

            const string requeueSql = @"
                UPDATE tasks SET
                    status = 'queued',
                    retry_count = @retry,
                    visibility_expires_at = NULL,
                    receipt_handle = NULL,
                    not_before = @notBefore
                WHERE id = @id";
            await using (var cmd = new NpgsqlCommand(requeueSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("retry", retry);
                cmd.Parameters.AddWithValue("notBefore", (object?)notBefore ?? DBNull.Value);
                cmd.Parameters.AddWithValue("id", taskId);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
        else
        {
            // Move to DLQ
            var dlqPayload = payload with { RetryCount = retry, FailureReason = reason };

            const string insertDlqSql = @"
                INSERT INTO tasks_dlq (id, queue_name, payload, enqueued_at, failed_at, retry_count, failure_reason)
                VALUES (@id, @queue, @payload::jsonb, @enqueuedAt, @failedAt, @retry, @reason)";
            await using (var insertCmd = new NpgsqlCommand(insertDlqSql, conn, tx))
            {
                insertCmd.Parameters.AddWithValue("id", taskId);
                insertCmd.Parameters.AddWithValue("queue", queueName);
                insertCmd.Parameters.AddWithValue("payload", JsonSerializer.Serialize(dlqPayload, _json));
                insertCmd.Parameters.AddWithValue("enqueuedAt", dlqPayload.EnqueuedAt);
                insertCmd.Parameters.AddWithValue("failedAt", DateTimeOffset.UtcNow);
                insertCmd.Parameters.AddWithValue("retry", retry);
                insertCmd.Parameters.AddWithValue("reason", reason);
                await insertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            const string deleteSql = "DELETE FROM tasks WHERE id = @id";
            await using (var delCmd = new NpgsqlCommand(deleteSql, conn, tx))
            {
                delCmd.Parameters.AddWithValue("id", taskId);
                await delCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QueuedTask>> GetDeadLetterQueueAsync(
        string queueName,
        int limit = 100,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        const string sql = @"
            SELECT id, payload::text, enqueued_at, retry_count
            FROM tasks_dlq
            WHERE queue_name = @queue
            ORDER BY failed_at DESC
            LIMIT @limit";

        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("queue", queueName);
        cmd.Parameters.AddWithValue("limit", limit);

        var result = new List<QueuedTask>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var payloadJson = reader.GetString(1);
            var payload = JsonSerializer.Deserialize<PostgresTaskPayload>(payloadJson, _json);
            if (payload is null) continue;

            var taskId = reader.GetString(0);
            var enqueuedAt = reader.GetFieldValue<DateTime>(2);
            var retryCount = reader.GetInt32(3);

            result.Add(new QueuedTask
            {
                Id = taskId,
                ReceiptHandle = taskId,
                Task = new MeshTask
                {
                    Id = payload.Id,
                    QueueName = queueName,
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
                EnqueuedAt = new DateTimeOffset(enqueuedAt, TimeSpan.Zero),
                RetryCount = retryCount,
                DeliveryCount = payload.MaxRetries
            });
        }
        return result;
    }

    /// <inheritdoc />
    public async Task RequeueDeadLetterAsync(string taskId, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        const string selectSql = @"
            SELECT payload::text, queue_name FROM tasks_dlq WHERE id = @id FOR UPDATE";
        string? payloadJson = null;
        string? queueName = null;
        await using (var cmd = new NpgsqlCommand(selectSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("id", taskId);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                payloadJson = reader.GetString(0);
                queueName = reader.GetString(1);
            }
        }

        if (payloadJson is null || queueName is null)
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            return;
        }

        var payload = JsonSerializer.Deserialize<PostgresTaskPayload>(payloadJson, _json);
        if (payload is null)
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            return;
        }

        const string insertSql = @"
            INSERT INTO tasks (id, queue_name, status, payload, enqueued_at, delivery_count, retry_count, max_retries)
            VALUES (@id, @queue, 'queued', @payload::jsonb, @enqueuedAt, 0, 0, @maxRetries)
            ON CONFLICT (id) DO UPDATE SET
                status = 'queued',
                payload = EXCLUDED.payload,
                enqueued_at = EXCLUDED.enqueued_at,
                delivery_count = 0,
                retry_count = 0,
                max_retries = EXCLUDED.max_retries";
        await using (var insertCmd = new NpgsqlCommand(insertSql, conn, tx))
        {
            insertCmd.Parameters.AddWithValue("id", taskId);
            insertCmd.Parameters.AddWithValue("queue", queueName);
            insertCmd.Parameters.AddWithValue("payload", JsonSerializer.Serialize(payload, _json));
            insertCmd.Parameters.AddWithValue("enqueuedAt", DateTimeOffset.UtcNow);
            insertCmd.Parameters.AddWithValue("maxRetries", payload.MaxRetries);
            await insertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        const string deleteSql = "DELETE FROM tasks_dlq WHERE id = @id";
        await using (var delCmd = new NpgsqlCommand(deleteSql, conn, tx))
        {
            delCmd.Parameters.AddWithValue("id", taskId);
            await delCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        if (_disposed) return false;
        try
        {
            await using var conn = await OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand("SELECT 1", conn);
            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return result is not null && result is not DBNull;
        }
        catch
        {
            return false;
        }
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        return await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
    }

    private async Task EnsureSchemaAsync(CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _schemaInitialized, 1, 0) != 0)
            return;

        if (!_config.AutoCreateSchema)
        {
            Interlocked.Exchange(ref _schemaInitialized, 1);
            return;
        }

        try
        {
            await using var conn = await OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(GetCreateSchemaSql(), conn);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _schemaInitialized, 0);
            _log.LogWarning(ex, "[PostgresTaskQueue] Failed to ensure schema");
            throw;
        }
    }

    private string GetCreateSchemaSql()
    {
        var schema = QuoteIdent(_config.Schema);
        var tasks = QuoteIdent(_config.TasksTable);
        var dlq = QuoteIdent(_config.DlqTable);
        return $@"
            CREATE SCHEMA IF NOT EXISTS {schema};
            CREATE TABLE IF NOT EXISTS {schema}.{tasks} (
                id TEXT PRIMARY KEY,
                queue_name TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'queued',
                payload JSONB NOT NULL,
                enqueued_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                visibility_expires_at TIMESTAMPTZ NULL,
                not_before TIMESTAMPTZ NULL,
                receipt_handle TEXT NULL,
                delivery_count INT NOT NULL DEFAULT 0,
                retry_count INT NOT NULL DEFAULT 0,
                max_retries INT NOT NULL DEFAULT 3
            );
            CREATE INDEX IF NOT EXISTS idx_{_config.TasksTable}_queue_status
                ON {schema}.{tasks}(queue_name, status, enqueued_at);
            CREATE INDEX IF NOT EXISTS idx_{_config.TasksTable}_visibility
                ON {schema}.{tasks}(visibility_expires_at) WHERE status = 'in_flight';

            CREATE TABLE IF NOT EXISTS {schema}.{dlq} (
                id TEXT PRIMARY KEY,
                queue_name TEXT NOT NULL,
                payload JSONB NOT NULL,
                enqueued_at TIMESTAMPTZ NOT NULL,
                failed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                retry_count INT NOT NULL DEFAULT 0,
                failure_reason TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_{_config.DlqTable}_queue
                ON {schema}.{dlq}(queue_name, failed_at);
        ";
    }

    private static string QuoteIdent(string ident)
    {
        return "\"" + ident.Replace("\"", "\"\"") + "\"";
    }

    private void RequeueTimedOutTasks()
    {
        if (_disposed) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            RequeueTimedOutCoreAsync(cts.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "[PostgresTaskQueue] Requeue timer iteration failed");
        }
    }

    private async Task RequeueTimedOutCoreAsync(CancellationToken ct)
    {
        const string sql = @"
            UPDATE tasks SET
                status = 'queued',
                visibility_expires_at = NULL,
                receipt_handle = NULL,
                retry_count = retry_count + 1
            WHERE status = 'in_flight'
              AND visibility_expires_at IS NOT NULL
              AND visibility_expires_at < NOW()
            RETURNING id";
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        var requeued = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (requeued > 0)
            _log.LogDebug("[PostgresTaskQueue] Requeued {Count} timed-out tasks", requeued);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PostgresTaskQueue));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true) == true)
            return;
        _requeueTimer.Dispose();
    }

    private sealed record PostgresTaskPayload
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
        public int RetryCount { get; set; }
        public string? FailureReason { get; set; }
    }
}
