using Hercules.Config;
using Hercules.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Skills.Quality.Tests;

public class SkillQualityServiceTests : IDisposable
{
    private readonly SkillQualityStore _store;
    private readonly SkillQualityService _service;
    private readonly SkillQualityConfig _config;

    public SkillQualityServiceTests()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hercules_quality_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var cfg = new StorageConfig { DataRoot = tempDir, SqliteFile = "test.db" };
        _config = new SkillQualityConfig { MinSampleSize = 3, MinScoreForPromotion = 0.40 };
        _store = new SkillQualityStore(cfg);
        _service = new SkillQualityService(_store, _config);
    }

    public void Dispose()
    {
        _store.Dispose();
    }

    // ─── RecordFallback ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RecordFallback_IncrementsCountAndRate()
    {
        await _service.RecordSuccessAsync("s1", 1, 100, 0.001);

        await _service.RecordFallbackAsync("s1", 1);

        var score = await _service.ComputeScoreAsync("s1", 1);
        Assert.Equal(2, score.TotalCalls);
        Assert.True(score.FallbackRate > 0);
        Assert.True(score.AcceptanceRate < 1.0);
    }

    [Fact]
    public async Task RecordFallback_10Falls_50Percent()
    {
        for (int i = 0; i < 10; i++)
        {
            await _service.RecordFallbackAsync("s2", 1);
        }

        var score = await _service.ComputeScoreAsync("s2", 1);
        Assert.Equal(10, score.TotalCalls);
        Assert.Equal(0.0, score.AcceptanceRate);
        Assert.Equal(1.0, score.FallbackRate);
    }

    // ─── RecordUserCorrection ───────────────────────────────────────────────────

    [Fact]
    public async Task RecordUserCorrection_TracksRate()
    {
        await _service.RecordSuccessAsync("s3", 1, 50, 0.0005);
        await _service.RecordUserCorrectionAsync("s3", 1);
        await _service.RecordUserCorrectionAsync("s3", 1);

        var score = await _service.ComputeScoreAsync("s3", 1);
        Assert.Equal(3, score.TotalCalls);
        Assert.True(score.UserCorrectionRate > 0.5);
    }

    // ─── RecordSafetyDenial ─────────────────────────────────────────────────────

    [Fact]
    public async Task RecordSafetyDenial_IncrementsCount()
    {
        await _service.RecordSafetyDenialAsync("s4", 1);
        await _service.RecordSafetyDenialAsync("s4", 1);

        var score = await _service.ComputeScoreAsync("s4", 1);
        Assert.Equal(2, score.SafetyDenialCount);
    }

    // ─── RecordSuccess ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RecordSuccess_TracksLatencyAndCost()
    {
        await _service.RecordSuccessAsync("s5", 1, 200, 0.005);
        await _service.RecordSuccessAsync("s5", 1, 300, 0.007);

        var score = await _service.ComputeScoreAsync("s5", 1);
        Assert.Equal(2, score.TotalCalls);
        Assert.Equal(1.0, score.AcceptanceRate);
        Assert.True(Math.Abs(score.AvgLatencyMs.GetValueOrDefault() - 250.0) < 1.0);
        Assert.True(score.AvgCostUsd.GetValueOrDefault() > 0.005);
    }

    // ─── ComputeScore: NoData ───────────────────────────────────────────────────

    [Fact]
    public async Task ComputeScore_NoData_ReturnsNeutral()
    {
        var score = await _service.ComputeScoreAsync("nonexistent", 99);

        Assert.Equal(1.0, score.CompositeScore);
        Assert.False(score.IsReliable);
        Assert.Contains("no data", score.Reason!);
    }

    // ─── ComputeScore: MinSampleSize gating ────────────────────────────────────

    [Fact]
    public async Task ComputeScore_BelowMinSampleSize_ReturnsNeutral()
    {
        await _service.RecordSuccessAsync("s6", 1, 100, 0.001);
        await _service.RecordSuccessAsync("s6", 1, 100, 0.001);
        // MinSampleSize = 3, we only have 2 → neutral

        var score = await _service.ComputeScoreAsync("s6", 1);

        Assert.Equal(1.0, score.CompositeScore);
        Assert.False(score.IsReliable);
        Assert.Contains("sample size 2", score.Reason!);
    }

    [Fact]
    public async Task ComputeScore_AtMinSampleSize_IsReliable()
    {
        for (int i = 0; i < 3; i++)
            await _service.RecordSuccessAsync("s7", 1, 100, 0.001);

        var score = await _service.ComputeScoreAsync("s7", 1);

        Assert.True(score.IsReliable);
        Assert.True(score.CompositeScore >= 0);
        Assert.True(score.CompositeScore <= 1);
    }

    // ─── ComputeScore: Weights ───────────────────────────────────────────────────

    [Fact]
    public async Task ComputeScore_AllPerfect_ReturnsOne()
    {
        for (int i = 0; i < 5; i++)
            await _service.RecordSuccessAsync("s8", 1, 100, 0.001);

        var score = await _service.ComputeScoreAsync("s8", 1);

        Assert.True(score.IsReliable);
        Assert.True(score.CompositeScore > 0.9);
    }

    [Fact]
    public async Task ComputeScore_AllFailing_ReturnsReflectsFallbacks()
    {
        for (int i = 0; i < 5; i++)
            await _service.RecordFallbackAsync("s9", 1);

        var score = await _service.ComputeScoreAsync("s9", 1);

        Assert.True(score.IsReliable);
        Assert.Equal(0.0, score.AcceptanceRate);
        Assert.Equal(1.0, score.FallbackRate);
        // Composite = (0*0.25 + 1*0.30 + 1*0.20 + 0*0.15 + 1*0.05 + 1*0.05) / 0.75 = 0.60
        Assert.True(score.CompositeScore < 0.85);
    }

    // ─── RecordTestScore ────────────────────────────────────────────────────────

    [Fact]
    public async Task RecordTestScore_UpdatesMetrics()
    {
        await _service.RecordSuccessAsync("s10", 1, 100, 0.001);
        await _service.RecordTestScoreAsync("s10", 1, 0.85);

        var m = await _service.GetMetricsAsync("s10", 1);
        Assert.NotNull(m);
        Assert.Equal(0.85, m!.TestScore);
    }

    // ─── GetHistory ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHistory_MultipleVersions_ReturnsAll()
    {
        await _service.RecordSuccessAsync("s11", 1, 100, 0.001);
        await _service.RecordSuccessAsync("s11", 2, 150, 0.002);

        var history = await _service.GetHistoryAsync("s11");

        Assert.Equal(2, history.Count);
    }

    // ─── Null config defaults ─────────────────────────────────────────────────────

    [Fact]
    public async Task Service_WithNullWeights_DefaultsToZeroWeight()
    {
        var nullConfig = new SkillQualityConfig
        {
            ScoreWeights = new Dictionary<string, double>(),
            MinSampleSize = 2
        };
        var svc = new SkillQualityService(_store, nullConfig);

        await svc.RecordSuccessAsync("s12", 1, 100, 0.001);
        await svc.RecordSuccessAsync("s12", 1, 100, 0.001);

        var score = await svc.ComputeScoreAsync("s12", 1);
        Assert.True(score.IsReliable);
        Assert.True(score.CompositeScore >= 0);
    }
}
