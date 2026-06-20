using System.Text.Json;
using HerculesBus.Core;
using Microsoft.Data.Sqlite;

namespace HerculesBus.Sqlite;

/// <summary>
///     SQLite-backed IAgentRegistry. Persistent LastSeen/Status между перезапусками.
///     Schema делит с SqliteChannelStore (таблица bus_agents).
/// </summary>
public sealed class SqliteAgentRegistry : IAgentRegistry, IAsyncDisposable
{
    private readonly SqliteConnection _conn;
    private readonly bool _weOwnConn;

    public SqliteAgentRegistry(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string required", nameof(connectionString));
        _conn = new SqliteConnection(connectionString);
        _conn.Open();
        _weOwnConn = true;
        SqliteSchema.EnsureCreated(_conn);
    }

    public SqliteAgentRegistry(SqliteConnection sharedConnection)
    {
        _conn = sharedConnection ?? throw new ArgumentNullException(nameof(sharedConnection));
        _weOwnConn = false;
        if (_conn.State != System.Data.ConnectionState.Open) _conn.Open();
        SqliteSchema.EnsureCreated(_conn);
    }

    public async Task<AgentRegistrationResult> RegisterAsync(AgentIdentity identity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var now = DateTimeOffset.UtcNow;

        // Check existing
        var existing = await GetAsync(identity.AgentId, ct);
        var isNew = existing == null;

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO bus_agents (agent_id, display_name, roles_json, status, registered_at, last_seen, subscribed_channels)
VALUES ($agent_id, $display_name, $roles_json, $status, $registered_at, $last_seen, $subscribed_channels)
ON CONFLICT(agent_id) DO UPDATE SET
  display_name = excluded.display_name,
  roles_json = excluded.roles_json,
  subscribed_channels = excluded.subscribed_channels,
  last_seen = excluded.last_seen,
  status = excluded.status";
        cmd.Parameters.AddWithValue("$agent_id", identity.AgentId);
        cmd.Parameters.AddWithValue("$display_name", identity.DisplayName);
        cmd.Parameters.AddWithValue("$roles_json", JsonSerializer.Serialize(identity.Roles));
        cmd.Parameters.AddWithValue("$status", AgentStatus.Online.ToString());
        cmd.Parameters.AddWithValue("$registered_at", (existing?.RegisteredAt ?? now).ToString("O"));
        cmd.Parameters.AddWithValue("$last_seen", now.ToString("O"));
        cmd.Parameters.AddWithValue("$subscribed_channels",
            (object?)(identity.SubscribedChannels is { Count: > 0 } ? string.Join(",", identity.SubscribedChannels) : null) ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);

        var info = new AgentInfo(
            AgentId: identity.AgentId,
            DisplayName: identity.DisplayName,
            Roles: identity.Roles,
            Status: AgentStatus.Online,
            RegisteredAt: existing?.RegisteredAt ?? now,
            LastSeen: now,
            SubscribedChannels: identity.SubscribedChannels);

        return new AgentRegistrationResult(isNew, info);
    }

    public async Task HeartbeatAsync(string agentId, AgentStatus status, CancellationToken ct = default)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
UPDATE bus_agents SET status = $status, last_seen = $last_seen WHERE agent_id = $agent_id";
        cmd.Parameters.AddWithValue("$status", status.ToString());
        cmd.Parameters.AddWithValue("$last_seen", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$agent_id", agentId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<AgentInfo>> ListAsync(bool includeOffline = true, CancellationToken ct = default)
    {
        var result = new List<AgentInfo>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
SELECT agent_id, display_name, roles_json, status, registered_at, last_seen, subscribed_channels
FROM bus_agents ORDER BY agent_id";
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(ReadAgent(reader));
        }
        return includeOffline ? result : result.Where(a => a.IsOnline).ToList();
    }

    public async Task<AgentInfo?> GetAsync(string agentId, CancellationToken ct = default)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
SELECT agent_id, display_name, roles_json, status, registered_at, last_seen, subscribed_channels
FROM bus_agents WHERE agent_id = $agent_id";
        cmd.Parameters.AddWithValue("$agent_id", agentId);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct)) return ReadAgent(reader);
        return null;
    }

    private static AgentInfo ReadAgent(SqliteDataReader r)
    {
        var rolesJson = r.GetString(2);
        var roles = JsonSerializer.Deserialize<List<string>>(rolesJson) ?? new List<string>();
        var subsStr = r.IsDBNull(6) ? null : r.GetString(6);
        var subs = subsStr?.Split(',', StringSplitOptions.RemoveEmptyEntries);

        return new AgentInfo(
            AgentId: r.GetString(0),
            DisplayName: r.GetString(1),
            Roles: roles,
            Status: Enum.Parse<AgentStatus>(r.GetString(3)),
            RegisteredAt: DateTimeOffset.Parse(r.GetString(4)),
            LastSeen: DateTimeOffset.Parse(r.GetString(5)),
            SubscribedChannels: subs);
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
