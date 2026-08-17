using System.Text.Json;
using Hercules.Config;
using Microsoft.Data.Sqlite;

namespace Hercules.Mesh.TaskLifecycle;

/// <summary>
///     SQLite-backed persistence for inter-agent delegated tasks (task_106).
///     Mirrors the SqliteSessionStore / SkillQualityStore pattern:
///     own <see cref="SqliteConnection"/>, own <see cref="SemaphoreSlim"/>(1,1)
///     for thread-safety (per task_071), schema bootstrap on construction,
///     idempotent Dispose.
/// </summary>
public sealed class SqliteDelegatedTaskStore : IDelegatedTaskStore, IAsyncDisposable, IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly SemaphoreSlim _connLock = new(1, 1);
    private int _disposed;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public SqliteDelegatedTaskStore(StorageConfig cfg)
    {
        Directory.CreateDirectory(cfg.DataRoot);
        var dbPath = Path.Combine(cfg.DataRoot, cfg.SqliteFile);
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        _conn = new SqliteConnection(connStr);
        _conn.Open();
        EnableWalMode();
        InitSchema();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _connLock.Wait();
        try { _conn.Dispose(); }
        finally { _connLock.Release(); _connLock.Dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _connLock.WaitAsync().ConfigureAwait(false);
        try { _conn.Dispose(); }
        finally { _connLock.Release(); _connLock.Dispose(); }
    }

    public bool IsHealthy()
    {
        if (Volatile.Read(ref _disposed) != 0) return false;
        if (!_connLock.Wait(0)) return false;
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            return cmd.ExecuteScalar() is not null;
        }
        catch { return false; }
        finally { _connLock.Release(); }
    }

    private void EnableWalMode()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
    }

    private void InitSchema()
    {
        const string ddl = """
                           CREATE TABLE IF NOT EXISTS delegated_tasks (
                               task_id              TEXT PRIMARY KEY,
                               parent_request_id    TEXT NOT NULL,
                               caller_agent_id      TEXT NOT NULL,
                               intent               TEXT NOT NULL,
                               payload              TEXT NOT NULL,
                               state                TEXT NOT NULL,
                               result               TEXT,
                               error                TEXT,
                               cancellation_reason  TEXT,
                               expires_at           TEXT,
                               awaiting_input_json  TEXT,
                               local_task_id        TEXT,
                               created_at           TEXT NOT NULL,
                               updated_at           TEXT NOT NULL,
                               completed_at         TEXT
                           );
                           CREATE INDEX IF NOT EXISTS ix_delegated_tasks_state     ON delegated_tasks(state);
                           CREATE INDEX IF NOT EXISTS ix_delegated_tasks_parent    ON delegated_tasks(parent_request_id);
                           CREATE INDEX IF NOT EXISTS ix_delegated_tasks_caller    ON delegated_tasks(caller_agent_id);
                           CREATE INDEX IF NOT EXISTS ix_delegated_tasks_expires   ON delegated_tasks(expires_at);
                           """;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = ddl;
        cmd.ExecuteNonQuery();
    }

    public async Task SaveAsync(DelegatedTask task, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (string.IsNullOrEmpty(task.TaskId))
            throw new ArgumentException("DelegatedTask.TaskId is required", nameof(task));

        await _connLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              INSERT INTO delegated_tasks
                                (task_id, parent_request_id, caller_agent_id, intent, payload, state,
                                 result, error, cancellation_reason, expires_at,
                                 awaiting_input_json, local_task_id,
                                 created_at, updated_at, completed_at)
                              VALUES
                                ($id, $parent, $caller, $intent, $payload, $state,
                                 $result, $error, $reason, $expires,
                                 $awaiting, $local,
                                 $created, $updated, $completed)
                              ON CONFLICT(task_id) DO UPDATE SET
                                parent_request_id   = excluded.parent_request_id,
                                caller_agent_id     = excluded.caller_agent_id,
                                intent              = excluded.intent,
                                payload             = excluded.payload,
                                state               = excluded.state,
                                result              = excluded.result,
                                error               = excluded.error,
                                cancellation_reason = excluded.cancellation_reason,
                                expires_at          = excluded.expires_at,
                                awaiting_input_json = excluded.awaiting_input_json,
                                local_task_id       = excluded.local_task_id,
                                updated_at          = excluded.updated_at,
                                completed_at        = excluded.completed_at
                              """;

            cmd.Parameters.AddWithValue("$id",       task.TaskId);
            cmd.Parameters.AddWithValue("$parent",   task.ParentRequestId);
            cmd.Parameters.AddWithValue("$caller",   task.CallerAgentId);
            cmd.Parameters.AddWithValue("$intent",   task.Intent);
            cmd.Parameters.AddWithValue("$payload",  task.Payload);
            cmd.Parameters.AddWithValue("$state",    task.State.ToString());
            cmd.Parameters.AddWithValue("$result",   (object?)task.Result ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$error",    (object?)task.Error ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$reason",   (object?)task.CancellationReason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$expires",  task.ExpiresAt?.ToString("o") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$awaiting", SerializeAwaiting(task.AwaitingInputContext));
            cmd.Parameters.AddWithValue("$local",    (object?)task.LocalTaskId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$created",  task.CreatedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$updated",  task.UpdatedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$completed",task.CompletedAt?.ToString("o") ?? (object)DBNull.Value);

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _connLock.Release();
        }
    }

    public async Task<DelegatedTask?> GetAsync(string taskId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(taskId)) return null;

        await _connLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              SELECT task_id, parent_request_id, caller_agent_id, intent, payload, state,
                                     result, error, cancellation_reason, expires_at,
                                     awaiting_input_json, local_task_id,
                                     created_at, updated_at, completed_at
                                FROM delegated_tasks
                               WHERE task_id = $id
                              """;
            cmd.Parameters.AddWithValue("$id", taskId);
            using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            return await r.ReadAsync(ct).ConfigureAwait(false) ? Map(r) : null;
        }
        finally
        {
            _connLock.Release();
        }
    }

    public async Task<List<DelegatedTask>> ListAsync(
        DelegatedTaskState? stateFilter = null,
        string? parentRequestId = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        if (limit <= 0) limit = 100;

        await _connLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _conn.CreateCommand();
            var sql = """
                      SELECT task_id, parent_request_id, caller_agent_id, intent, payload, state,
                             result, error, cancellation_reason, expires_at,
                             awaiting_input_json, local_task_id,
                             created_at, updated_at, completed_at
                        FROM delegated_tasks
                      """;
            var where = new List<string>();
            if (stateFilter.HasValue)
            {
                where.Add("state = $state");
                cmd.Parameters.AddWithValue("$state", stateFilter.Value.ToString());
            }
            if (!string.IsNullOrEmpty(parentRequestId))
            {
                where.Add("parent_request_id = $parent");
                cmd.Parameters.AddWithValue("$parent", parentRequestId);
            }
            if (where.Count > 0) sql += " WHERE " + string.Join(" AND ", where);
            sql += " ORDER BY created_at ASC LIMIT $limit";
            cmd.Parameters.AddWithValue("$limit", limit);
            cmd.CommandText = sql;

            var list = new List<DelegatedTask>();
            using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                list.Add(Map(r));
            return list;
        }
        finally
        {
            _connLock.Release();
        }
    }

    public async Task<List<DelegatedTask>> ListPendingAsync(CancellationToken ct = default)
    {
        // Pending = any non-terminal state. We fetch the three known pending
        // states in a single pass. (Could be combined into a NOT IN clause but
        // explicit states keep the SQL self-documenting and let us index-hint.)
        await _connLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              SELECT task_id, parent_request_id, caller_agent_id, intent, payload, state,
                                     result, error, cancellation_reason, expires_at,
                                     awaiting_input_json, local_task_id,
                                     created_at, updated_at, completed_at
                                FROM delegated_tasks
                               WHERE state IN ('Accepted', 'Working', 'AwaitingInput')
                               ORDER BY created_at ASC
                              """;
            var list = new List<DelegatedTask>();
            using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                list.Add(Map(r));
            return list;
        }
        finally
        {
            _connLock.Release();
        }
    }

    public async Task DeleteAsync(string taskId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(taskId)) return;

        await _connLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM delegated_tasks WHERE task_id = $id";
            cmd.Parameters.AddWithValue("$id", taskId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _connLock.Release();
        }
    }

    public async Task<int> PruneExpiredAsync(DateTimeOffset asOf, CancellationToken ct = default)
    {
        await _connLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _conn.CreateCommand();
            // Terminal states: Completed, Failed, Cancelled, Expired.
            // We only delete terminal tasks that have already been cleaned up by
            // the protocol's CheckExpiredAsync path. Non-terminal expired tasks
            // are first transitioned to Expired by the protocol, then pruned here.
            cmd.CommandText = """
                              DELETE FROM delegated_tasks
                               WHERE expires_at IS NOT NULL
                                 AND expires_at <= $cutoff
                                 AND state IN ('Completed', 'Failed', 'Cancelled', 'Expired')
                              """;
            cmd.Parameters.AddWithValue("$cutoff", asOf.ToString("o"));
            return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _connLock.Release();
        }
    }

    // ---- mapping helpers ----

    private static DelegatedTask Map(SqliteDataReader r) => new(
        TaskId:            r.GetString(0),
        ParentRequestId:   r.GetString(1),
        CallerAgentId:     r.GetString(2),
        Intent:            r.GetString(3),
        Payload:           r.GetString(4),
        State:             Enum.Parse<DelegatedTaskState>(r.GetString(5)),
        CreatedAt:         DateTimeOffset.Parse(r.GetString(12)),
        UpdatedAt:         DateTimeOffset.Parse(r.GetString(13)),
        CompletedAt:       r.IsDBNull(14) ? null : DateTimeOffset.Parse(r.GetString(14)),
        Result:            r.IsDBNull(6)  ? null : r.GetString(6),
        Error:             r.IsDBNull(7)  ? null : r.GetString(7),
        CancellationReason:r.IsDBNull(8)  ? null : r.GetString(8),
        ExpiresAt:         r.IsDBNull(9)  ? null : DateTimeOffset.Parse(r.GetString(9)),
        AwaitingInputContext: r.IsDBNull(10) ? null : DeserializeAwaiting(r.GetString(10)),
        LocalTaskId:       r.IsDBNull(11) ? null : r.GetString(11));

    private static object SerializeAwaiting(AwaitingInputContext? ctx) =>
        ctx is null ? DBNull.Value : (object)JsonSerializer.Serialize(ctx, JsonOpts);

    private static AwaitingInputContext? DeserializeAwaiting(string json) =>
        string.IsNullOrEmpty(json)
            ? null
            : JsonSerializer.Deserialize<AwaitingInputContext>(json, JsonOpts);
}
