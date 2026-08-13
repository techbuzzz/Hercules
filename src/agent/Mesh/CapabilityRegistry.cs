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
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

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
        // Migrate v1 → v2: add TTL, health status, trust level, cost/latency hints
        MigrateSchemaV2();

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
                               last_seen     TEXT NOT NULL,
                               expiry_seconds INTEGER NOT NULL DEFAULT 86400,
                               health_status TEXT NOT NULL DEFAULT 'unknown',
                               last_health_check TEXT NOT NULL DEFAULT '',
                               consecutive_failures INTEGER NOT NULL DEFAULT 0,
                               trust_level TEXT NOT NULL DEFAULT 'unverified',
                               cost_hint_usd REAL NOT NULL DEFAULT 0,
                               latency_hint_ms INTEGER NOT NULL DEFAULT 0,
                               supported_protocol_versions_json TEXT NOT NULL DEFAULT '["1.0"]'
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
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void MigrateSchemaV2()
    {
        // Add new columns only if they don't exist (backward-compat with v1 schema)
        string[] newCols = {
            "expiry_seconds INTEGER NOT NULL DEFAULT 86400",
            "health_status TEXT NOT NULL DEFAULT 'unknown'",
            "last_health_check TEXT NOT NULL DEFAULT ''",
            "consecutive_failures INTEGER NOT NULL DEFAULT 0",
            "trust_level TEXT NOT NULL DEFAULT 'unverified'",
            "cost_hint_usd REAL NOT NULL DEFAULT 0",
            "latency_hint_ms INTEGER NOT NULL DEFAULT 0",
            "supported_protocol_versions_json TEXT NOT NULL DEFAULT '[\"1.0\"]'"
        };

        foreach (string colDef in newCols)
        {
            string colName = colDef.Split(' ')[0];
            try
            {
                using SqliteCommand cmd = _conn.CreateCommand();
                cmd.CommandText = $"ALTER TABLE mesh_agents ADD COLUMN {colDef}";
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException)
            {
                // Column already exists — ignore
            }
        }
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

        using SqliteTransaction tx = _conn.BeginTransaction();
        try
        {
            // Upsert агента
            using (SqliteCommand cmd = _conn.CreateCommand())
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
            using (SqliteCommand del = _conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM mesh_capabilities WHERE agent_id = $id";
                del.Parameters.AddWithValue("$id", manifest.AgentId);
                del.ExecuteNonQuery();
            }

            // Вставляем новые capabilities
            foreach (ManifestCapability cap in manifest.Capabilities)
            {
                using SqliteCommand capCmd = _conn.CreateCommand();
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
                capCmd.Parameters.AddWithValue("$tj", cap.Tools is not null
                    ? JsonSerializer.Serialize(cap.Tools)
                    : DBNull.Value);
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
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE mesh_agents SET last_seen = $now WHERE agent_id = $id";
        cmd.Parameters.AddWithValue("$id", agentId);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Удалить агента из реестра.</summary>
    public bool Remove(string agentId)
    {
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM mesh_agents WHERE agent_id = $id";
        cmd.Parameters.AddWithValue("$id", agentId);
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>Получить манифест агента по ID.</summary>
    public AgentManifest? Get(string agentId)
    {
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT manifest_json FROM mesh_agents WHERE agent_id = $id";
        cmd.Parameters.AddWithValue("$id", agentId);
        var result = cmd.ExecuteScalar();
        if (result is null || result == DBNull.Value)
        {
            return null;
        }

        return JsonSerializer.Deserialize<AgentManifest>((string)result, JsonOpts);
    }

    /// <summary>Список всех известных агентов.</summary>
    public List<RegistryAgentEntry> ListAgents()
    {
        var list = new List<RegistryAgentEntry>();
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = """
                          SELECT agent_id, display_name, description, endpoint, last_seen
                          FROM mesh_agents
                          ORDER BY agent_id
                          """;
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new RegistryAgentEntry(
                r.GetString(0),
                r.GetString(1),
                r.IsDBNull(2)
                    ? ""
                    : r.GetString(2),
                r.IsDBNull(3)
                    ? ""
                    : r.GetString(3),
                r.IsDBNull(4)
                    ? ""
                    : r.GetString(4)));
        }

        return list;
    }

    /// <summary>
    ///     Найти агентов, которые имеют capability с указанным именем.
    /// </summary>
    public List<RegistryAgentEntry> FindByCapability(string capabilityName)
    {
        var list = new List<RegistryAgentEntry>();
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = """
                          SELECT a.agent_id, a.display_name, a.description, a.endpoint, a.last_seen
                          FROM mesh_agents a
                          JOIN mesh_capabilities c ON c.agent_id = a.agent_id
                          WHERE c.capability_name = $cap
                          ORDER BY a.agent_id
                          """;
        cmd.Parameters.AddWithValue("$cap", capabilityName);
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new RegistryAgentEntry(
                r.GetString(0),
                r.GetString(1),
                r.IsDBNull(2)
                    ? ""
                    : r.GetString(2),
                r.IsDBNull(3)
                    ? ""
                    : r.GetString(3),
                r.IsDBNull(4)
                    ? ""
                    : r.GetString(4)));
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
        if (string.IsNullOrEmpty(normalized))
        {
            return new List<RegistryAgentEntry>();
        }

        var list = new List<RegistryAgentEntry>();
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = """
                          SELECT DISTINCT a.agent_id, a.display_name, a.description, a.endpoint, a.last_seen
                          FROM mesh_agents a
                          JOIN mesh_capabilities c ON c.agent_id = a.agent_id
                          WHERE LOWER(c.phrase_receivers) LIKE $phrase
                          ORDER BY a.agent_id
                          """;
        cmd.Parameters.AddWithValue("$phrase", $"%{normalized}%");
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new RegistryAgentEntry(
                r.GetString(0),
                r.GetString(1),
                r.IsDBNull(2)
                    ? ""
                    : r.GetString(2),
                r.IsDBNull(3)
                    ? ""
                    : r.GetString(3),
                r.IsDBNull(4)
                    ? ""
                    : r.GetString(4)));
        }

        return list;
    }

    /// <summary>Список capabilities указанного агента.</summary>
    public List<RegistryCapabilityEntry> ListCapabilities(string agentId)
    {
        var list = new List<RegistryCapabilityEntry>();
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = """
                          SELECT capability_name, description, phrase_receivers
                          FROM mesh_capabilities
                          WHERE agent_id = $id
                          ORDER BY capability_name
                          """;
        cmd.Parameters.AddWithValue("$id", agentId);
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            List<string> receivers = JsonSerializer.Deserialize<List<string>>(r.GetString(2)) ?? new List<string>();
            list.Add(new RegistryCapabilityEntry(
                r.GetString(0),
                r.IsDBNull(1)
                    ? ""
                    : r.GetString(1),
                receivers));
        }

        return list;
    }

    /// <summary>
    ///     Обновить health status агента.
    ///     Также обновляет last_health_check и consecutive_failures.
    /// </summary>
    public void UpdateHealthStatus(string agentId, AgentHealthStatus status, string? error = null, int? consecutiveFailures = null)
    {
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = """
                          UPDATE mesh_agents
                          SET health_status = $hs,
                              last_health_check = $lhc,
                              consecutive_failures = $cf
                          WHERE agent_id = $id
                          """;
        cmd.Parameters.AddWithValue("$id", agentId);
        cmd.Parameters.AddWithValue("$hs", status.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("$lhc", DateTimeOffset.UtcNow.ToString("o"));

        int failures;
        if (consecutiveFailures.HasValue)
        {
            failures = consecutiveFailures.Value;
        }
        else if (status == AgentHealthStatus.Healthy)
        {
            failures = 0;
        }
        else
        {
            // Preserve existing failures for Unknown/Unreachable, increment for Unhealthy
            failures = status == AgentHealthStatus.Unhealthy ? 1 : 0;
        }
        cmd.Parameters.AddWithValue("$cf", failures);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    ///     Обновить trust level агента.
    /// </summary>
    public void UpdateTrustLevel(string agentId, string trustLevel)
    {
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE mesh_agents SET trust_level = $tl WHERE agent_id = $id";
        cmd.Parameters.AddWithValue("$id", agentId);
        cmd.Parameters.AddWithValue("$tl", trustLevel);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    ///     Обновить cost hint агента (USD per call).
    /// </summary>
    public void UpdateCostHint(string agentId, decimal costUsd)
    {
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE mesh_agents SET cost_hint_usd = $cost WHERE agent_id = $id";
        cmd.Parameters.AddWithValue("$id", agentId);
        cmd.Parameters.AddWithValue("$cost", costUsd);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    ///     Обновить latency hint агента (ms per call).
    /// </summary>
    public void UpdateLatencyHint(string agentId, int latencyMs)
    {
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE mesh_agents SET latency_hint_ms = $lat WHERE agent_id = $id";
        cmd.Parameters.AddWithValue("$id", agentId);
        cmd.Parameters.AddWithValue("$lat", latencyMs);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    ///     Установить TTL (expiry_seconds) для агента.
    /// </summary>
    public void SetExpiry(string agentId, int expirySeconds)
    {
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE mesh_agents SET expiry_seconds = $exp WHERE agent_id = $id";
        cmd.Parameters.AddWithValue("$id", agentId);
        cmd.Parameters.AddWithValue("$exp", expirySeconds);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    ///     Получить полную запись агента из реестра (включая новые поля).
    /// </summary>
    public RegistryAgentFullEntry? GetFull(string agentId)
    {
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = """
                          SELECT agent_id, display_name, description, endpoint, last_seen,
                                 health_status, last_health_check, consecutive_failures,
                                 trust_level, cost_hint_usd, latency_hint_ms,
                                 expiry_seconds, supported_protocol_versions_json
                          FROM mesh_agents
                          WHERE agent_id = $id
                          """;
        cmd.Parameters.AddWithValue("$id", agentId);
        using SqliteDataReader r = cmd.ExecuteReader();
        if (!r.Read())
        {
            return null;
        }

        return new RegistryAgentFullEntry(
            r.GetString(0),
            r.GetString(1),
            r.IsDBNull(2) ? "" : r.GetString(2),
            r.IsDBNull(3) ? "" : r.GetString(3),
            r.IsDBNull(4) ? "" : r.GetString(4),
            r.IsDBNull(5) ? "unknown" : r.GetString(5),
            r.IsDBNull(6) ? "" : r.GetString(6),
            r.IsDBNull(7) ? 0 : r.GetInt32(7),
            r.IsDBNull(8) ? "unverified" : r.GetString(8),
            r.IsDBNull(9) ? 0m : r.GetDecimal(9),
            r.IsDBNull(10) ? 0 : r.GetInt32(10),
            r.IsDBNull(11) ? 86400 : r.GetInt32(11),
            r.IsDBNull(12) ? "[\"1.0\"]" : r.GetString(12));
    }

    /// <summary>
    ///     Список всех агентов с полной информацией.
    /// </summary>
    public List<RegistryAgentFullEntry> ListAllAgents()
    {
        var list = new List<RegistryAgentFullEntry>();
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = """
                          SELECT agent_id, display_name, description, endpoint, last_seen,
                                 health_status, last_health_check, consecutive_failures,
                                 trust_level, cost_hint_usd, latency_hint_ms,
                                 expiry_seconds, supported_protocol_versions_json
                          FROM mesh_agents
                          ORDER BY agent_id
                          """;
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new RegistryAgentFullEntry(
                r.GetString(0),
                r.GetString(1),
                r.IsDBNull(2) ? "" : r.GetString(2),
                r.IsDBNull(3) ? "" : r.GetString(3),
                r.IsDBNull(4) ? "" : r.GetString(4),
                r.IsDBNull(5) ? "unknown" : r.GetString(5),
                r.IsDBNull(6) ? "" : r.GetString(6),
                r.IsDBNull(7) ? 0 : r.GetInt32(7),
                r.IsDBNull(8) ? "unverified" : r.GetString(8),
                r.IsDBNull(9) ? 0m : r.GetDecimal(9),
                r.IsDBNull(10) ? 0 : r.GetInt32(10),
                r.IsDBNull(11) ? 86400 : r.GetInt32(11),
                r.IsDBNull(12) ? "[\"1.0\"]" : r.GetString(12)));
        }

        return list;
    }

    /// <summary>
    ///     Удалить просроченных агентов (TTL expired).
    ///     Возвращает количество удалённых записей.
    /// </summary>
    public int CleanupExpired()
    {
        var now = DateTimeOffset.UtcNow;
        using SqliteCommand sel = _conn.CreateCommand();
        sel.CommandText = "SELECT agent_id, last_seen, expiry_seconds FROM mesh_agents";
        var toDelete = new List<string>();
        using (SqliteDataReader r = sel.ExecuteReader())
        {
            while (r.Read())
            {
                string lastSeenStr = r.IsDBNull(1) ? "" : r.GetString(1);
                int expirySec = r.IsDBNull(2) ? 86400 : r.GetInt32(2);
                if (string.IsNullOrEmpty(lastSeenStr))
                    continue;
                if (!DateTimeOffset.TryParse(lastSeenStr, out var lastSeen))
                    continue;
                if (now - lastSeen > TimeSpan.FromSeconds(expirySec))
                {
                    toDelete.Add(r.GetString(0));
                }
            }
        }

        int count = 0;
        foreach (string id in toDelete)
        {
            using SqliteCommand del = _conn.CreateCommand();
            del.CommandText = "DELETE FROM mesh_agents WHERE agent_id = $id";
            del.Parameters.AddWithValue("$id", id);
            count += del.ExecuteNonQuery();
        }

        return count;
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
        cmd.Parameters.AddWithValue("$tg", m.Tags is not null
            ? JsonSerializer.Serialize(m.Tags)
            : DBNull.Value);
        cmd.Parameters.AddWithValue("$mj", manifestJson);
        cmd.Parameters.AddWithValue("$now", now);
    }
}

/// <summary>
///     Health status агента в capability registry.
/// </summary>
public enum AgentHealthStatus
{
    Unknown,
    Healthy,
    Unhealthy,
    Unreachable
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

/// <summary>
///     Полная запись об агенте в capability registry (расширенная с TTL, health, trust, cost/latency).
/// </summary>
public sealed record RegistryAgentFullEntry(
    string AgentId,
    string DisplayName,
    string Description,
    string Endpoint,
    string LastSeen,
    string HealthStatus,
    string LastHealthCheck,
    int ConsecutiveFailures,
    string TrustLevel,
    decimal CostHintUsd,
    int LatencyHintMs,
    int ExpirySeconds,
    string SupportedProtocolVersionsJson);
