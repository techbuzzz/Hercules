using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Hercules.Offline;

/// <summary>
///     SQLite-backed implementation of <see cref="IOutboxStore"/> for task_060.
///     Uses the shared SqliteSessionStore connection to co-locate data.
/// </summary>
public sealed class SqliteOutboxStore : IOutboxStore
{
    private readonly SqliteConnection _conn;
    private readonly OfflineSyncConfig _config;
    private readonly ILogger<SqliteOutboxStore> _log;

    public SqliteOutboxStore(
        Hercules.Storage.SqliteSessionStore store,
        OfflineSyncConfig config,
        ILogger<SqliteOutboxStore> log)
    {
        _conn = store.Connection;
        _config = config;
        _log = log;
        InitSchema();
    }

    private void InitSchema()
    {
        const string ddl = """
            CREATE TABLE IF NOT EXISTS outbox_items (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                item_id         TEXT NOT NULL UNIQUE,
                type            TEXT NOT NULL,
                payload         TEXT NOT NULL,
                created_at      TEXT NOT NULL,
                priority        INTEGER NOT NULL DEFAULT 1,
                status          TEXT NOT NULL DEFAULT 'Pending',
                retry_count     INTEGER NOT NULL DEFAULT 0,
                last_error      TEXT,
                session_id      TEXT,
                correlation_id  TEXT,
                synced_at       TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_outbox_status ON outbox_items(status, created_at);
            CREATE INDEX IF NOT EXISTS ix_outbox_type   ON outbox_items(type, status, created_at);
            CREATE INDEX IF NOT EXISTS ix_outbox_itemid ON outbox_items(item_id);
            CREATE INDEX IF NOT EXISTS idx_outbox_status_synced ON outbox_items(status, synced_at);
            """;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = ddl;
        cmd.ExecuteNonQuery();

        // Add synced_at column on existing DBs (no-op if already present).
        EnsureColumn("synced_at", "TEXT");
    }

    private void EnsureColumn(string columnName, string columnType)
    {
        using var pragma = _conn.CreateCommand();
        pragma.CommandText = "PRAGMA table_info(outbox_items)";
        using var reader = pragma.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                return;
        }
        reader.Close();

        using var alter = _conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE outbox_items ADD COLUMN {columnName} {columnType}";
        alter.ExecuteNonQuery();
    }

    public async Task EnqueueAsync(OutboxItem item, CancellationToken ct = default)
    {
        // Deduplication: skip if item_id already exists (any status)
        if (await ExistsAsync(item.ItemId, ct))
        {
            _log.LogDebug("Outbox deduplication: skip existing item {ItemId}", item.ItemId);
            return;
        }

        // Bounded queue: when synced-count grows above the configured threshold,
        // prune oldest Synced rows so the cap remains bounded. Without an UPDATE
        // in MarkSyncedAsync this prune is a no-op because no row is ever 'Synced'.
        var totalPending = await GetPendingCountAsync(ct);
        var totalSynced = await GetSyncedCountAsync(ct);

        if (totalPending + totalSynced >= _config.MaxQueueSize)
        {
            if (!_config.DropOldestSyncedOnCap)
            {
                _log.LogWarning(
                    "Outbox at cap ({MaxSize}), rejecting new item {ItemId}",
                    _config.MaxQueueSize, item.ItemId);
                return;
            }
            var pruned = await PruneSyncedToCapAsync(
                _config.PruneSyncedKeep, ct);
            _log.LogInformation(
                "Outbox cap reached ({MaxSize}), pruned {PrunedCount} oldest synced items",
                _config.MaxQueueSize, pruned);
        }
        else if (totalSynced > _config.PruneSyncedThreshold)
        {
            // Even below MaxQueueSize, keep the Synced tail bounded so that
            // audit/history tables do not grow without limit.
            var pruned = await PruneSyncedToCapAsync(
                _config.PruneSyncedKeep, ct);
            if (pruned > 0)
            {
                _log.LogDebug(
                    "Outbox Synced tail above threshold ({Threshold}), pruned {PrunedCount} items",
                    _config.PruneSyncedThreshold, pruned);
            }
        }

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO outbox_items
                (item_id, type, payload, created_at, priority, status, retry_count, last_error, session_id, correlation_id)
            VALUES
                ($iid, $type, $payload, $created, $priority, $status, $retries, $err, $sid, $cid)
            """;
        cmd.Parameters.AddWithValue("$iid", item.ItemId);
        cmd.Parameters.AddWithValue("$type", item.Type.ToString());
        cmd.Parameters.AddWithValue("$payload", item.Payload);
        cmd.Parameters.AddWithValue("$created", item.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$priority", (int)item.Priority);
        cmd.Parameters.AddWithValue("$status", item.Status.ToString());
        cmd.Parameters.AddWithValue("$retries", item.RetryCount);
        cmd.Parameters.AddWithValue("$err", (object?)item.LastError ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sid", (object?)item.SessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cid", (object?)item.CorrelationId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<OutboxItem>> GetPendingAsync(CancellationToken ct = default)
    {
        var list = new List<OutboxItem>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, item_id, type, payload, created_at, priority, status, retry_count, last_error, session_id, correlation_id
            FROM outbox_items
            WHERE status = 'Pending'
            ORDER BY priority DESC, created_at ASC
            """;
        using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(ReadItem(r));
        return list;
    }

    public async Task<IReadOnlyList<OutboxItem>> GetPendingByTypeAsync(
        OutboxItemType type, CancellationToken ct = default)
    {
        var list = new List<OutboxItem>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, item_id, type, payload, created_at, priority, status, retry_count, last_error, session_id, correlation_id
            FROM outbox_items
            WHERE type = $type AND status = 'Pending'
            ORDER BY priority DESC, created_at ASC
            """;
        cmd.Parameters.AddWithValue("$type", type.ToString());
        using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(ReadItem(r));
        return list;
    }

    public async Task MarkSyncedAsync(string itemId, CancellationToken ct = default)
    {
        // NOTE: status='Synced' (not DELETE) so that PruneSyncedToCapAsync and
        // GetSyncedCountAsync can see the row and apply the bounded-queue policy.
        // The row is later pruned by PruneSyncedToCapAsync once the synced tail
        // exceeds PruneSyncedKeep, or by DeleteOlderThanAsync after TTL.
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            UPDATE outbox_items
            SET status = 'Synced', synced_at = $now
            WHERE item_id = $iid
            """;
        cmd.Parameters.AddWithValue("$iid", itemId);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkFailedAsync(string itemId, string error, CancellationToken ct = default)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            UPDATE outbox_items
            SET status = 'Failed', last_error = $err
            WHERE item_id = $iid
            """;
        cmd.Parameters.AddWithValue("$iid", itemId);
        cmd.Parameters.AddWithValue("$err", error);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task IncrementRetryAsync(string itemId, string? error = null, CancellationToken ct = default)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            UPDATE outbox_items
            SET retry_count = retry_count + 1, last_error = COALESCE($err, last_error)
            WHERE item_id = $iid
            """;
        cmd.Parameters.AddWithValue("$iid", itemId);
        cmd.Parameters.AddWithValue("$err", (object?)error ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> GetPendingCountAsync(CancellationToken ct = default)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM outbox_items WHERE status = 'Pending'";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0);
    }

    public async Task<int> GetPendingCountByTypeAsync(OutboxItemType type, CancellationToken ct = default)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM outbox_items WHERE status = 'Pending' AND type = $type";
        cmd.Parameters.AddWithValue("$type", type.ToString());
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0);
    }

    public async Task<bool> ExistsAsync(string itemId, CancellationToken ct = default)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM outbox_items WHERE item_id = $iid LIMIT 1";
        cmd.Parameters.AddWithValue("$iid", itemId);
        return await cmd.ExecuteScalarAsync(ct) != null;
    }

    public async Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
    {
        // TTL-based retention: only drop rows that are no longer in-flight.
        // Pending items must NOT be deleted by TTL — that would silently drop
        // data the user/agent is still trying to sync.
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM outbox_items
            WHERE created_at < $cutoff
              AND status IN ('Synced', 'Failed')
            """;
        cmd.Parameters.AddWithValue("$cutoff", cutoff.ToString("o"));
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> PruneSyncedToCapAsync(int maxSynced, CancellationToken ct = default)
    {
        if (maxSynced < 0) maxSynced = 0;
        // Order by synced_at ASC (NULL first) so we evict the oldest Synced
        // rows first. Falls back to id ASC for rows where synced_at is NULL
        // (e.g. manually-inserted Synced rows in tests).
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM outbox_items
            WHERE id IN (
                SELECT id FROM outbox_items
                WHERE status = 'Synced'
                ORDER BY synced_at ASC, id ASC
                LIMIT (
                    SELECT MAX(0, COUNT(*) - $max) FROM outbox_items WHERE status = 'Synced'
                )
            )
            """;
        cmd.Parameters.AddWithValue("$max", maxSynced);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> GetSyncedCountAsync(CancellationToken ct = default)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM outbox_items WHERE status = 'Synced'";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0);
    }

    private static OutboxItem ReadItem(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        ItemId = r.GetString(1),
        Type = Enum.Parse<OutboxItemType>(r.GetString(2)),
        Payload = r.GetString(3),
        CreatedAt = DateTime.Parse(r.GetString(4)),
        Priority = (OutboxItemPriority)r.GetInt32(5),
        Status = Enum.Parse<OutboxItemStatus>(r.GetString(6)),
        RetryCount = r.GetInt32(7),
        LastError = r.IsDBNull(8) ? null : r.GetString(8),
        SessionId = r.IsDBNull(9) ? null : r.GetString(9),
        CorrelationId = r.IsDBNull(10) ? null : r.GetString(10)
    };
}
