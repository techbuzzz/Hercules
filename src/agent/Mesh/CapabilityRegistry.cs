using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Hercules.Mesh;

/// <summary>
///     Локальный реестр известных агентов и их capabilities.
///     Хранится в SQLite (mesh_registry.db). Позволяет агенту находить peer'ов
///     для маршрутизации intent'ов: "какой агент умеет csharp-refactor?".
///     Спецификация: docs/ROADMAP-RU.md Phase 3 #13.
/// </summary>
public sealed class CapabilityRegistry : IDisposable
{
    private readonly SqliteConnection _conn;

    public CapabilityRegistry(string dbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        InitSchema();
    }

    public void Dispose()
    {
        _conn.Dispose();
    }

    private void InitSchema()
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS mesh_agents (
                agent_id      TEXT PRIMARY KEY NOT NULL,
                display_name  TEXT NOT NULL,
                description   TEXT NOT NULL DEFAULT '',
                endpoint      TEXT NOT NULL DEFAULT '',
                transport     TEXT NOT NULL DEFAULT 'http',
                auth_type     TEXT NOT NULL DEFAULT 'apikey',
                auth_header   TEXT,
                primary_model TEXT NOT NULL DEFAULT '',
                fallback_json TEXT NOT NULL DEFAULT '[]',
                health        TEXT NOT NULL DEFAULT '',
                tags_json     TEXT,
                manifest_json TEXT NOT NULL,
                registered_at TEXT NOT NULL,
                last_seen     TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS mesh_capabilities (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                agent_id        TEXT NOT NULL,
                capability_name TEXT NOT NULL,
                description     TEXT NOT NULL DEFAULT '',
                phrase_receivers TEXT NOT NULL DEFAULT '[]',
                tools_json      TEXT,
                FOREIGN KEY (agent_id) REFERENCES mesh_agents(agent_id) ON DELETE CASCADE,
                UNIQUE (agent_id, capability_name)
            );

            CREATE INDEX IF NOT EXISTS idx_caps_agent ON mesh_capabilities(agent_id);
            CREATE INDEX IF NOT EXISTS idx_caps_name ON mesh_capabilities(capability_name);
            """;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    ///     Зарегистрировать или обновить агента в реестре.
    ///     Заменяет все данные агента (включая capabilities).
    /// </summary>
    public void Register(AgentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.AgentId);

        var now = DateTimeOffset.UtcNow.ToString("o");
        var manifestJson = JsonSerializer.Serialize(manifest, JsonOpts);

        using var tx = _conn.BeginTransaction();
        try
        {
            // Upsert агента
            using (var cmd = _conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO mesh_agents
                        (agent_id, display_name, description, endpoint, transport, auth_type, auth_header,
                         primary_model, fallback_json, health, tags_json, manifest_json, registered_at, last_seen)
                    VALUES
                        ($id, $name, $desc, $ep, $tr, $at, $ah, $pm, $fb, $hl, $tg, $mj, $now, $now)
                    ON CONFLICT(agent_id) DO UPDATE SET
                        display_name = $name, description = $desc, endpoint = $ep, transport = $tr,
                        auth_type = $at, auth_header = $ah, primary_model = $pm, fallback_json = $fb,
                        health = $hl, tags_json = $tg, manifest_json = $mj, last_seen = $now
                    """;
                AddParams(cmd, manifest, now, manifestJson);
                cmd.ExecuteNonQuery();
            }

            // Удаляем старые capabilities (если агент уже был)
            using (var del = _conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM mesh_capabilities WHERE agent_id = $id";
                del.Parameters.AddWithValue("$id", manifest.AgentId);
                del.ExecuteNonQuery();
            }

            // Вставляем новые capabilities
            foreach (var cap in manifest.Capabilities)
            {
                using var capCmd = _conn.CreateCommand();
                capCmd.Transaction = tx;
                capCmd.CommandText = """
                    INSERT INTO mesh_capabilities
                        (agent_id, capability_name, description, phrase_receivers, tools_json)
                    VALUES
                        ($aid, $cn, $cd, $pr, $tj)
                    """;
                capCmd.Parameters.AddWithValue("$aid", manifest.AgentId);
                capCmd.Parameters.AddWithValue("$cn", cap.Name);
                capCmd.Parameters.AddWithValue("$cd", cap.Description);
                capCmd.Parameters.AddWithValue("$pr", JsonSerializer.Serialize(cap.PhraseReceivers));
                capCmd.Parameters.AddWithValue("$tj", cap.Tools is not null ? JsonSerializer.Serialize(cap.Tools) : DBNull.Value);
                capCmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>Обновить last_seen агента (heartbeat).</summary>
    public void Touch(string agentId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE mesh_agents SET last_seen = $now WHERE agent_id = $id";
        cmd.Parameters.AddWithValue("$id", agentId);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Удалить агента из реестра.</summary>
    public bool Remove(string agentId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM mesh_agents WHERE agent_id = $id";
        cmd.Parameters.AddWithValue("$id", agentId);
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>Получить манифест агента по ID.</summary>
    public AgentManifest? Get(string agentId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT manifest_json FROM mesh_agents WHERE agent_id = $id";
        cmd.Parameters.AddWithValue("$id", agentId);
        var result = cmd.ExecuteScalar();
        if (result is null || result == DBNull.Value) return null;
        return JsonSerializer.Deserialize<AgentManifest>((string)result, JsonOpts);
    }

    /// <summary>Список всех известных агентов.</summary>
    public List<RegistryAgentEntry> ListAgents()
    {
        var list = new List<RegistryAgentEntry>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT agent_id, display_name, description, endpoint, last_seen
            FROM mesh_agents
            ORDER BY agent_id
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new RegistryAgentEntry(
                AgentId: r.GetString(0),
                DisplayName: r.GetString(1),
                Description: r.IsDBNull(2) ? "" : r.GetString(2),
                Endpoint: r.IsDBNull(3) ? "" : r.GetString(3),
                LastSeen: r.IsDBNull(4) ? "" : r.GetString(4)));
        }
        return list;
    }

    /// <summary>
    ///     Найти агентов, которые имеют capability с указанным именем.
    /// </summary>
    public List<RegistryAgentEntry> FindByCapability(string capabilityName)
    {
        var list = new List<RegistryAgentEntry>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT a.agent_id, a.display_name, a.description, a.endpoint, a.last_seen
            FROM mesh_agents a
            JOIN mesh_capabilities c ON c.agent_id = a.agent_id
            WHERE c.capability_name = $cap
            ORDER BY a.agent_id
            """;
        cmd.Parameters.AddWithValue("$cap", capabilityName);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new RegistryAgentEntry(
                AgentId: r.GetString(0),
                DisplayName: r.GetString(1),
                Description: r.IsDBNull(2) ? "" : r.GetString(2),
                Endpoint: r.IsDBNull(3) ? "" : r.GetString(3),
                LastSeen: r.IsDBNull(4) ? "" : r.GetString(4)));
        }
        return list;
    }

    /// <summary>
    ///     Найти агентов по фразе-приёмнику (semantic lookup).
    ///     Возвращает агентов, у которых есть capability с совпадающим phrase_receiver.
    /// </summary>
    public List<RegistryAgentEntry> FindByPhrase(string phrase)
    {
        var normalized = phrase.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized)) return new();

        var list = new List<RegistryAgentEntry>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT a.agent_id, a.display_name, a.description, a.endpoint, a.last_seen
            FROM mesh_agents a
            JOIN mesh_capabilities c ON c.agent_id = a.agent_id
            WHERE LOWER(c.phrase_receivers) LIKE $phrase
            ORDER BY a.agent_id
            """;
        cmd.Parameters.AddWithValue("$phrase", $"%{normalized}%");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new RegistryAgentEntry(
                AgentId: r.GetString(0),
                DisplayName: r.GetString(1),
                Description: r.IsDBNull(2) ? "" : r.GetString(2),
                Endpoint: r.IsDBNull(3) ? "" : r.GetString(3),
                LastSeen: r.IsDBNull(4) ? "" : r.GetString(4)));
        }
        return list;
    }

    /// <summary>Список capabilities указанного агента.</summary>
    public List<RegistryCapabilityEntry> ListCapabilities(string agentId)
    {
        var list = new List<RegistryCapabilityEntry>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT capability_name, description, phrase_receivers
            FROM mesh_capabilities
            WHERE agent_id = $id
            ORDER BY capability_name
            """;
        cmd.Parameters.AddWithValue("$id", agentId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var receivers = JsonSerializer.Deserialize<List<string>>(r.GetString(2)) ?? new();
            list.Add(new RegistryCapabilityEntry(
                r.GetString(0),
                r.IsDBNull(1) ? "" : r.GetString(1),
                receivers));
        }
        return list;
    }

    private static void AddParams(SqliteCommand cmd, AgentManifest m, string now, string manifestJson)
    {
        cmd.Parameters.AddWithValue("$id", m.AgentId);
        cmd.Parameters.AddWithValue("$name", m.DisplayName);
        cmd.Parameters.AddWithValue("$desc", m.Description);
        cmd.Parameters.AddWithValue("$ep", m.Endpoint);
        cmd.Parameters.AddWithValue("$tr", m.Transport);
        cmd.Parameters.AddWithValue("$at", m.Auth.Type);
        cmd.Parameters.AddWithValue("$ah", (object?)m.Auth.Header ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pm", m.Models.Primary);
        cmd.Parameters.AddWithValue("$fb", JsonSerializer.Serialize(m.Models.Fallback));
        cmd.Parameters.AddWithValue("$hl", m.Health);
        cmd.Parameters.AddWithValue("$tg", m.Tags is not null ? JsonSerializer.Serialize(m.Tags) : DBNull.Value);
        cmd.Parameters.AddWithValue("$mj", manifestJson);
        cmd.Parameters.AddWithValue("$now", now);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
}

/// <summary>Краткая запись об агенте в реестре.</summary>
public sealed record RegistryAgentEntry(
    string AgentId,
    string DisplayName,
    string Description,
    string Endpoint,
    string LastSeen);

/// <summary>Capability в реестре.</summary>
public sealed record RegistryCapabilityEntry(
    string Name,
    string Description,
    IReadOnlyList<string> PhraseReceivers);