using System.Text.Json;
using HerculesBus.Core;
using Microsoft.Data.Sqlite;

namespace HerculesBus.Sqlite;

/// <summary>
///     SQLite-backed IChannelStore. Persistent между перезапусками процесса.
///     Один SqliteConnection — single-threaded (но SQLite WAL mode позволяет
///     concurrent reads с single writer). Для multi-process — V3.2 dedicated server.
///     JsonSerializer используется для serializing attachments/messages.
/// </summary>
public sealed class SqliteChannelStore : IChannelStore, IAsyncDisposable
{
    private readonly SqliteConnection _conn;
    private readonly bool _weOwnConn;
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    ///     Создать store с собственным connection. <paramref name="connectionString"/>
    ///     должен быть валидным SQLite connection string (e.g. "Data Source=hercules-bus.db").
    /// </summary>
    public SqliteChannelStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string required", nameof(connectionString));
        _conn = new SqliteConnection(connectionString);
        _conn.Open();
        _weOwnConn = true;
        SqliteSchema.EnsureCreated(_conn);
    }

    /// <summary>
    ///     Использовать существующий connection (для тестов с shared DB или
    ///     когда connection lifecycle управляется извне).
    /// </summary>
    public SqliteChannelStore(SqliteConnection sharedConnection)
    {
        _conn = sharedConnection ?? throw new ArgumentNullException(nameof(sharedConnection));
        _weOwnConn = false;
        if (_conn.State != System.Data.ConnectionState.Open) _conn.Open();
        SqliteSchema.EnsureCreated(_conn);
    }

    public async Task<BusChannel> EnsureChannelAsync(string name, string description, bool isPrivate, string createdBy, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // SELECT existing
        using (var select = _conn.CreateCommand())
        {
            select.CommandText = "SELECT name, description, is_private, created_at, created_by FROM bus_channels WHERE name = $name";
            select.Parameters.AddWithValue("$name", name);
            using var reader = await select.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                return new BusChannel(
                    Name: reader.GetString(0),
                    Description: reader.GetString(1),
                    IsPrivate: reader.GetInt32(2) != 0,
                    CreatedAt: DateTimeOffset.Parse(reader.GetString(3)),
                    CreatedBy: reader.GetString(4));
            }
        }

        // INSERT new
        var now = DateTimeOffset.UtcNow;
        var channel = new BusChannel(name, description, isPrivate, now, createdBy);
        using var insert = _conn.CreateCommand();
        insert.CommandText = @"
INSERT OR IGNORE INTO bus_channels (name, description, is_private, created_at, created_by)
VALUES ($name, $description, $is_private, $created_at, $created_by)";
        insert.Parameters.AddWithValue("$name", channel.Name);
        insert.Parameters.AddWithValue("$description", channel.Description);
        insert.Parameters.AddWithValue("$is_private", channel.IsPrivate ? 1 : 0);
        insert.Parameters.AddWithValue("$created_at", channel.CreatedAt.ToString("O"));
        insert.Parameters.AddWithValue("$created_by", channel.CreatedBy);
        await insert.ExecuteNonQueryAsync(ct);

        return channel;
    }

    public async Task<BusChannel?> GetChannelAsync(string name, CancellationToken ct = default)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT name, description, is_private, created_at, created_by FROM bus_channels WHERE name = $name";
        cmd.Parameters.AddWithValue("$name", name);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new BusChannel(
                Name: reader.GetString(0),
                Description: reader.GetString(1),
                IsPrivate: reader.GetInt32(2) != 0,
                CreatedAt: DateTimeOffset.Parse(reader.GetString(3)),
                CreatedBy: reader.GetString(4));
        }
        return null;
    }

    public async Task<IReadOnlyList<BusChannel>> ListChannelsAsync(CancellationToken ct = default)
    {
        var result = new List<BusChannel>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT name, description, is_private, created_at, created_by FROM bus_channels ORDER BY name";
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new BusChannel(
                Name: reader.GetString(0),
                Description: reader.GetString(1),
                IsPrivate: reader.GetInt32(2) != 0,
                CreatedAt: DateTimeOffset.Parse(reader.GetString(3)),
                CreatedBy: reader.GetString(4)));
        }
        return result;
    }

    public async Task<AgentMessage> AppendMessageAsync(AgentMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var id = string.IsNullOrEmpty(message.Id) ? Ulid.NewId() : message.Id;
        var ts = message.Timestamp ?? DateTimeOffset.UtcNow;
        var withMeta = message with { Id = id, Timestamp = ts };

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
INSERT OR REPLACE INTO bus_messages
  (id, channel, sender_agent_id, sender_name, kind, body, reply_to, mentions, attachments_json, timestamp)
VALUES
  ($id, $channel, $sender_agent_id, $sender_name, $kind, $body, $reply_to, $mentions, $attachments_json, $timestamp)";
        cmd.Parameters.AddWithValue("$id", withMeta.Id);
        cmd.Parameters.AddWithValue("$channel", withMeta.Channel);
        cmd.Parameters.AddWithValue("$sender_agent_id", withMeta.SenderAgentId);
        cmd.Parameters.AddWithValue("$sender_name", withMeta.SenderName);
        cmd.Parameters.AddWithValue("$kind", withMeta.Kind);
        cmd.Parameters.AddWithValue("$body", withMeta.Body);
        cmd.Parameters.AddWithValue("$reply_to", (object?)withMeta.ReplyTo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$mentions", (object?)(withMeta.Mentions is { Count: > 0 }
            ? string.Join(",", withMeta.Mentions) : null) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$attachments_json", (object?)(withMeta.Attachments is { Count: > 0 }
            ? JsonSerializer.Serialize(withMeta.Attachments, _jsonOpts) : null) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$timestamp", ts.ToString("O"));

        await cmd.ExecuteNonQueryAsync(ct);
        return withMeta;
    }

    public async Task<IReadOnlyList<AgentMessage>> GetRecentMessagesAsync(string channel, int limit = 50, string? beforeId = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        if (limit <= 0) limit = 50;

        var result = new List<AgentMessage>();
        using var cmd = _conn.CreateCommand();

        if (beforeId != null)
        {
            // Получаем timestamp beforeId, фильтруем по нему
            cmd.CommandText = @"
SELECT id FROM bus_messages WHERE id = $before_id";
            cmd.Parameters.AddWithValue("$before_id", beforeId);
            var beforeTs = await cmd.ExecuteScalarAsync(ct) as string;

            cmd.Parameters.Clear();
            cmd.CommandText = @"
SELECT id, channel, sender_agent_id, sender_name, kind, body, reply_to, mentions, attachments_json, timestamp
FROM bus_messages
WHERE channel = $channel AND ($before_ts IS NULL OR timestamp < $before_ts)
ORDER BY timestamp DESC
LIMIT $limit";
            cmd.Parameters.AddWithValue("$channel", channel);
            cmd.Parameters.AddWithValue("$before_ts", (object?)beforeTs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$limit", limit);
        }
        else
        {
            cmd.CommandText = @"
SELECT id, channel, sender_agent_id, sender_name, kind, body, reply_to, mentions, attachments_json, timestamp
FROM bus_messages
WHERE channel = $channel
ORDER BY timestamp DESC
LIMIT $limit";
            cmd.Parameters.AddWithValue("$channel", channel);
            cmd.Parameters.AddWithValue("$limit", limit);
        }

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(ReadMessage(reader));
        }
        result.Reverse(); // chronological order
        return result;
    }

    public async Task<AgentMessage?> GetMessageAsync(string id, CancellationToken ct = default)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
SELECT id, channel, sender_agent_id, sender_name, kind, body, reply_to, mentions, attachments_json, timestamp
FROM bus_messages WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct)) return ReadMessage(reader);
        return null;
    }

    public async Task<IReadOnlyList<AgentMessage>> GetThreadAsync(string messageId, CancellationToken ct = default)
    {
        var result = new List<AgentMessage>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
SELECT id, channel, sender_agent_id, sender_name, kind, body, reply_to, mentions, attachments_json, timestamp
FROM bus_messages
WHERE reply_to = $reply_to
ORDER BY timestamp ASC";
        cmd.Parameters.AddWithValue("$reply_to", messageId);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(ReadMessage(reader));
        }
        return result;
    }

    private static AgentMessage ReadMessage(SqliteDataReader r)
    {
        var attachmentsJson = r.IsDBNull(8) ? null : r.GetString(8);
        var attachments = attachmentsJson != null
            ? JsonSerializer.Deserialize<List<MessageAttachment>>(attachmentsJson, _jsonOpts)
            : null;
        var mentions = r.IsDBNull(7) ? null : r.GetString(7)?.Split(',', StringSplitOptions.RemoveEmptyEntries);

        return new AgentMessage(
            Id: r.GetString(0),
            Channel: r.GetString(1),
            SenderAgentId: r.GetString(2),
            SenderName: r.GetString(3),
            Kind: r.GetString(4),
            Body: r.GetString(5),
            ReplyTo: r.IsDBNull(6) ? null : r.GetString(6),
            Mentions: mentions,
            Attachments: attachments,
            Timestamp: DateTimeOffset.Parse(r.GetString(9)));
    }

    public ValueTask DisposeAsync()
    {
        if (_weOwnConn)
        {
            try { _conn.Close(); _conn.Dispose(); } catch { }
        }
        return ValueTask.CompletedTask;
    }
}
