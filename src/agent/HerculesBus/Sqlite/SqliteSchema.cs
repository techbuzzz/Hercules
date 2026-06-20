using Microsoft.Data.Sqlite;

namespace HerculesBus.Sqlite;

/// <summary>
///     Schema для SQLite-backed IChannelStore и IAgentRegistry.
///     Один connection string — один DB. Multi-process не поддерживается (single-writer SQLite).
///     V3.2: WAL mode + connection pool для concurrent reads.
/// </summary>
public static class SqliteSchema
{
    private const int SchemaVersion = 1;

    public static void EnsureCreated(SqliteConnection conn)
    {
        ArgumentNullException.ThrowIfNull(conn);
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var pragma = conn.CreateCommand();
        pragma.CommandText = @"
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA foreign_keys = ON;
";
        pragma.ExecuteNonQuery();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER PRIMARY KEY
);

CREATE TABLE IF NOT EXISTS bus_channels (
    name        TEXT PRIMARY KEY NOT NULL,
    description TEXT NOT NULL DEFAULT '',
    is_private  INTEGER NOT NULL DEFAULT 0,
    created_at  TEXT NOT NULL,
    created_by  TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS bus_messages (
    id                TEXT PRIMARY KEY NOT NULL,
    channel           TEXT NOT NULL,
    sender_agent_id   TEXT NOT NULL,
    sender_name       TEXT NOT NULL,
    kind              TEXT NOT NULL,
    body              TEXT NOT NULL,
    reply_to          TEXT,
    mentions          TEXT,
    attachments_json  TEXT,
    timestamp         TEXT NOT NULL,
    FOREIGN KEY (channel) REFERENCES bus_channels(name) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_messages_channel_time ON bus_messages(channel, timestamp);
CREATE INDEX IF NOT EXISTS idx_messages_reply_to ON bus_messages(reply_to);

CREATE TABLE IF NOT EXISTS bus_agents (
    agent_id            TEXT PRIMARY KEY NOT NULL,
    display_name        TEXT NOT NULL,
    roles_json          TEXT NOT NULL,
    status              TEXT NOT NULL,
    registered_at       TEXT NOT NULL,
    last_seen           TEXT NOT NULL,
    subscribed_channels TEXT
);

INSERT OR IGNORE INTO schema_version (version) VALUES ({SchemaVersion});
";
        cmd.ExecuteNonQuery();
    }
}
