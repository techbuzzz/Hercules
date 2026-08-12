using Hercules.Config;
using Microsoft.Data.Sqlite;

namespace Hercules.Storage;

/// <summary>
///     Хранилище сессий, логов взаимодействий и метрик в SQLite.
/// </summary>
public sealed class SqliteSessionStore : IAsyncDisposable, IDisposable
{
    private readonly SqliteConnection _conn;

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
    }

    public ValueTask DisposeAsync()
    {
        _conn.Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        _conn.Dispose();
    }

    public bool IsHealthy()
    {
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
    }

    private void EnableWalMode()
    {
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
    }

    private void InitSchema()
    {
        const string sql = """
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
                           CREATE TABLE IF NOT EXISTS audit_log (
                               id              INTEGER PRIMARY KEY AUTOINCREMENT,
                               actor           TEXT NOT NULL,
                               action          TEXT NOT NULL,
                               target          TEXT,
                               details         TEXT,
                               session_id      TEXT,
                               created_at      TEXT NOT NULL
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
                           CREATE INDEX IF NOT EXISTS ix_interactions_session ON interactions(session_id);
                           CREATE INDEX IF NOT EXISTS ix_interactions_created ON interactions(created_at);
                           CREATE INDEX IF NOT EXISTS ix_sandbox_executions_session ON sandbox_executions(session_id);
                           CREATE INDEX IF NOT EXISTS ix_budget_session ON budget_entries(session_id);
                           CREATE INDEX IF NOT EXISTS ix_budget_created ON budget_entries(created_at);
                           CREATE INDEX IF NOT EXISTS ix_audit_created ON audit_log(created_at);
                           CREATE INDEX IF NOT EXISTS ix_audit_target ON audit_log(target);
                           CREATE INDEX IF NOT EXISTS ix_eval_skill ON skill_evaluations(skill_id);
                           CREATE INDEX IF NOT EXISTS ix_task_taskid ON task_states(task_id);
                           """;
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public Task StartSessionAsync(string sessionId, CancellationToken ct = default)
    {
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO sessions (id, started_at) VALUES ($id, $t)";
        cmd.Parameters.AddWithValue("$id", sessionId);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
        return cmd.ExecuteNonQueryAsync(ct);
    }

    public void StartSession(string sessionId)
    {
        StartSessionAsync(sessionId).GetAwaiter().GetResult();
    }

    public Task EndSessionAsync(string sessionId, CancellationToken ct = default)
    {
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE sessions SET ended_at = $t WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", sessionId);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
        return cmd.ExecuteNonQueryAsync(ct);
    }

    public void EndSession(string sessionId)
    {
        EndSessionAsync(sessionId).GetAwaiter().GetResult();
    }

    public Task LogInteractionAsync(InteractionLog log, CancellationToken ct = default)
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
        return cmd.ExecuteNonQueryAsync(ct);
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

    public int IncrementRequestCount(string normalizedInput)
    {
        return IncrementRequestCountAsync(normalizedInput).GetAwaiter().GetResult();
    }

    /// <summary>Сбросить счётчик повторов для запроса (после создания навыка).</summary>
    public Task ResetRequestCountAsync(string normalizedInput, CancellationToken ct = default)
    {
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM request_stats WHERE norm_input = $n";
        cmd.Parameters.AddWithValue("$n", normalizedInput);
        return cmd.ExecuteNonQueryAsync(ct);
    }

    public void ResetRequestCount(string normalizedInput)
    {
        ResetRequestCountAsync(normalizedInput).GetAwaiter().GetResult();
    }

    /// <summary>Получить low-confidence взаимодействия за текущую сессию (для рефлексии).</summary>
    public async Task<List<InteractionLog>> GetLowConfidenceAsync(string sessionId, CancellationToken ct = default)
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

    public List<InteractionLog> GetLowConfidence(string sessionId)
    {
        return GetLowConfidenceAsync(sessionId).GetAwaiter().GetResult();
    }

    /// <summary>Сводная статистика по режимам (skill vs direct) за сессию.</summary>
    public async Task<(int Skill, int Direct)> GetModeStatsAsync(string sessionId, CancellationToken ct = default)
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

    public (int Skill, int Direct) GetModeStats(string sessionId)
    {
        return GetModeStatsAsync(sessionId).GetAwaiter().GetResult();
    }

    /// <summary>Глобальная статистика режимов (skill vs direct) по всем сессиям.</summary>
    public async Task<(int Skill, int Direct)> GetGlobalModeStatsAsync(CancellationToken ct = default)
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

    public (int Skill, int Direct) GetGlobalModeStats()
    {
        return GetGlobalModeStatsAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    ///     Глобальный success_rate: доля ответов с уверенностью не 'low' среди всех взаимодействий.
    /// </summary>
    public async Task<double> GetGlobalSuccessRateAsync(CancellationToken ct = default)
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

    public double GetGlobalSuccessRate()
    {
        return GetGlobalSuccessRateAsync().GetAwaiter().GetResult();
    }

    /// <summary>Количество взаимодействий по дням (для графиков фронтенда).</summary>
    public async Task<List<(string Date, int Total, int Skill, int Direct)>> GetDailyStatsAsync(int days = 14, CancellationToken ct = default)
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

    public List<(string Date, int Total, int Skill, int Direct)> GetDailyStats(int days = 14)
    {
        return GetDailyStatsAsync(days).GetAwaiter().GetResult();
    }

    /// <summary>Общее количество взаимодействий.</summary>
    public async Task<int> GetTotalInteractionsAsync(CancellationToken ct = default)
    {
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM interactions";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0);
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

    public void LogSandboxExecution(
        string sessionId, string codeHash, string language, int? exitCode,
        string status, long durationMs, IReadOnlyList<string> blockedPatterns)
    {
        LogSandboxExecutionAsync(sessionId, codeHash, language, exitCode, status, durationMs, blockedPatterns).GetAwaiter().GetResult();
    }

    /// <summary>Последние N выполнений в sandbox (для админ-вывода).</summary>
    public async Task<List<SandboxExecutionLog>> GetRecentSandboxExecutionsAsync(int limit = 20, CancellationToken ct = default)
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

    public List<SandboxExecutionLog> GetRecentSandboxExecutions(int limit = 20)
    {
        return GetRecentSandboxExecutionsAsync(limit).GetAwaiter().GetResult();
    }

    /// <summary>Failure rate за последние N выполнений (для ReflectionEngine).</summary>
    public async Task<double> GetRecentSandboxFailureRateAsync(int window = 5, CancellationToken ct = default)
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

    public void LogBudgetEntry(string sessionId, string provider, string model, int inputTokens, int outputTokens, decimal costUsd)
    {
        LogBudgetEntryAsync(sessionId, provider, model, inputTokens, outputTokens, costUsd).GetAwaiter().GetResult();
    }

    public async Task<BudgetSummary> GetBudgetSummaryAsync(DateTime? since = null, CancellationToken ct = default)
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

    public BudgetSummary GetBudgetSummary(DateTime? since = null)
    {
        return GetBudgetSummaryAsync(since).GetAwaiter().GetResult();
    }

    public async Task<List<(string Date, int Calls, decimal CostUsd)>> GetDailyBudgetAsync(int days = 30, CancellationToken ct = default)
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

    public List<(string Date, int Calls, decimal CostUsd)> GetDailyBudget(int days = 30)
    {
        return GetDailyBudgetAsync(days).GetAwaiter().GetResult();
    }

    // ---- Audit log (task_003) ----

    public async Task LogAuditAsync(
        string actor,
        string action,
        string? target,
        string? details,
        string? sessionId,
        CancellationToken ct = default)
    {
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = """
                          INSERT INTO audit_log (actor, action, target, details, session_id, created_at)
                          VALUES ($a, $ac, $t, $d, $s, $ct)
                          """;
        cmd.Parameters.AddWithValue("$a", actor);
        cmd.Parameters.AddWithValue("$ac", action);
        cmd.Parameters.AddWithValue("$t", (object?)target ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$d", (object?)details ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$s", (object?)sessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ct", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public void LogAudit(string actor, string action, string? target = null, string? details = null, string? sessionId = null)
    {
        LogAuditAsync(actor, action, target, details, sessionId).GetAwaiter().GetResult();
    }

    public async Task<List<AuditLogEntry>> GetAuditLogAsync(int limit = 100, CancellationToken ct = default)
    {
        var list = new List<AuditLogEntry>();
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = """
                          SELECT id, actor, action, target, details, session_id, created_at
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
                DateTime.Parse(r.GetString(6))));
        }

        return list;
    }

    public List<AuditLogEntry> GetAuditLog(int limit = 100)
    {
        return GetAuditLogAsync(limit).GetAwaiter().GetResult();
    }

    public async Task<List<AuditLogEntry>> GetAuditLogByTargetAsync(string target, int limit = 50, CancellationToken ct = default)
    {
        var list = new List<AuditLogEntry>();
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = """
                          SELECT id, actor, action, target, details, session_id, created_at
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
                DateTime.Parse(r.GetString(6))));
        }

        return list;
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

    public void SaveEvaluationResult(string skillId, double score, bool passed, string? testResults, string evaluatorProvider)
    {
        SaveEvaluationResultAsync(skillId, score, passed, testResults, evaluatorProvider).GetAwaiter().GetResult();
    }

    public async Task<List<SkillEvaluationRecord>> GetSkillEvaluationHistoryAsync(string skillId, int limit = 20, CancellationToken ct = default)
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

    public List<SkillEvaluationRecord> GetSkillEvaluationHistory(string skillId, int limit = 20)
    {
        return GetSkillEvaluationHistoryAsync(skillId, limit).GetAwaiter().GetResult();
    }

    // ---- Durable task state (task_003 / task_018) ----

    public async Task SaveTaskStateAsync(
        string taskId,
        string status,
        string? result,
        string? error,
        string? metadata,
        CancellationToken ct = default)
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

    public void SaveTaskState(string taskId, string status, string? result = null, string? error = null, string? metadata = null)
    {
        SaveTaskStateAsync(taskId, status, result, error, metadata).GetAwaiter().GetResult();
    }

    public async Task<TaskState?> LoadTaskStateAsync(string taskId, CancellationToken ct = default)
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

    public TaskState? LoadTaskState(string taskId)
    {
        return LoadTaskStateAsync(taskId).GetAwaiter().GetResult();
    }

    public async Task<List<TaskState>> ListTaskStatesAsync(string? statusFilter = null, int limit = 100, CancellationToken ct = default)
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

    public List<TaskState> ListTaskStates(string? statusFilter = null, int limit = 100)
    {
        return ListTaskStatesAsync(statusFilter, limit).GetAwaiter().GetResult();
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
