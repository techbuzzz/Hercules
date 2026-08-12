using Hercules.Config;
using Hercules.Skills.Quality;
using Hercules.Storage;

namespace Hercules.Skills.Quality;

/// <summary>
///     Interface for skill quality metric tracking and scoring.
///     Task 029: Skill Quality Score.
/// </summary>
public interface ISkillQualityService
{
    /// <summary>Record a tool fallback event.</summary>
    Task RecordFallbackAsync(string skillId, int version, CancellationToken ct = default);

    /// <summary>Record a user correction event.</summary>
    Task RecordUserCorrectionAsync(string skillId, int version, CancellationToken ct = default);

    /// <summary>Record a safety/approval denial event.</summary>
    Task RecordSafetyDenialAsync(string skillId, int version, CancellationToken ct = default);

    /// <summary>Record latency and cost for a skill call.</summary>
    Task RecordLatencySampleAsync(string skillId, int version, int latencyMs, double costUsd, CancellationToken ct = default);

    /// <summary>Record a successful skill invocation.</summary>
    Task RecordSuccessAsync(string skillId, int version, int latencyMs, double costUsd, CancellationToken ct = default);

    /// <summary>Record an eval harness result as the test score.</summary>
    Task RecordTestScoreAsync(string skillId, int version, double testScore, CancellationToken ct = default);

    /// <summary>Compute the composite quality score for a skill+version.</summary>
    Task<SkillQualityScore> ComputeScoreAsync(string skillId, int version, CancellationToken ct = default);

    /// <summary>Get current metrics for a skill+version.</summary>
    Task<SkillQualityMetrics?> GetMetricsAsync(string skillId, int version, CancellationToken ct = default);

    /// <summary>Get all quality history for a skill.</summary>
    Task<IReadOnlyList<SkillQualityMetrics>> GetHistoryAsync(string skillId, CancellationToken ct = default);

    /// <summary>Sync computed metrics to SkillMeta (SuccessRate, LastEvaluationScore).</summary>
    Task UpdateSkillMetaAsync(string skillId, int version, FileSkillRepository repo, CancellationToken ct = default);
}

/// <summary>
///     Default implementation of ISkillQualityService.
///     Computes a weighted composite score from per-version quality metrics.
/// </summary>
public sealed class SkillQualityService : ISkillQualityService
{
    private readonly SkillQualityStore _store;
    private readonly SkillQualityConfig _config;

    public SkillQualityService(SkillQualityStore store, SkillQualityConfig config)
    {
        _store = store;
        _config = config;
    }

    public async Task RecordFallbackAsync(string skillId, int version, CancellationToken ct = default)
    {
        var m = await _store.LoadMetricsAsync(skillId, version, ct)
                   ?? new SkillQualityMetrics { SkillId = skillId, Version = version };

        m.FallbackCount++;
        m.TotalCalls++;
        m.FallbackRate = (double)m.FallbackCount / m.TotalCalls;
        m.AcceptanceRate = 1.0 - m.FallbackRate;

        if (m.TotalCalls > 0)
        {
            m.AvgLatencyMs = m.TotalLatencyMs / m.TotalCalls;
            m.AvgCostUsd = m.TotalCostUsd / m.TotalCalls;
        }

        await _store.SaveMetricsAsync(m, ct);
    }

    public async Task RecordUserCorrectionAsync(string skillId, int version, CancellationToken ct = default)
    {
        var m = await _store.LoadMetricsAsync(skillId, version, ct)
                   ?? new SkillQualityMetrics { SkillId = skillId, Version = version };

        m.UserCorrectionCount++;
        m.TotalCalls++;
        m.UserCorrectionRate = (double)m.UserCorrectionCount / m.TotalCalls;

        await _store.SaveMetricsAsync(m, ct);
    }

    public async Task RecordSafetyDenialAsync(string skillId, int version, CancellationToken ct = default)
    {
        var m = await _store.LoadMetricsAsync(skillId, version, ct)
                   ?? new SkillQualityMetrics { SkillId = skillId, Version = version };

        m.SafetyDenialCount++;
        await _store.SaveMetricsAsync(m, ct);
    }

    public async Task RecordLatencySampleAsync(string skillId, int version, int latencyMs, double costUsd, CancellationToken ct = default)
    {
        var m = await _store.LoadMetricsAsync(skillId, version, ct)
                   ?? new SkillQualityMetrics { SkillId = skillId, Version = version };

        m.TotalCalls++;
        m.TotalLatencyMs += latencyMs;
        m.TotalCostUsd += costUsd;
        m.AvgLatencyMs = m.TotalLatencyMs / m.TotalCalls;
        m.AvgCostUsd = m.TotalCostUsd / m.TotalCalls;

        await _store.SaveMetricsAsync(m, ct);
    }

    public async Task RecordSuccessAsync(string skillId, int version, int latencyMs, double costUsd, CancellationToken ct = default)
    {
        var m = await _store.LoadMetricsAsync(skillId, version, ct)
                   ?? new SkillQualityMetrics { SkillId = skillId, Version = version };

        m.TotalCalls++;
        m.TotalLatencyMs += latencyMs;
        m.TotalCostUsd += costUsd;
        m.AvgLatencyMs = m.TotalLatencyMs / m.TotalCalls;
        m.AvgCostUsd = m.TotalCostUsd / m.TotalCalls;
        m.AcceptanceRate = 1.0 - ((double)m.FallbackCount / m.TotalCalls);

        await _store.SaveMetricsAsync(m, ct);
    }

    public async Task RecordTestScoreAsync(string skillId, int version, double testScore, CancellationToken ct = default)
    {
        var m = await _store.LoadMetricsAsync(skillId, version, ct)
                   ?? new SkillQualityMetrics { SkillId = skillId, Version = version };

        m.TestScore = testScore;
        await _store.SaveMetricsAsync(m, ct);
    }

    public async Task<SkillQualityMetrics?> GetMetricsAsync(string skillId, int version, CancellationToken ct = default)
    {
        return await _store.LoadMetricsAsync(skillId, version, ct);
    }

    public async Task<IReadOnlyList<SkillQualityMetrics>> GetHistoryAsync(string skillId, CancellationToken ct = default)
    {
        return await _store.GetHistoryAsync(skillId, ct);
    }

    public async Task<SkillQualityScore> ComputeScoreAsync(string skillId, int version, CancellationToken ct = default)
    {
        var m = await _store.LoadMetricsAsync(skillId, version, ct);

        if (m is null)
        {
            return new SkillQualityScore
            {
                SkillId = skillId,
                Version = version,
                CompositeScore = 1.0,
                IsReliable = false,
                Reason = "no data (neutral 1.0)",
            };
        }

        // Always return current metrics; gate only the CompositeScore behind MinSampleSize
        bool isReliable = m.TotalCalls >= _config.MinSampleSize;

        if (!isReliable)
        {
            return new SkillQualityScore
            {
                SkillId = skillId,
                Version = version,
                CompositeScore = 1.0,
                IsReliable = false,
                Reason = $"sample size {m.TotalCalls} < {_config.MinSampleSize} (neutral 1.0)",
                AcceptanceRate = m.AcceptanceRate,
                TestScore = m.TestScore > 0 ? m.TestScore : null,
                UserCorrectionRate = m.UserCorrectionRate,
                FallbackRate = m.FallbackRate,
                AvgLatencyMs = m.AvgLatencyMs,
                AvgCostUsd = m.AvgCostUsd,
                TotalCalls = m.TotalCalls,
                SafetyDenialCount = m.SafetyDenialCount,
            };
        }

        // Fetch weights; default to 0 if key missing
        var w = _config.ScoreWeights;

        double acceptanceRate    = w.GetValueOrDefault("acceptanceRate",     0.0);
        double testScore        = w.GetValueOrDefault("testScore",          0.0);
        double userCorrection   = w.GetValueOrDefault("userCorrectionRate",  0.0);
        double fallback         = w.GetValueOrDefault("fallbackRate",       0.0);
        double latency          = w.GetValueOrDefault("latency",           0.0);
        double cost             = w.GetValueOrDefault("cost",               0.0);

        double totalWeight = acceptanceRate + testScore + userCorrection + fallback + latency + cost;

        // Component scores (all 0..1, normalized by inverses where appropriate)
        double accScore = m.AcceptanceRate;                           // higher = better
        double testS    = m.TestScore > 0 ? m.TestScore : 1.0;       // 1.0 = no eval
        double corrScore = 1.0 - m.UserCorrectionRate;               // lower corrections = better
        double fallScore = 1.0 - m.FallbackRate;                      // lower fallback = better

        // Latency score: normalize relative to best observed (lower = better)
        double latScore = m.AvgLatencyMs > 0
            ? Math.Max(0, 1.0 - (m.AvgLatencyMs / 10_000.0))        // penalty starts > 10s
            : 1.0;

        // Cost score: normalize relative to max acceptable (lower = better)
        double costScore = m.AvgCostUsd > 0
            ? Math.Max(0, 1.0 - (m.AvgCostUsd / 1.0))               // penalty starts > $1
            : 1.0;

        double composite = totalWeight > 0
            ? (accScore * acceptanceRate
             + testS * testScore
             + corrScore * userCorrection
             + fallScore * fallback
             + latScore * latency
             + costScore * cost) / totalWeight
            : 1.0;

        composite = Math.Clamp(composite, 0.0, 1.0);

        return new SkillQualityScore
        {
            SkillId = skillId,
            Version = version,
            CompositeScore = composite,
            IsReliable = true,
            Reason = null,
            AcceptanceRate = m.AcceptanceRate,
            TestScore = m.TestScore > 0 ? m.TestScore : null,
            UserCorrectionRate = m.UserCorrectionRate,
            FallbackRate = m.FallbackRate,
            AvgLatencyMs = m.AvgLatencyMs,
            AvgCostUsd = m.AvgCostUsd,
            TotalCalls = m.TotalCalls,
            SafetyDenialCount = m.SafetyDenialCount,
        };
    }

    public async Task UpdateSkillMetaAsync(string skillId, int version, FileSkillRepository repo, CancellationToken ct = default)
    {
        var score = await ComputeScoreAsync(skillId, version, ct);
        if (!score.IsReliable) return;

        try
        {
            var skill = repo.Load(skillId);
            if (skill is null) return;

            // Map composite score → SuccessRate (0..1)
            skill.Meta.SuccessRate = score.CompositeScore;
            if (score.TestScore.HasValue && score.TestScore.Value > 0)
                skill.Meta.LastEvaluationScore = score.TestScore.Value;

            repo.Save(skill);
        }
        catch
        {
            // Non-critical: skill meta update should never break core flow
        }
    }
}
