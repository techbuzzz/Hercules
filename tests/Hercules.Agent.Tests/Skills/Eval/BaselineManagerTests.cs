using Hercules.Config;
using Hercules.Skills;
using Hercules.Skills.Eval;
using Hercules.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Skills.Eval;

/// <summary>
/// Тесты BaselineManager: save/load/has/delete baseline.
/// </summary>
public class BaselineManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly BaselineManager _manager;

    public BaselineManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-baselines-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var storageCfg = new StorageConfig { DataRoot = _tempDir };
        _manager = new BaselineManager(storageCfg, NullLogger<BaselineManager>.Instance);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    [Fact]
    public void SaveBaseline_WritesJsonFile()
    {
        var skill = new Skill { Meta = new SkillMeta { Id = "test-skill-1", Name = "Test" } };
        var result = new SkillEvaluationResult
        {
            SkillId = skill.Meta.Id,
            SkillName = skill.Meta.Name,
            TotalTests = 3,
            PassedTests = 2,
            FailedTests = 1,
            Score = 0.67,
            TestResults = [],
            EvaluatedAt = DateTime.UtcNow.ToString("o")
        };

        var record = _manager.SaveBaseline(skill.Meta.Id, "v1", result, "user", "manual");

        Assert.NotNull(record);
        Assert.Equal(skill.Meta.Id, record.SkillId);
        Assert.Equal("v1", record.SkillVersion);
        Assert.Equal(0.67, record.Score);
        Assert.Equal("user", record.RecordedBy);
        Assert.Equal("manual", record.Reason);
        Assert.NotEmpty(record.Id);
    }

    [Fact]
    public void LoadBaseline_ReturnsRecord()
    {
        var skill = new Skill { Meta = new SkillMeta { Id = "test-skill-2", Name = "Test2" } };
        var result = new SkillEvaluationResult
        {
            SkillId = skill.Meta.Id,
            SkillName = skill.Meta.Name,
            TotalTests = 5,
            PassedTests = 5,
            FailedTests = 0,
            Score = 1.0,
            TestResults = [],
            EvaluatedAt = DateTime.UtcNow.ToString("o")
        };

        _manager.SaveBaseline(skill.Meta.Id, "v1", result, "system", "pre-promotion");
        var loaded = _manager.LoadBaseline(skill.Meta.Id);

        Assert.NotNull(loaded);
        Assert.Equal(skill.Meta.Id, loaded.SkillId);
        Assert.Equal(1.0, loaded.Score);
        Assert.Equal(5, loaded.TotalTests);
        Assert.Equal(5, loaded.PassedTests);
    }

    [Fact]
    public void LoadBaseline_ReturnsNull_WhenMissing()
    {
        var loaded = _manager.LoadBaseline("nonexistent-skill");
        Assert.Null(loaded);
    }

    [Fact]
    public void HasBaseline_ReturnsTrue_AfterSave()
    {
        var skill = new Skill { Meta = new SkillMeta { Id = "test-skill-3" } };
        var result = new SkillEvaluationResult
        {
            SkillId = skill.Meta.Id, SkillName = "x", TotalTests = 1, PassedTests = 1,
            FailedTests = 0, Score = 1.0, TestResults = [], EvaluatedAt = DateTime.UtcNow.ToString("o")
        };

        Assert.False(_manager.HasBaseline(skill.Meta.Id));
        _manager.SaveBaseline(skill.Meta.Id, "v1", result);
        Assert.True(_manager.HasBaseline(skill.Meta.Id));
    }

    [Fact]
    public void DeleteBaseline_RemovesFile()
    {
        var skill = new Skill { Meta = new SkillMeta { Id = "test-skill-4" } };
        var result = new SkillEvaluationResult
        {
            SkillId = skill.Meta.Id, SkillName = "x", TotalTests = 1, PassedTests = 1,
            FailedTests = 0, Score = 1.0, TestResults = [], EvaluatedAt = DateTime.UtcNow.ToString("o")
        };

        _manager.SaveBaseline(skill.Meta.Id, "v1", result);
        Assert.True(_manager.HasBaseline(skill.Meta.Id));
        var deleted = _manager.DeleteBaseline(skill.Meta.Id);
        Assert.True(deleted);
        Assert.False(_manager.HasBaseline(skill.Meta.Id));
    }

    [Fact]
    public void DeleteBaseline_ReturnsFalse_WhenMissing()
    {
        var deleted = _manager.DeleteBaseline("nonexistent");
        Assert.False(deleted);
    }

    [Fact]
    public void SaveBaseline_Overwrites_Existing()
    {
        var skill = new Skill { Meta = new SkillMeta { Id = "test-skill-5" } };
        var result1 = new SkillEvaluationResult
        {
            SkillId = skill.Meta.Id, SkillName = "x", TotalTests = 1, PassedTests = 1,
            FailedTests = 0, Score = 0.5, TestResults = [], EvaluatedAt = DateTime.UtcNow.ToString("o")
        };
        var result2 = new SkillEvaluationResult
        {
            SkillId = skill.Meta.Id, SkillName = "x", TotalTests = 1, PassedTests = 1,
            FailedTests = 0, Score = 0.9, TestResults = [], EvaluatedAt = DateTime.UtcNow.ToString("o")
        };

        _manager.SaveBaseline(skill.Meta.Id, "v1", result1);
        _manager.SaveBaseline(skill.Meta.Id, "v2", result2);

        var loaded = _manager.LoadBaseline(skill.Meta.Id);
        Assert.NotNull(loaded);
        Assert.Equal(0.9, loaded.Score);
        Assert.Equal("v2", loaded.SkillVersion);
    }

    [Fact]
    public void GetAllBaselines_ReturnsAll()
    {
        for (int i = 0; i < 3; i++)
        {
            var skill = new Skill { Meta = new SkillMeta { Id = $"skill-{i}" } };
            var result = new SkillEvaluationResult
            {
                SkillId = skill.Meta.Id, SkillName = "x", TotalTests = 1, PassedTests = 1,
                FailedTests = 0, Score = 1.0, TestResults = [], EvaluatedAt = DateTime.UtcNow.ToString("o")
            };
            _manager.SaveBaseline(skill.Meta.Id, "v1", result);
        }

        var all = _manager.GetAllBaselines();
        Assert.Equal(3, all.Count);
    }
}
