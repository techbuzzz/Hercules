using Hercules.Config;
using Hercules.Tasks;
using Microsoft.Data.Sqlite;

namespace Hercules.Storage;

/// <summary>
///     Хранилище сессий, логов взаимодействий и метрик в SQLite.
/// </summary>
public sealed class SqliteSessionStore : IAsyncDisposable, IDisposable
{
    private readonly SqliteConnection _conn;
    // task_071: Serialise access to the shared SqliteConnection.
    // Microsoft.Data.Sqlite is not thread-safe for concurrent commands on a
    // single connection — a SemaphoreSlim(1,1) around every command keeps the
    // store safe for concurrent async and sync callers (WAL is already enabled
    // in EnableWalMode, and Cache=Shared keeps the on-disk file coherent).
    private readonly SemaphoreSlim _connLock = new(1, 1);
    // task_071: set after Dispose so post-disposal calls degrade gracefully
    // (e.g. IsHealthy returning false instead of throwing on a disposed lock).
    private int _disposed;

    public SqliteSessionStore(StorageConfig cfg)
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
        InitSchema();
        EnableWalMode();
        InitCheckpointSchemaAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        // task_071: drain in-flight commands before disposing the connection.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _connLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _conn.Dispose();
        }
        finally
        {
            _connLock.Release();
            _connLock.Dispose();
        }
    }

    public void Dispose()
    {
        // task_071: acquire the lock so a concurrent async call cannot observe
        // a disposed connection. We block briefly here; disposal is expected to
        // happen during shutdown when the host has already stopped new work.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _connLock.Wait();
        try
        {
            _conn.Dispose();
        }
        finally
        {
            _connLock.Release();
            _connLock.Dispose();
        }
    }

    /// <summary>Exposes the underlying connection for shared-quality store usage (task_029).</summary>
    public SqliteConnection Connection => _conn;

    public bool IsHealthy()
    {
        // task_071: protect against concurrent commands on the shared connection.
        if (Volatile.Read(ref _disposed) != 0)
            return false;

        if (!_connLock.Wait(0))
            return false; // another op is in progress; treat as "not healthy right now"

        try
        {
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            var result = cmd.ExecuteScalar();
            return result is not null;
        }
        catch
        {
            return false;
        }
        finally
        {
            _connLock.Release();
        }
    }

    private void EnableWalMode()
    {
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
    }

    private void InitSchema()
    {
        const string ddl = """
                           CREATE TABLE IF NOT EXISTS sessions (
                               id          TEXT PRIMARY KEY,
                               started_at  TEXT NOT NULL,
                               ended_at    TEXT
                           );
                           CREATE TABLE IF NOT EXISTS interactions (
                               id          INTEGER PRIMARY KEY AUTOINCREMENT,
                               session_id  TEXT NOT NULL,
                               input       TEXT NOT NULL,
                               output      TEXT NOT NULL,
                               confidence  TEXT NOT NULL,
                               mode        TEXT NOT NULL,
                               skill_id    TEXT,
                               provider    TEXT,
                               created_at  TEXT NOT NULL
                           );
                           CREATE TABLE IF NOT EXISTS request_stats (
                               norm_input  TEXT PRIMARY KEY,
                               count       INTEGER NOT NULL,
                               last_seen   TEXT NOT NULL
                           );
                           CREATE TABLE IF NOT EXISTS sandbox_executions (
                               id              INTEGER PRIMARY KEY AUTOINCREMENT,
                               session_id      TEXT NOT NULL,
                               code_hash       TEXT NOT NULL,
                               language        TEXT NOT NULL,
                               exit_code       INTEGER,
                               status          TEXT NOT NULL,
                               duration_ms     INTEGER,
                               blocked_patterns TEXT,
                               created_at      TEXT NOT NULL
                           );
                           -- task_003: budget tracking
                           CREATE TABLE IF NOT EXISTS budget_entries (
                               id              INTEGER PRIMARY KEY AUTOINCREMENT,
                               session_id      TEXT NOT NULL,
                               provider        TEXT NOT NULL,
                               model           TEXT NOT NULL,
                               input_tokens    INTEGER NOT NULL DEFAULT 0,
                               output_tokens   INTEGER NOT NULL DEFAULT 0,
                               cost_usd        REAL NOT NULL DEFAULT 0,
                               created_at      TEXT NOT NULL
                           );
                           -- task_003: audit log
                           -- task_014: enriched with request_id, tool_name, policy_decision, permission_used, result, payload_hash
                           CREATE TABLE IF NOT EXISTS audit_log (
                               id              INTEGER PRIMARY KEY AUTOINCREMENT,
                               actor           TEXT NOT NULL,
                               action          TEXT NOT NULL,
                               target          TEXT,
                               details         TEXT,
                               session_id      TEXT,
                               created_at      TEXT NOT NULL,
                               request_id      TEXT,
                               tool_name       TEXT,
                               policy_decision TEXT,
                               permission_used TEXT,
                               result          TEXT,
                               payload_hash    TEXT
                           );
                           -- task_003: skill evaluation history
                           CREATE TABLE IF NOT EXISTS skill_evaluations (
                               id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                               skill_id            TEXT NOT NULL,
                               score               REAL NOT NULL,
                               passed              INTEGER NOT NULL,
                               test_results        TEXT,
                               evaluator_provider  TEXT NOT NULL,
                               created_at          TEXT NOT NULL
                           );
                           -- task_003: durable task state
                           CREATE TABLE IF NOT EXISTS task_states (
                               id              INTEGER PRIMARY KEY AUTOINCREMENT,
                               task_id         TEXT NOT NULL UNIQUE,
                               status          TEXT NOT NULL,
                               result          TEXT,
                               error           TEXT,
                               metadata        TEXT,
                               created_at      TEXT NOT NULL,
                               updated_at      TEXT NOT NULL
                           );
                           -- task_010: approval gates
                           CREATE TABLE IF NOT EXISTS approval_requests (
                               id              TEXT PRIMARY KEY,
                               session_id      TEXT NOT NULL,
                               tool_name       TEXT NOT NULL,
                               arguments_json  TEXT NOT NULL,
                               policy_decision TEXT NOT NULL,
                               reason          TEXT NOT NULL,
                               requested_at    TEXT NOT NULL,
                               requested_by    TEXT,
                               status          TEXT NOT NULL DEFAULT 'Pending',
                               approved_at     TEXT,
                               denied_at       TEXT
                           );
                           CREATE INDEX IF NOT EXISTS ix_interactions_session ON interactions(session_id);
                           CREATE INDEX IF NOT EXISTS ix_interactions_created ON interactions(created_at);
                           CREATE INDEX IF NOT EXISTS ix_sandbox_executions_session ON sandbox_executions(session_id);
                           CREATE INDEX IF NOT EXISTS ix_budget_session ON budget_entries(session_id);
                           CREATE INDEX IF NOT EXISTS ix_budget_created ON budget_entries(created_at);
                           CREATE INDEX IF NOT EXISTS ix_audit_created ON audit_log(created_at);
                           CREATE INDEX IF NOT EXISTS ix_audit_target ON audit_log(target);
                           CREATE INDEX IF NOT EXISTS ix_audit_session ON audit_log(session_id);
                           CREATE INDEX IF NOT EXISTS ix_audit_tool ON audit_log(tool_name);
                           CREATE INDEX IF NOT EXISTS ix_eval_skill ON skill_evaluations(skill_id);
                           CREATE INDEX IF NOT EXISTS ix_task_taskid ON task_states(task_id);
                           CREATE INDEX IF NOT EXISTS ix_approval_session ON approval_requests(session_id);
                           CREATE INDEX IF NOT EXISTS ix_approval_status ON approval_requests(status);
                           -- task_049: human-in-the-loop escalation
                           CREATE TABLE IF NOT EXISTS escalations (
                               id              TEXT PRIMARY KEY,
                               request_id      TEXT NOT NULL,
                               session_id      TEXT NOT NULL,
                               agent_id        TEXT NOT NULL,
                               type            TEXT NOT NULL,
                               severity        TEXT NOT NULL,
                               status          TEXT NOT NULL DEFAULT 'Pending',
                               action_plan     TEXT NOT NULL,
                               context         TEXT NOT NULL,
                               payload_json    TEXT,
                               tool_or_intent  TEXT,
                               requested_by    TEXT NOT NULL,
                               created_at      TEXT NOT NULL,
                               resolved_at     TEXT,
                               resolved_by     TEXT
                           );
                           CREATE INDEX IF NOT EXISTS ix_esc_session   ON escalations(session_id);
                           CREATE INDEX IF NOT EXISTS ix_esc_status   ON escalations(status);
                           CREATE INDEX IF NOT EXISTS ix_esc_severity ON escalations(severity);
                           """;
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = ddl;
        cmd.ExecuteNonQuery();

        // task_014: add new columns to existing audit_log table (forward migration)
        var migrations = new[]
        {
            "ALTER TABLE audit_log ADD COLUMN request_id TEXT",
            "ALTER TABLE audit_log ADD COLUMN tool_name TEXT",
            "ALTER TABLE audit_log ADD COLUMN policy_decision TEXT",
            "ALTER TABLE audit_log ADD COLUMN permission_used TEXT",
            "ALTER TABLE audit_log ADD COLUMN result TEXT",
            "ALTER TABLE audit_log ADD COLUMN payload_hash TEXT"
        };
        foreach (var migration in migrations)
        {
            try
            {
                cmd.CommandText = migration;
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException ex) when (ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
            {
                // Column already exists — ignore
            }
        }

        // task_018: extend task_states with durable task fields
        var taskMigrations = new[]
        {
            "ALTER TABLE task_states ADD COLUMN name TEXT",
            "ALTER TABLE task_states ADD COLUMN attempt_count INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE task_states ADD COLUMN current_step INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE task_states ADD COLUMN completed_at TEXT",
            "ALTER TABLE task_states ADD COLUMN cancellation_reason TEXT",
            "ALTER TABLE task_states ADD COLUMN retry_policy TEXT",
            "ALTER TABLE task_states ADD COLUMN skill_id TEXT",
            "ALTER TABLE task_states ADD COLUMN session_id TEXT",
            "ALTER TABLE task_states ADD COLUMN priority TEXT DEFAULT 'normal'",
            "ALTER TABLE task_states ADD COLUMN tags TEXT",
            "ALTER TABLE task_states ADD COLUMN created_by TEXT",
            "ALTER TABLE task_states ADD COLUMN description TEXT",
            "ALTER TABLE task_states ADD COLUMN owner_agent_id TEXT"
        };
        foreach (var m in taskMigrations)
        {
            try
            {
                cmd.CommandText = m;
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException ex) when (ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
            {
                // Column already exists — ignore
            }
        }
    }

    public async Task StartSessionAsync(string sessionId, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO sessions (id, started_at) VALUES ($id, $t)";
            cmd.Parameters.AddWithValue("$id", sessionId);
            cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _connLock.Release();
        }
    }

    public void StartSession(string sessionId)
    {
        StartSessionAsync(sessionId).GetAwaiter().GetResult();
    }

    public async Task EndSessionAsync(string sessionId, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE sessions SET ended_at = $t WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", sessionId);
            cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _connLock.Release();
        }
    }

    public void EndSession(string sessionId)
    {
        EndSessionAsync(sessionId).GetAwaiter().GetResult();
    }

    public async Task LogInteractionAsync(InteractionLog log, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              INSERT INTO interactions (session_id, input, output, confidence, mode, skill_id, provider, created_at)
                              VALUES ($s, $i, $o, $c, $m, $sk, $p, $t)
                              """;
            cmd.Parameters.AddWithValue("$s", log.SessionId);
            cmd.Parameters.AddWithValue("$i", log.Input);
            cmd.Parameters.AddWithValue("$o", log.Output);
            cmd.Parameters.AddWithValue("$c", log.Confidence);
            cmd.Parameters.AddWithValue("$m", log.Mode);
            cmd.Parameters.AddWithValue("$sk", (object?)log.SkillId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$p", log.Provider);
            cmd.Parameters.AddWithValue("$t", log.CreatedAt.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _connLock.Release();
        }
    }

    public void LogInteraction(InteractionLog log)
    {
        LogInteractionAsync(log).GetAwaiter().GetResult();
    }

    /// <summary>
    ///     Увеличить счётчик нормализованного запроса и вернуть текущее количество повторов.
    ///     Используется для авто-предложения создания навыка.
    /// </summary>
    public async Task<int> IncrementRequestCountAsync(string normalizedInput, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection —
        // the two commands below must run in the same critical section so the
        // SELECT observes the row inserted/updated by the previous INSERT.
        await _connLock.WaitAsync(ct);
        try
        {
            using SqliteCommand up = _conn.CreateCommand();
            up.CommandText = """
                             INSERT INTO request_stats (norm_input, count, last_seen)
                             VALUES ($n, 1, $t)
                             ON CONFLICT(norm_input) DO UPDATE SET count = count + 1, last_seen = $t
                             """;
            up.Parameters.AddWithValue("$n", normalizedInput);
            up.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
            await up.ExecuteNonQueryAsync(ct);

            using SqliteCommand sel = _conn.CreateCommand();
            sel.CommandText = "SELECT count FROM request_stats WHERE norm_input = $n";
            sel.Parameters.AddWithValue("$n", normalizedInput);
            return Convert.ToInt32(await sel.ExecuteScalarAsync(ct) ?? 0);
        }
        finally
        {
            _connLock.Release();
        }
    }

    public int IncrementRequestCount(string normalizedInput)
    {
        return IncrementRequestCountAsync(normalizedInput).GetAwaiter().GetResult();
    }

    /// <summary>Сбросить счётчик повторов для запроса (после создания навыка).</summary>
    public async Task ResetRequestCountAsync(string normalizedInput, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM request_stats WHERE norm_input = $n";
            cmd.Parameters.AddWithValue("$n", normalizedInput);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _connLock.Release();
        }
    }

    public void ResetRequestCount(string normalizedInput)
    {
        ResetRequestCountAsync(normalizedInput).GetAwaiter().GetResult();
    }

    /// <summary>Получить low-confidence взаимодействия за текущую сессию (для рефлексии).</summary>
    public async Task<List<InteractionLog>> GetLowConfidenceAsync(string sessionId, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            var list = new List<InteractionLog>();
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              SELECT session_id, input, output, confidence, mode, skill_id, provider, created_at
                              FROM interactions
                              WHERE session_id = $s AND confidence = 'low'
                              ORDER BY id
                              """;
            cmd.Parameters.AddWithValue("$s", sessionId);
            using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new InteractionLog(
                    r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                    r.GetString(4), r.IsDBNull(5)
                        ? null
                        : r.GetString(5),
                    r.IsDBNull(6)
                        ? ""
                        : r.GetString(6), DateTime.Parse(r.GetString(7))));
            }

            return list;
        }
        finally
        {
            _connLock.Release();
        }
    }

    public List<InteractionLog> GetLowConfidence(string sessionId)
    {
        return GetLowConfidenceAsync(sessionId).GetAwaiter().GetResult();
    }

    /// <summary>Сводная статистика по режимам (skill vs direct) за сессию.</summary>
    public async Task<(int Skill, int Direct)> GetModeStatsAsync(string sessionId, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              SELECT
                                  SUM(CASE WHEN mode='skill' THEN 1 ELSE 0 END),
                                  SUM(CASE WHEN mode='direct' THEN 1 ELSE 0 END)
                              FROM interactions WHERE session_id = $s
                              """;
            cmd.Parameters.AddWithValue("$s", sessionId);
            using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                return (r.IsDBNull(0)
                    ? 0
                    : r.GetInt32(0), r.IsDBNull(1)
                    ? 0
                    : r.GetInt32(1));
            }

            return (0, 0);
        }
        finally
        {
            _connLock.Release();
        }
    }

    public (int Skill, int Direct) GetModeStats(string sessionId)
    {
        return GetModeStatsAsync(sessionId).GetAwaiter().GetResult();
    }

    /// <summary>Глобальная статистика режимов (skill vs direct) по всем сессиям.</summary>
    public async Task<(int Skill, int Direct)> GetGlobalModeStatsAsync(CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              SELECT
                                  SUM(CASE WHEN mode='skill' THEN 1 ELSE 0 END),
                                  SUM(CASE WHEN mode='direct' THEN 1 ELSE 0 END)
                              FROM interactions
                              """;
            using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                return (r.IsDBNull(0)
                    ? 0
                    : r.GetInt32(0), r.IsDBNull(1)
                    ? 0
                    : r.GetInt32(1));
            }

            return (0, 0);
        }
        finally
        {
            _connLock.Release();
        }
    }

    public (int Skill, int Direct) GetGlobalModeStats()
    {
        return GetGlobalModeStatsAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    ///     Глобальный success_rate: доля ответов с уверенностью не 'low' среди всех взаимодействий.
    /// </summary>
    public async Task<double> GetGlobalSuccessRateAsync(CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              SELECT
                                  SUM(CASE WHEN confidence != 'low' THEN 1 ELSE 0 END) AS ok,
                                  COUNT(*) AS total
                              FROM interactions
                              """;
            using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                var total = r.IsDBNull(1)
                    ? 0
                    : r.GetInt32(1);
                if (total == 0)
                {
                    return 1.0;
                }

                var ok = r.IsDBNull(0)
                    ? 0
                    : r.GetInt32(0);
                return Math.Round(ok / (double)total, 2);
            }

            return 1.0;
        }
        finally
        {
            _connLock.Release();
        }
    }

    public double GetGlobalSuccessRate()
    {
        return GetGlobalSuccessRateAsync().GetAwaiter().GetResult();
    }

    /// <summary>Количество взаимодействий по дням (для графиков фронтенда).</summary>
    public async Task<List<(string Date, int Total, int Skill, int Direct)>> GetDailyStatsAsync(int days = 14, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            var list = new List<(string, int, int, int)>();
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              SELECT
                                  substr(created_at, 1, 10) AS day,
                                  COUNT(*) AS total,
                                  SUM(CASE WHEN mode='skill' THEN 1 ELSE 0 END) AS skill,
                                  SUM(CASE WHEN mode='direct' THEN 1 ELSE 0 END) AS direct
                              FROM interactions
                              GROUP BY day
                              ORDER BY day DESC
                              LIMIT $days
                              """;
            cmd.Parameters.AddWithValue("$days", days);
            using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add((
                    r.GetString(0),
                    r.IsDBNull(1)
                        ? 0
                        : r.GetInt32(1),
                    r.IsDBNull(2)
                        ? 0
                        : r.GetInt32(2),
                    r.IsDBNull(3)
                        ? 0
                        : r.GetInt32(3)));
            }

            list.Reverse(); // по возрастанию даты
            return list;
        }
        finally
        {
            _connLock.Release();
        }
    }

    public List<(string Date, int Total, int Skill, int Direct)> GetDailyStats(int days = 14)
    {
        return GetDailyStatsAsync(days).GetAwaiter().GetResult();
    }

    /// <summary>Общее количество взаимодействий.</summary>
    public async Task<int> GetTotalInteractionsAsync(CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM interactions";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0);
        }
        finally
        {
            _connLock.Release();
        }
    }

    public int GetTotalInteractions()
    {
        return GetTotalInteractionsAsync().GetAwaiter().GetResult();
    }

    // ---- Sandbox audit (Stage 4) ----

    /// <summary>
    ///     Записать факт выполнения кода в sandbox.
    ///     code_hash — SHA-256 hex (для аудита без хранения самого кода).
    /// </summary>
    public async Task LogSandboxExecutionAsync(
        string sessionId,
        string codeHash,
        string language,
        int? exitCode,
        string status,
        long durationMs,
        IReadOnlyList<string> blockedPatterns,
        CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              INSERT INTO sandbox_executions
                                  (session_id, code_hash, language, exit_code, status, duration_ms, blocked_patterns, created_at)
                              VALUES ($s, $h, $l, $e, $st, $d, $b, $t)
                              """;
            cmd.Parameters.AddWithValue("$s", sessionId);
            cmd.Parameters.AddWithValue("$h", codeHash);
            cmd.Parameters.AddWithValue("$l", language);
            cmd.Parameters.AddWithValue("$e", (object?)exitCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$st", status);
            cmd.Parameters.AddWithValue("$d", durationMs);
            cmd.Parameters.AddWithValue("$b",
                blockedPatterns.Count > 0
                    ? string.Join("; ", blockedPatterns)
                    : DBNull.Value);
            cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _connLock.Release();
        }
    }

    public void LogSandboxExecution(
        string sessionId, string codeHash, string language, int? exitCode,
        string status, long durationMs, IReadOnlyList<string> blockedPatterns)
    {
        LogSandboxExecutionAsync(sessionId, codeHash, language, exitCode, status, durationMs, blockedPatterns).GetAwaiter().GetResult();
    }

    /// <summary>Последние N выполнений в sandbox (для админ-вывода).</summary>
    public async Task<List<SandboxExecutionLog>> GetRecentSandboxExecutionsAsync(int limit = 20, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            var list = new List<SandboxExecutionLog>();
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              SELECT id, session_id, code_hash, language, exit_code, status, duration_ms, blocked_patterns, created_at
                              FROM sandbox_executions
                              ORDER BY id DESC
                              LIMIT $n
                              """;
            cmd.Parameters.AddWithValue("$n", limit);
            using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new SandboxExecutionLog(
                    r.GetInt64(0),
                    r.GetString(1),
                    r.GetString(2),
                    r.GetString(3),
                    r.IsDBNull(4)
                        ? null
                        : r.GetInt32(4),
                    r.GetString(5),
                    r.GetInt64(6),
                    r.IsDBNull(7)
                        ? ""
                        : r.GetString(7),
                    DateTime.Parse(r.GetString(8))));
            }

            return list;
        }
        finally
        {
            _connLock.Release();
        }
    }

    public List<SandboxExecutionLog> GetRecentSandboxExecutions(int limit = 20)
    {
        return GetRecentSandboxExecutionsAsync(limit).GetAwaiter().GetResult();
    }

    /// <summary>Failure rate за последние N выполнений (для ReflectionEngine).</summary>
    public async Task<double> GetRecentSandboxFailureRateAsync(int window = 5, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              SELECT
                                  SUM(CASE WHEN status IN ('failed', 'timeout', 'rejected', 'killed') THEN 1 ELSE 0 END) AS fails,
                                  COUNT(*) AS total
                              FROM (SELECT status FROM sandbox_executions ORDER BY id DESC LIMIT $n)
                              """;
            cmd.Parameters.AddWithValue("$n", window);
            using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                var total = r.IsDBNull(1)
                    ? 0
                    : r.GetInt32(1);
                if (total == 0)
                {
                    return 0.0;
                }

                var fails = r.IsDBNull(0)
                    ? 0
                    : r.GetInt32(0);
                return Math.Round(fails / (double)total, 2);
            }

            return 0.0;
        }
        finally
        {
            _connLock.Release();
        }
    }

    public double GetRecentSandboxFailureRate(int window = 5)
    {
        return GetRecentSandboxFailureRateAsync(window).GetAwaiter().GetResult();
    }

    // ---- Budget tracking (task_003) ----

    public async Task LogBudgetEntryAsync(
        string sessionId,
        string provider,
        string model,
        int inputTokens,
        int outputTokens,
        decimal costUsd,
        CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              INSERT INTO budget_entries (session_id, provider, model, input_tokens, output_tokens, cost_usd, created_at)
                              VALUES ($s, $p, $m, $it, $ot, $c, $t)
                              """;
            cmd.Parameters.AddWithValue("$s", sessionId);
            cmd.Parameters.AddWithValue("$p", provider);
            cmd.Parameters.AddWithValue("$m", model);
            cmd.Parameters.AddWithValue("$it", inputTokens);
            cmd.Parameters.AddWithValue("$ot", outputTokens);
            cmd.Parameters.AddWithValue("$c", costUsd);
            cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _connLock.Release();
        }
    }

    public void LogBudgetEntry(string sessionId, string provider, string model, int inputTokens, int outputTokens, decimal costUsd)
    {
        LogBudgetEntryAsync(sessionId, provider, model, inputTokens, outputTokens, costUsd).GetAwaiter().GetResult();
    }

    public async Task<BudgetSummary> GetBudgetSummaryAsync(DateTime? since = null, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            var sinceStr = since?.ToString("o") ?? "1970-01-01";
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              SELECT
                                  COUNT(*),
                                  COALESCE(SUM(input_tokens), 0),
                                  COALESCE(SUM(output_tokens), 0),
                                  COALESCE(SUM(cost_usd), 0.0)
                              FROM budget_entries
                              WHERE created_at >= $since
                              """;
            cmd.Parameters.AddWithValue("$since", sinceStr);
            using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                return new BudgetSummary(
                    r.GetInt32(0),
                    r.GetInt32(1),
                    r.GetInt32(2),
                    (decimal)r.GetDouble(3));
            }

            return new BudgetSummary(0, 0, 0, 0m);
        }
        finally
        {
            _connLock.Release();
        }
    }

    public BudgetSummary GetBudgetSummary(DateTime? since = null)
    {
        return GetBudgetSummaryAsync(since).GetAwaiter().GetResult();
    }

    public async Task<List<(string Date, int Calls, decimal CostUsd)>> GetDailyBudgetAsync(int days = 30, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            var list = new List<(string, int, decimal)>();
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              SELECT
                                  substr(created_at, 1, 10) AS day,
                                  COUNT(*) AS calls,
                                  COALESCE(SUM(cost_usd), 0.0) AS cost
                              FROM budget_entries
                              GROUP BY day
                              ORDER BY day DESC
                              LIMIT $days
                              """;
            cmd.Parameters.AddWithValue("$days", days);
            using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add((r.GetString(0), r.GetInt32(1), (decimal)r.GetDouble(2)));
            }

            list.Reverse();
            return list;
        }
        finally
        {
            _connLock.Release();
        }
    }

    public List<(string Date, int Calls, decimal CostUsd)> GetDailyBudget(int days = 30)
    {
        return GetDailyBudgetAsync(days).GetAwaiter().GetResult();
    }

    // ---- Audit log (task_003 / task_014) ----

    /// <summary>
    ///     Записать аудит-событие (task_003).
    ///     task_014: перенаправляет на LogAuditExAsync с базовыми полями.
    /// </summary>
    public Task LogAuditAsync(
        string actor,
        string action,
        string? target,
        string? details,
        string? sessionId,
        CancellationToken ct = default)
    {
        // task_071: delegate to LogAuditExAsync, which serialises access to
        // the shared connection. No lock is taken here because the call is
        // synchronous and LogAuditExAsync acquires the lock itself.
        return LogAuditExAsync(actor, action, target, details, sessionId,
            requestId: null, toolName: null, policyDecision: null,
            permissionUsed: null, result: null, payloadHash: null, ct: ct);
    }

    public void LogAudit(string actor, string action, string? target = null, string? details = null, string? sessionId = null)
    {
        LogAuditAsync(actor, action, target, details, sessionId).GetAwaiter().GetResult();
    }

    /// <summary>
    ///     Расширенное аудит-событие (task_014).
    ///     Все параметры кроме actor/action — опциональны.
    /// </summary>
    public async Task LogAuditExAsync(
        string actor,
        string action,
        string? target,
        string? details,
        string? sessionId,
        string? requestId,
        string? toolName,
        string? policyDecision,
        string? permissionUsed,
        string? result,
        string? payloadHash,
        CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              INSERT INTO audit_log
                                  (actor, action, target, details, session_id, created_at,
                                   request_id, tool_name, policy_decision, permission_used, result, payload_hash)
                              VALUES ($a, $ac, $t, $d, $s, $ct, $rid, $tn, $pd, $pu, $r, $ph)
                              """;
            cmd.Parameters.AddWithValue("$a", actor);
            cmd.Parameters.AddWithValue("$ac", action);
            cmd.Parameters.AddWithValue("$t", (object?)target ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$d", (object?)details ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$s", (object?)sessionId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ct", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$rid", (object?)requestId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$tn", (object?)toolName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$pd", (object?)policyDecision ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$pu", (object?)permissionUsed ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$r", (object?)result ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ph", (object?)payloadHash ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _connLock.Release();
        }
    }

    public async Task<List<AuditLogEntry>> GetAuditLogAsync(int limit = 100, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            var list = new List<AuditLogEntry>();
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              SELECT id, actor, action, target, details, session_id, created_at,
                                     request_id, tool_name, policy_decision, permission_used, result, payload_hash
                              FROM audit_log
                              ORDER BY id DESC
                              LIMIT $n
                              """;
            cmd.Parameters.AddWithValue("$n", limit);
            using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new AuditLogEntry(
                    r.GetInt64(0),
                    r.GetString(1),
                    r.GetString(2),
                    r.IsDBNull(3) ? null : r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.IsDBNull(5) ? null : r.GetString(5),
                    DateTime.Parse(r.GetString(6)))
                {
                    RequestId = r.IsDBNull(7) ? null : r.GetString(7),
                    ToolName = r.IsDBNull(8) ? null : r.GetString(8),
                    PolicyDecision = r.IsDBNull(9) ? null : r.GetString(9),
                    PermissionUsed = r.IsDBNull(10) ? null : r.GetString(10),
                    Result = r.IsDBNull(11) ? null : r.GetString(11),
                    PayloadHash = r.IsDBNull(12) ? null : r.GetString(12)
                });
            }

            return list;
        }
        finally
        {
            _connLock.Release();
        }
    }

    /// <summary>
    ///     task_077: Aggregated counts of <c>audit_log</c> rows filtered by <paramref name="action" />
    ///     and an optional time window. Replaces the 100k-row <c>QueryAsync</c> + in-memory
    ///     <c>.Count()</c> hot path used by SLO evaluation. Single SQL aggregate, no row materialisation.
    /// </summary>
    public async Task<AuditLogStats> GetAuditLogStatsAsync(
        string action,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default)
    {
        await _connLock.WaitAsync(ct);
        try
        {
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              SELECT
                                  COUNT(*) AS total,
                                  SUM(CASE WHEN result = 'success' THEN 1 ELSE 0 END) AS successes,
                                  SUM(CASE WHEN result LIKE '%failure%' THEN 1 ELSE 0 END) AS failures,
                                  SUM(CASE WHEN result LIKE '%timeout%' THEN 1 ELSE 0 END) AS timeouts,
                                  SUM(CASE WHEN result LIKE '%denied%' THEN 1 ELSE 0 END) AS denied
                              FROM audit_log
                              WHERE action = $a
                                AND ($from IS NULL OR created_at >= $from)
                                AND ($to   IS NULL OR created_at <  $to)
                              """;
            cmd.Parameters.AddWithValue("$a", action);
            cmd.Parameters.AddWithValue("$from", (object?)from?.ToString("o") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$to", (object?)to?.ToString("o") ?? DBNull.Value);
            using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct))
            {
                return new AuditLogStats(0, 0, 0, 0, 0);
            }

            return new AuditLogStats(
                r.IsDBNull(0) ? 0 : r.GetInt32(0),
                r.IsDBNull(1) ? 0 : r.GetInt32(1),
                r.IsDBNull(2) ? 0 : r.GetInt32(2),
                r.IsDBNull(3) ? 0 : r.GetInt32(3),
                r.IsDBNull(4) ? 0 : r.GetInt32(4));
        }
        finally
        {
            _connLock.Release();
        }
    }

    public List<AuditLogEntry> GetAuditLog(int limit = 100)
    {
        return GetAuditLogAsync(limit).GetAwaiter().GetResult();
    }

    public async Task<List<AuditLogEntry>> GetAuditLogByTargetAsync(string target, int limit = 50, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            var list = new List<AuditLogEntry>();
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              SELECT id, actor, action, target, details, session_id, created_at,
                                     request_id, tool_name, policy_decision, permission_used, result, payload_hash
                              FROM audit_log
                              WHERE target = $t
                              ORDER BY id DESC
                              LIMIT $n
                              """;
            cmd.Parameters.AddWithValue("$t", target);
            cmd.Parameters.AddWithValue("$n", limit);
            using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new AuditLogEntry(
                    r.GetInt64(0),
                    r.GetString(1),
                    r.GetString(2),
                    r.IsDBNull(3) ? null : r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.IsDBNull(5) ? null : r.GetString(5),
                    DateTime.Parse(r.GetString(6)))
                {
                    RequestId = r.IsDBNull(7) ? null : r.GetString(7),
                    ToolName = r.IsDBNull(8) ? null : r.GetString(8),
                    PolicyDecision = r.IsDBNull(9) ? null : r.GetString(9),
                    PermissionUsed = r.IsDBNull(10) ? null : r.GetString(10),
                    Result = r.IsDBNull(11) ? null : r.GetString(11),
                    PayloadHash = r.IsDBNull(12) ? null : r.GetString(12)
                });
            }

            return list;
        }
        finally
        {
            _connLock.Release();
        }
    }

    public List<AuditLogEntry> GetAuditLogByTarget(string target, int limit = 50)
    {
        return GetAuditLogByTargetAsync(target, limit).GetAwaiter().GetResult();
    }

    // ---- Skill evaluation history (task_003) ----

    public async Task SaveEvaluationResultAsync(
        string skillId,
        double score,
        bool passed,
        string? testResults,
        string evaluatorProvider,
        CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              INSERT INTO skill_evaluations (skill_id, score, passed, test_results, evaluator_provider, created_at)
                              VALUES ($sk, $sc, $p, $tr, $ev, $t)
                              """;
            cmd.Parameters.AddWithValue("$sk", skillId);
            cmd.Parameters.AddWithValue("$sc", score);
            cmd.Parameters.AddWithValue("$p", passed ? 1 : 0);
            cmd.Parameters.AddWithValue("$tr", (object?)testResults ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ev", evaluatorProvider);
            cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _connLock.Release();
        }
    }

    public void SaveEvaluationResult(string skillId, double score, bool passed, string? testResults, string evaluatorProvider)
    {
        SaveEvaluationResultAsync(skillId, score, passed, testResults, evaluatorProvider).GetAwaiter().GetResult();
    }

    public async Task<List<SkillEvaluationRecord>> GetSkillEvaluationHistoryAsync(string skillId, int limit = 20, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            var list = new List<SkillEvaluationRecord>();
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              SELECT id, skill_id, score, passed, test_results, evaluator_provider, created_at
                              FROM skill_evaluations
                              WHERE skill_id = $sk
                              ORDER BY id DESC
                              LIMIT $n
                              """;
            cmd.Parameters.AddWithValue("$sk", skillId);
            cmd.Parameters.AddWithValue("$n", limit);
            using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new SkillEvaluationRecord(
                    r.GetInt64(0),
                    r.GetString(1),
                    r.GetDouble(2),
                    r.GetInt32(3) == 1,
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.GetString(5),
                    DateTime.Parse(r.GetString(6))));
            }

            return list;
        }
        finally
        {
            _connLock.Release();
        }
    }

    public List<SkillEvaluationRecord> GetSkillEvaluationHistory(string skillId, int limit = 20)
    {
        return GetSkillEvaluationHistoryAsync(skillId, limit).GetAwaiter().GetResult();
    }

    // ---- Durable task state (task_003 / task_018) ----
    // task_018: extended SaveTaskStateAsync with new durable task columns

    public async Task SaveTaskStateAsync(
        string taskId,
        string status,
        string? result,
        string? error,
        string? metadata,
        CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow.ToString("o");
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              INSERT INTO task_states (task_id, status, result, error, metadata, created_at, updated_at)
                              VALUES ($id, $st, $r, $e, $m, $ca, $ua)
                              ON CONFLICT(task_id) DO UPDATE SET
                                  status = excluded.status,
                                  result = excluded.result,
                                  error  = excluded.error,
                                  metadata = excluded.metadata,
                                  updated_at = excluded.updated_at
                              """;
            cmd.Parameters.AddWithValue("$id", taskId);
            cmd.Parameters.AddWithValue("$st", status);
            cmd.Parameters.AddWithValue("$r", (object?)result ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$e", (object?)error ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$m", (object?)metadata ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ca", now);
            cmd.Parameters.AddWithValue("$ua", now);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _connLock.Release();
        }
    }

    public void SaveTaskState(string taskId, string status, string? result = null, string? error = null, string? metadata = null)
    {
        SaveTaskStateAsync(taskId, status, result, error, metadata).GetAwaiter().GetResult();
    }

    public async Task<TaskState?> LoadTaskStateAsync(string taskId, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              SELECT id, task_id, status, result, error, metadata, created_at, updated_at
                              FROM task_states WHERE task_id = $id
                              """;
            cmd.Parameters.AddWithValue("$id", taskId);
            using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                return new TaskState(
                    r.GetInt64(0),
                    r.GetString(1),
                    r.GetString(2),
                    r.IsDBNull(3) ? null : r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.IsDBNull(5) ? null : r.GetString(5),
                    DateTime.Parse(r.GetString(6)),
                    DateTime.Parse(r.GetString(7)));
            }

            return null;
        }
        finally
        {
            _connLock.Release();
        }
    }

    public TaskState? LoadTaskState(string taskId)
    {
        return LoadTaskStateAsync(taskId).GetAwaiter().GetResult();
    }

    public async Task<List<TaskState>> ListTaskStatesAsync(string? statusFilter = null, int limit = 100, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            var list = new List<TaskState>();
            using SqliteCommand cmd = _conn.CreateCommand();
            if (statusFilter is null)
            {
                cmd.CommandText = """
                                  SELECT id, task_id, status, result, error, metadata, created_at, updated_at
                                  FROM task_states
                                  ORDER BY updated_at DESC
                                  LIMIT $n
                                  """;
            }
            else
            {
                cmd.CommandText = """
                                  SELECT id, task_id, status, result, error, metadata, created_at, updated_at
                                  FROM task_states
                                  WHERE status = $st
                                  ORDER BY updated_at DESC
                                  LIMIT $n
                                  """;
                cmd.Parameters.AddWithValue("$st", statusFilter);
            }

            cmd.Parameters.AddWithValue("$n", limit);
            using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new TaskState(
                    r.GetInt64(0),
                    r.GetString(1),
                    r.GetString(2),
                    r.IsDBNull(3) ? null : r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.IsDBNull(5) ? null : r.GetString(5),
                    DateTime.Parse(r.GetString(6)),
                    DateTime.Parse(r.GetString(7))));
            }

            return list;
        }
        finally
        {
            _connLock.Release();
        }
    }

    public List<TaskState> ListTaskStates(string? statusFilter = null, int limit = 100)
    {
        return ListTaskStatesAsync(statusFilter, limit).GetAwaiter().GetResult();
    }

    // --- task_010: approval gates ---

    public async Task SaveApprovalRequestAsync(ApprovalRequest req, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              INSERT OR REPLACE INTO approval_requests
                                (id, session_id, tool_name, arguments_json, policy_decision, reason, requested_at, requested_by, status, approved_at, denied_at)
                              VALUES ($id, $s, $t, $a, $p, $r, $ra, $rb, $st, $aa, $da)
                              """;
            cmd.Parameters.AddWithValue("$id", req.Id);
            cmd.Parameters.AddWithValue("$s", req.SessionId);
            cmd.Parameters.AddWithValue("$t", req.ToolName);
            cmd.Parameters.AddWithValue("$a", req.ArgumentsJson);
            cmd.Parameters.AddWithValue("$p", req.PolicyDecision);
            cmd.Parameters.AddWithValue("$r", req.Reason);
            cmd.Parameters.AddWithValue("$ra", req.RequestedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$rb", (object?)req.RequestedBy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$st", req.Status);
            cmd.Parameters.AddWithValue("$aa", req.ApprovedAt?.ToString("o") ?? (object?)DBNull.Value);
            cmd.Parameters.AddWithValue("$da", req.DeniedAt?.ToString("o") ?? (object?)DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _connLock.Release();
        }
    }

    public async Task<List<ApprovalRequest>> GetPendingApprovalsAsync(string? sessionId = null, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            var list = new List<ApprovalRequest>();
            using SqliteCommand cmd = _conn.CreateCommand();
            if (sessionId is null)
            {
                cmd.CommandText = """
                                  SELECT id, session_id, tool_name, arguments_json, policy_decision, reason,
                                         requested_at, requested_by, status, approved_at, denied_at
                                  FROM approval_requests
                                  WHERE status = 'Pending'
                                  ORDER BY requested_at ASC
                                  """;
            }
            else
            {
                cmd.CommandText = """
                                  SELECT id, session_id, tool_name, arguments_json, policy_decision, reason,
                                         requested_at, requested_by, status, approved_at, denied_at
                                  FROM approval_requests
                                  WHERE status = 'Pending' AND session_id = $s
                                  ORDER BY requested_at ASC
                                  """;
                cmd.Parameters.AddWithValue("$s", sessionId);
            }

            using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new ApprovalRequest(
                    r.GetString(0),  // id
                    r.GetString(1),  // session_id
                    r.GetString(2),  // tool_name
                    r.GetString(3),  // arguments_json
                    r.GetString(4),  // policy_decision
                    r.GetString(5),  // reason
                    DateTime.Parse(r.GetString(6)),  // requested_at
                    r.IsDBNull(7) ? null : r.GetString(7),  // requested_by
                    r.GetString(8),  // status
                    r.IsDBNull(9) ? null : DateTime.Parse(r.GetString(9)),  // approved_at
                    r.IsDBNull(10) ? null : DateTime.Parse(r.GetString(10))  // denied_at
                ));
            }

            return list;
        }
        finally
        {
            _connLock.Release();
        }
    }

    public async Task<List<ApprovalRequest>> GetApprovalRequestsAsync(string? sessionId = null, int limit = 100, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            var list = new List<ApprovalRequest>();
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = sessionId is null
                ? """
                  SELECT id, session_id, tool_name, arguments_json, policy_decision, reason,
                         requested_at, requested_by, status, approved_at, denied_at
                  FROM approval_requests ORDER BY requested_at DESC LIMIT $n
                  """
                : """
                  SELECT id, session_id, tool_name, arguments_json, policy_decision, reason,
                         requested_at, requested_by, status, approved_at, denied_at
                  FROM approval_requests WHERE session_id = $s ORDER BY requested_at DESC LIMIT $n
                  """;
            if (sessionId is not null) cmd.Parameters.AddWithValue("$s", sessionId);
            cmd.Parameters.AddWithValue("$n", limit);
            using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new ApprovalRequest(
                    r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
                    DateTime.Parse(r.GetString(6)),
                    r.IsDBNull(7) ? null : r.GetString(7),
                    r.GetString(8),
                    r.IsDBNull(9) ? null : DateTime.Parse(r.GetString(9)),
                    r.IsDBNull(10) ? null : DateTime.Parse(r.GetString(10))));
            }

            return list;
        }
        finally
        {
            _connLock.Release();
        }
    }

    public async Task UpdateApprovalStatusAsync(string id, string status, DateTime? approvedAt = null, DateTime? deniedAt = null, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              UPDATE approval_requests
                              SET status = $st, approved_at = $aa, denied_at = $da
                              WHERE id = $id
                              """;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$st", status);
            cmd.Parameters.AddWithValue("$aa", approvedAt?.ToString("o") ?? (object?)DBNull.Value);
            cmd.Parameters.AddWithValue("$da", deniedAt?.ToString("o") ?? (object?)DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _connLock.Release();
        }
    }

    public async Task ExpireOldApprovalsAsync(int ttlMinutes, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-ttlMinutes).ToString("o");
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE approval_requests SET status = 'Expired' WHERE status = 'Pending' AND requested_at < $cutoff";
            cmd.Parameters.AddWithValue("$cutoff", cutoff);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _connLock.Release();
        }
    }

    // ---- task_018: durable task repository methods ----

    private static Hercules.Tasks.DurableTask? MapRowToDurableTask(SqliteDataReader r)
    {
        var taskIdStr = r.GetString(r.GetOrdinal("task_id"));
        var statusStr = r.GetString(r.GetOrdinal("status"));
        if (!Enum.TryParse<DurableTaskStatus>(statusStr, true, out var status))
            status = DurableTaskStatus.Pending;

        var metadata = new DurableTaskMetadata();
        if (!r.IsDBNull(r.GetOrdinal("skill_id")))
            metadata.SkillId = r.GetString(r.GetOrdinal("skill_id"));
        if (!r.IsDBNull(r.GetOrdinal("session_id")))
            metadata.SessionId = r.GetString(r.GetOrdinal("session_id"));
        if (!r.IsDBNull(r.GetOrdinal("priority")))
            metadata.Priority = r.GetString(r.GetOrdinal("priority"));
        if (!r.IsDBNull(r.GetOrdinal("created_by")))
            metadata.CreatedBy = r.GetString(r.GetOrdinal("created_by"));
        if (!r.IsDBNull(r.GetOrdinal("description")))
            metadata.Description = r.GetString(r.GetOrdinal("description"));
        if (!r.IsDBNull(r.GetOrdinal("owner_agent_id")))
            metadata.OwnerAgentId = r.GetString(r.GetOrdinal("owner_agent_id"));
        if (!r.IsDBNull(r.GetOrdinal("tags")))
        {
            var tagsJson = r.GetString(r.GetOrdinal("tags"));
            if (!string.IsNullOrEmpty(tagsJson))
            {
                try { metadata.Tags = System.Text.Json.JsonSerializer.Deserialize<List<string>>(tagsJson) ?? new(); }
                catch { metadata.Tags = new(); }
            }
        }

        var retryPolicy = new TaskRetryPolicy();
        if (!r.IsDBNull(r.GetOrdinal("retry_policy")))
        {
            var rpJson = r.GetString(r.GetOrdinal("retry_policy"));
            if (!string.IsNullOrEmpty(rpJson))
            {
                try { retryPolicy = System.Text.Json.JsonSerializer.Deserialize<TaskRetryPolicy>(rpJson) ?? retryPolicy; }
                catch { /* use defaults */ }
            }
        }

        var attemptOrdinal = r.GetOrdinal("attempt_count");
        var stepOrdinal = r.GetOrdinal("current_step");

        DateTime? completedAt = null;
        var completedAtOrdinal = r.GetOrdinal("completed_at");
        if (!r.IsDBNull(completedAtOrdinal))
            completedAt = DateTime.Parse(r.GetString(completedAtOrdinal));

        return new Hercules.Tasks.DurableTask(
            new Hercules.Tasks.TaskId(Guid.Parse(taskIdStr)),
            r.IsDBNull(r.GetOrdinal("name")) ? "" : r.GetString(r.GetOrdinal("name")),
            status,
            metadata,
            retryPolicy,
            r.IsDBNull(attemptOrdinal) ? 0 : r.GetInt32(attemptOrdinal),
            r.IsDBNull(stepOrdinal) ? 0 : r.GetInt32(stepOrdinal),
            r.IsDBNull(r.GetOrdinal("result")) ? null : r.GetString(r.GetOrdinal("result")),
            r.IsDBNull(r.GetOrdinal("error")) ? null : r.GetString(r.GetOrdinal("error")),
            DateTime.Parse(r.GetString(r.GetOrdinal("created_at"))),
            DateTime.Parse(r.GetString(r.GetOrdinal("updated_at"))),
            completedAt,
            r.IsDBNull(r.GetOrdinal("cancellation_reason")) ? null : r.GetString(r.GetOrdinal("cancellation_reason")));
    }

    /// <summary>
    ///     task_018: Save full DurableTask state (INSERT OR REPLACE).
    ///     Maps DurableTask fields to the extended task_states columns.
    /// </summary>
    public async Task SaveDurableTaskAsync(Hercules.Tasks.DurableTask task, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow.ToString("o");
            var metadata = task.Metadata;
            var rpJson = System.Text.Json.JsonSerializer.Serialize(task.RetryPolicy);
            var tagsJson = System.Text.Json.JsonSerializer.Serialize(metadata.Tags);

            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              INSERT INTO task_states (task_id, name, status, result, error, metadata,
                                  created_at, updated_at, attempt_count, current_step, completed_at,
                                  cancellation_reason, retry_policy, skill_id, session_id,
                                  priority, tags, created_by, description, owner_agent_id)
                              VALUES ($id, $name, $st, $r, $e, $m, $ca, $ua, $ac, $cs, $compl,
                                  $cr, $rp, $skid, $sid, $pri, $tags, $cb, $desc, $oid)
                              ON CONFLICT(task_id) DO UPDATE SET
                                  name = excluded.name,
                                  status = excluded.status,
                                  result = excluded.result,
                                  error = excluded.error,
                                  metadata = excluded.metadata,
                                  updated_at = excluded.updated_at,
                                  attempt_count = excluded.attempt_count,
                                  current_step = excluded.current_step,
                                  completed_at = excluded.completed_at,
                                  cancellation_reason = excluded.cancellation_reason,
                                  retry_policy = excluded.retry_policy,
                                  skill_id = excluded.skill_id,
                                  session_id = excluded.session_id,
                                  priority = excluded.priority,
                                  tags = excluded.tags,
                                  created_by = excluded.created_by,
                                  description = excluded.description,
                                  owner_agent_id = excluded.owner_agent_id
                              """;
            cmd.Parameters.AddWithValue("$id", task.Id.ToString());
            cmd.Parameters.AddWithValue("$name", (object?)task.Name ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$st", task.Status.ToString());
            cmd.Parameters.AddWithValue("$r", (object?)task.Result ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$e", (object?)task.Error ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$m", DBNull.Value); // legacy metadata column
            cmd.Parameters.AddWithValue("$ca", task.CreatedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$ua", now);
            cmd.Parameters.AddWithValue("$ac", task.AttemptCount);
            cmd.Parameters.AddWithValue("$cs", task.CurrentStep);
            cmd.Parameters.AddWithValue("$compl", task.CompletedAt?.ToString("o") ?? (object?)DBNull.Value);
            cmd.Parameters.AddWithValue("$cr", (object?)task.CancellationReason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$rp", rpJson);
            cmd.Parameters.AddWithValue("$skid", (object?)metadata.SkillId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$sid", (object?)metadata.SessionId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$pri", metadata.Priority);
            cmd.Parameters.AddWithValue("$tags", tagsJson);
            cmd.Parameters.AddWithValue("$cb", (object?)metadata.CreatedBy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$desc", (object?)metadata.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$oid", (object?)metadata.OwnerAgentId ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _connLock.Release();
        }
    }

    /// <summary>
    ///     task_018: Load a DurableTask by ID.
    /// </summary>
    public async Task<Hercules.Tasks.DurableTask?> LoadDurableTaskAsync(string taskId, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT task_id, name, status, result, error, metadata, created_at, updated_at, attempt_count, current_step, completed_at, cancellation_reason, retry_policy, skill_id, session_id, priority, tags, created_by, description, owner_agent_id FROM task_states WHERE task_id = $id";
            cmd.Parameters.AddWithValue("$id", taskId);
            using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                return MapRowToDurableTask(r);
            }
            return null;
        }
        finally
        {
            _connLock.Release();
        }
    }

    /// <summary>
    ///     task_018: List DurableTasks with optional status filter.
    /// </summary>
    public async Task<List<Hercules.Tasks.DurableTask>> ListDurableTasksAsync(DurableTaskStatus? statusFilter = null, int limit = 100, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            var list = new List<Hercules.Tasks.DurableTask>();
            using SqliteCommand cmd = _conn.CreateCommand();
            var sql = "SELECT task_id, name, status, result, error, metadata, created_at, updated_at, attempt_count, current_step, completed_at, cancellation_reason, retry_policy, skill_id, session_id, priority, tags, created_by, description, owner_agent_id FROM task_states";
            if (statusFilter.HasValue)
                sql += " WHERE status = $st";
            sql += " ORDER BY updated_at DESC LIMIT $n";
            cmd.CommandText = sql;
            if (statusFilter.HasValue) cmd.Parameters.AddWithValue("$st", statusFilter.Value.ToString());
            cmd.Parameters.AddWithValue("$n", limit);
            using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var task = MapRowToDurableTask(r);
                if (task is not null) list.Add(task);
            }
            return list;
        }
        finally
        {
            _connLock.Release();
        }
    }

    /// <summary>
    ///     task_018: Delete a DurableTask.
    /// </summary>
    public async Task DeleteDurableTaskAsync(string taskId, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM task_states WHERE task_id = $id";
            cmd.Parameters.AddWithValue("$id", taskId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _connLock.Release();
        }
    }

    /// <summary>
    ///     task_018: Checkpoint table for durable tasks.
    /// </summary>
    public async Task InitCheckpointSchemaAsync(CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                             CREATE TABLE IF NOT EXISTS task_checkpoints (
                                 id              TEXT PRIMARY KEY,
                                 task_id         TEXT NOT NULL,
                                 step_number     INTEGER NOT NULL,
                                 state_snapshot  TEXT NOT NULL,
                                 created_at      TEXT NOT NULL
                             );
                             CREATE INDEX IF NOT EXISTS ix_ckp_task ON task_checkpoints(task_id);
                             """;
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _connLock.Release();
        }
    }

    /// <summary>
    ///     task_018: Save a task checkpoint.
    /// </summary>
    public async Task SaveCheckpointAsync(TaskCheckpoint ckpt, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              INSERT OR REPLACE INTO task_checkpoints (id, task_id, step_number, state_snapshot, created_at)
                              VALUES ($id, $tid, $sn, $ss, $ca)
                              """;
            cmd.Parameters.AddWithValue("$id", ckpt.Id);
            cmd.Parameters.AddWithValue("$tid", ckpt.TaskId.ToString());
            cmd.Parameters.AddWithValue("$sn", ckpt.StepNumber);
            cmd.Parameters.AddWithValue("$ss", ckpt.StateSnapshot);
            cmd.Parameters.AddWithValue("$ca", ckpt.CreatedAt.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _connLock.Release();
        }
    }

    /// <summary>
    ///     task_018: List checkpoints for a task.
    /// </summary>
    public async Task<List<TaskCheckpoint>> ListCheckpointsAsync(string taskId, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            var list = new List<TaskCheckpoint>();
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT id, task_id, step_number, state_snapshot, created_at FROM task_checkpoints WHERE task_id = $tid ORDER BY step_number ASC";
            cmd.Parameters.AddWithValue("$tid", taskId);
            using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new TaskCheckpoint
                {
                    Id = r.GetString(0),
                    TaskId = new TaskId(Guid.Parse(r.GetString(1))),
                    StepNumber = r.GetInt32(2),
                    StateSnapshot = r.GetString(3),
                    CreatedAt = DateTime.Parse(r.GetString(4))
                });
            }
            return list;
        }
        finally
        {
            _connLock.Release();
        }
    }

    /// <summary>
    ///     task_018: Delete old checkpoints (retention policy).
    /// </summary>
    public async Task CleanupOldCheckpointsAsync(int retentionDays, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays).ToString("o");
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM task_checkpoints WHERE created_at < $cutoff";
            cmd.Parameters.AddWithValue("$cutoff", cutoff);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _connLock.Release();
        }
    }

    // ---- Escalation persistence (task_049) ----

    /// <summary>
    ///     task_049: Persist a new escalation record.
    /// </summary>
    public async Task SaveEscalationAsync(Hercules.Mesh.Escalation.EscalationResult e, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              INSERT INTO escalations (id, request_id, session_id, agent_id, type, severity, status,
                                  action_plan, context, payload_json, tool_or_intent, requested_by, created_at,
                                  resolved_at, resolved_by)
                              VALUES ($id, $rid, $sid, $aid, $t, $sev, $st, $ap, $ctx, $pj, $toi, $rb, $ca, $ra, $rb2)
                              """;
            cmd.Parameters.AddWithValue("$id", e.EscalationId);
            cmd.Parameters.AddWithValue("$rid", e.RequestId);
            cmd.Parameters.AddWithValue("$sid", e.SessionId);
            cmd.Parameters.AddWithValue("$aid", e.SessionId); // agent_id placeholder
            cmd.Parameters.AddWithValue("$t", e.Type.ToString());
            cmd.Parameters.AddWithValue("$sev", e.Severity.ToString());
            cmd.Parameters.AddWithValue("$st", e.Status.ToString());
            cmd.Parameters.AddWithValue("$ap", e.ActionPlan);
            cmd.Parameters.AddWithValue("$ctx", e.Context);
            cmd.Parameters.AddWithValue("$pj", (object?)e.PayloadJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$toi", (object?)e.ToolOrIntentName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$rb", e.RequestedBy);
            cmd.Parameters.AddWithValue("$ca", e.CreatedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$ra", e.ResolvedAt?.ToString("o") ?? (object?)DBNull.Value);
            cmd.Parameters.AddWithValue("$rb2", (object?)e.ResolvedBy ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _connLock.Release();
        }
    }

    /// <summary>
    ///     task_049: Update escalation status.
    /// </summary>
    public async Task UpdateEscalationStatusAsync(string escalationId, string status, string? resolvedBy = null, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow.ToString("o");
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = resolvedBy is not null
                ? "UPDATE escalations SET status = $st, resolved_at = $ra, resolved_by = $rb WHERE id = $id"
                : "UPDATE escalations SET status = $st WHERE id = $id";
            cmd.Parameters.AddWithValue("$st", status);
            cmd.Parameters.AddWithValue("$ra", now);
            cmd.Parameters.AddWithValue("$rb", resolvedBy);
            cmd.Parameters.AddWithValue("$id", escalationId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _connLock.Release();
        }
    }

    /// <summary>
    ///     task_049: Expire old pending escalations.
    /// </summary>
    public async Task ExpireOldEscalationsAsync(int ttlMinutes, CancellationToken ct = default)
    {
        // task_071: serialise concurrent access to the shared connection.
        await _connLock.WaitAsync(ct);
        try
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-ttlMinutes).ToString("o");
            using SqliteCommand cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE escalations SET status = 'Expired' WHERE status = 'Pending' AND created_at < $cutoff";
            cmd.Parameters.AddWithValue("$cutoff", cutoff);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _connLock.Release();
        }
    }
}

/// <summary>Запись о выполнении кода в sandbox (audit log).</summary>
public sealed record SandboxExecutionLog(
    long Id,
    string SessionId,
    string CodeHash,
    string Language,
    int? ExitCode,
    string Status,
    long DurationMs,
    string BlockedPatterns,
    DateTime CreatedAt);
