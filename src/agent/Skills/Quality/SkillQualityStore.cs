using Microsoft.Data.Sqlite;
using Hercules.Config;

namespace Hercules.Skills.Quality;

/// <summary>
///     SQLite-backed persistence for skill quality metrics.
///     Task 029: Skill Quality Score.
/// </summary>
public sealed class SkillQualityStore : IAsyncDisposable, IDisposable
{
    private readonly SqliteConnection _conn;

    public SkillQualityStore(StorageConfig storageCfg)
    {
        Directory.CreateDirectory(storageCfg.DataRoot);
        var dbPath = Path.Combine(storageCfg.DataRoot, storageCfg.SqliteFile);
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        _conn = new SqliteConnection(connStr);
        _conn.Open();
        InitSchema();
    }

    public void Dispose()
    {
        _conn.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        _conn.Dispose();
        return ValueTask.CompletedTask;
    }

    private void InitSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS skill_quality_metrics (
                skill_id          TEXT NOT NULL,
                version           INTEGER NOT NULL,
                acceptance_rate   REAL NOT NULL DEFAULT 1.0,
                test_score        REAL NOT NULL DEFAULT 0.0,
                user_correction_rate REAL NOT NULL DEFAULT 0.0,
                fallback_rate     REAL NOT NULL DEFAULT 0.0,
                avg_latency_ms    REAL NOT NULL DEFAULT 0.0,
                avg_cost_usd      REAL NOT NULL DEFAULT 0.0,
                total_calls       INTEGER NOT NULL DEFAULT 0,
                safety_denial_count INTEGER NOT NULL DEFAULT 0,
                fallback_count    INTEGER NOT NULL DEFAULT 0,
                user_correction_count INTEGER NOT NULL DEFAULT 0,
                total_latency_ms  REAL NOT NULL DEFAULT 0.0,
                total_cost_usd    REAL NOT NULL DEFAULT 0.0,
                updated_at        TEXT NOT NULL,
                PRIMARY KEY (skill_id, version)
            );
            CREATE INDEX IF NOT EXISTS ix_quality_skill ON skill_quality_metrics(skill_id);
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Load metrics for a skill+version. Returns null if not found.</summary>
    public async Task<SkillQualityMetrics?> LoadMetricsAsync(string skillId, int version, CancellationToken ct = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT skill_id, version, acceptance_rate, test_score, user_correction_rate,
                   fallback_rate, avg_latency_ms, avg_cost_usd, total_calls,
                   safety_denial_count, fallback_count, user_correction_count,
                   total_latency_ms, total_cost_usd, updated_at
              FROM skill_quality_metrics
             WHERE skill_id = @skillId AND version = @version
            """;
        cmd.Parameters.AddWithValue("@skillId", skillId);
        cmd.Parameters.AddWithValue("@version", version);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return ReadMetrics(reader);
    }

    /// <summary>Save or upsert metrics for a skill+version.</summary>
    public async Task SaveMetricsAsync(SkillQualityMetrics m, CancellationToken ct = default)
    {
        m.UpdatedAt = DateTime.UtcNow.ToString("o");
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO skill_quality_metrics
                (skill_id, version, acceptance_rate, test_score, user_correction_rate,
                 fallback_rate, avg_latency_ms, avg_cost_usd, total_calls,
                 safety_denial_count, fallback_count, user_correction_count,
                 total_latency_ms, total_cost_usd, updated_at)
            VALUES
                (@skillId, @version, @acceptanceRate, @testScore, @userCorrectionRate,
                 @fallbackRate, @avgLatencyMs, @avgCostUsd, @totalCalls,
                 @safetyDenialCount, @fallbackCount, @userCorrectionCount,
                 @totalLatencyMs, @totalCostUsd, @updatedAt)
            ON CONFLICT(skill_id, version) DO UPDATE SET
                acceptance_rate       = excluded.acceptance_rate,
                test_score           = excluded.test_score,
                user_correction_rate = excluded.user_correction_rate,
                fallback_rate        = excluded.fallback_rate,
                avg_latency_ms       = excluded.avg_latency_ms,
                avg_cost_usd         = excluded.avg_cost_usd,
                total_calls          = excluded.total_calls,
                safety_denial_count  = excluded.safety_denial_count,
                fallback_count       = excluded.fallback_count,
                user_correction_count = excluded.user_correction_count,
                total_latency_ms     = excluded.total_latency_ms,
                total_cost_usd       = excluded.total_cost_usd,
                updated_at            = excluded.updated_at
            """;
        cmd.Parameters.AddWithValue("@skillId", m.SkillId);
        cmd.Parameters.AddWithValue("@version", m.Version);
        cmd.Parameters.AddWithValue("@acceptanceRate", m.AcceptanceRate);
        cmd.Parameters.AddWithValue("@testScore", m.TestScore);
        cmd.Parameters.AddWithValue("@userCorrectionRate", m.UserCorrectionRate);
        cmd.Parameters.AddWithValue("@fallbackRate", m.FallbackRate);
        cmd.Parameters.AddWithValue("@avgLatencyMs", m.AvgLatencyMs);
        cmd.Parameters.AddWithValue("@avgCostUsd", m.AvgCostUsd);
        cmd.Parameters.AddWithValue("@totalCalls", m.TotalCalls);
        cmd.Parameters.AddWithValue("@safetyDenialCount", m.SafetyDenialCount);
        cmd.Parameters.AddWithValue("@fallbackCount", m.FallbackCount);
        cmd.Parameters.AddWithValue("@userCorrectionCount", m.UserCorrectionCount);
        cmd.Parameters.AddWithValue("@totalLatencyMs", m.TotalLatencyMs);
        cmd.Parameters.AddWithValue("@totalCostUsd", m.TotalCostUsd);
        cmd.Parameters.AddWithValue("@updatedAt", m.UpdatedAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    ///     Increment total calls for a skill+version atomically.
    ///     If the row doesn't exist, creates it with the given initial values.
    /// </summary>
    public async Task IncrementUsageAsync(
        string skillId,
        int version,
        bool succeeded,
        int latencyMs,
        double costUsd,
        CancellationToken ct = default)
    {
        var metrics = await LoadMetricsAsync(skillId, version, ct);
        metrics ??= new SkillQualityMetrics { SkillId = skillId, Version = version };

        metrics.TotalCalls++;

        if (metrics.TotalCalls > 0)
        {
            metrics.TotalLatencyMs += latencyMs;
            metrics.TotalCostUsd += costUsd;
            metrics.AvgLatencyMs = metrics.TotalLatencyMs / metrics.TotalCalls;
            metrics.AvgCostUsd = metrics.TotalCostUsd / metrics.TotalCalls;
        }

        if (succeeded)
            metrics.AcceptanceRate = 1.0 - ((double)metrics.FallbackCount / metrics.TotalCalls);

        await SaveMetricsAsync(metrics, ct);
    }

    /// <summary>Get all quality metrics history for a skill.</summary>
    public async Task<IReadOnlyList<SkillQualityMetrics>> GetHistoryAsync(string skillId, CancellationToken ct = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT skill_id, version, acceptance_rate, test_score, user_correction_rate,
                   fallback_rate, avg_latency_ms, avg_cost_usd, total_calls,
                   safety_denial_count, fallback_count, user_correction_count,
                   total_latency_ms, total_cost_usd, updated_at
              FROM skill_quality_metrics
             WHERE skill_id = @skillId
             ORDER BY version DESC
            """;
        cmd.Parameters.AddWithValue("@skillId", skillId);

        var results = new List<SkillQualityMetrics>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(ReadMetrics(reader));
        return results;
    }

    private static SkillQualityMetrics ReadMetrics(SqliteDataReader r)
    {
        return new SkillQualityMetrics
        {
            SkillId = r.GetString(0),
            Version = r.GetInt32(1),
            AcceptanceRate = r.GetDouble(2),
            TestScore = r.GetDouble(3),
            UserCorrectionRate = r.GetDouble(4),
            FallbackRate = r.GetDouble(5),
            AvgLatencyMs = r.GetDouble(6),
            AvgCostUsd = r.GetDouble(7),
            TotalCalls = r.GetInt32(8),
            SafetyDenialCount = r.GetInt32(9),
            FallbackCount = r.GetInt32(10),
            UserCorrectionCount = r.GetInt32(11),
            TotalLatencyMs = r.GetDouble(12),
            TotalCostUsd = r.GetDouble(13),
            UpdatedAt = r.GetString(14),
        };
    }
}
