using System.Runtime.CompilerServices;
using Hercules.Agent;
using Hercules.Config;
using Hercules.LLM;
using Hercules.LLM.JsonRepair;
using Hercules.Skills;
using Hercules.Skills.Eval;
using Hercules.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Skills.Eval;

/// <summary>
/// Тесты EvalHarnessService: regression detection, baseline comparison.
/// </summary>
public class EvalHarnessServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSkillRepository _repo;
    private readonly SkillManager _skillManager;
    private readonly SkillEvaluationEngine _engine;
    private readonly BaselineManager _baselineManager;
    private readonly EvalConfig _evalConfig;
    private readonly EvalHarnessService _harness;

    public EvalHarnessServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-harness-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var storageCfg = new StorageConfig { DataRoot = _tempDir };
        _repo = new FileSkillRepository(storageCfg, NullLogger<FileSkillRepository>.Instance);
        _skillManager = new SkillManager(_repo, new StubLlmClient("test"), new AgentConfig(), new JsonRepairService());

        var sessions = new SqliteSessionStore(storageCfg);
        var router = new SkillRouter(_skillManager);
        var memory = new MemoryManager(new MemoryStore(storageCfg), new StubLlmClient("memory"));
        var agent = new AgentCore(
            new StubLlmClient("answer [confidence: high]"),
            router, _skillManager, memory, sessions,
            new AgentConfig(), NullLogger<AgentCore>.Instance,
            new JsonRepairService());

        _engine = new SkillEvaluationEngine(agent, NullLogger<SkillEvaluationEngine>.Instance);
        _baselineManager = new BaselineManager(storageCfg, NullLogger<BaselineManager>.Instance);
        _evalConfig = new EvalConfig { BlockOnRegression = true, BaselineComparisonThreshold = 0.05 };
        _harness = new EvalHarnessService(
            _baselineManager, _engine, _repo, _evalConfig,
            NullLogger<EvalHarnessService>.Instance);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    [Fact]
    public void CompareWithBaseline_NoBaseline_CreatesBaseline()
    {
        var skill = _skillManager.CreateManual("test-no-baseline", ["test"], "Тестовый навык.");
        var result = new SkillEvaluationResult
        {
            SkillId = skill.Meta.Id,
            SkillName = skill.Meta.Name,
            TotalTests = 2,
            PassedTests = 2,
            FailedTests = 0,
            Score = 0.8,
            TestResults = [],
            EvaluatedAt = DateTime.UtcNow.ToString("o")
        };

        var regression = _harness.CompareWithBaseline(skill.Meta.Id, result);

        Assert.False(regression.HasRegression);
        Assert.Equal(0.8, regression.PreviousScore);
        Assert.Equal(0.8, regression.CurrentScore);
        Assert.NotNull(regression.BaselineId);
        Assert.NotNull(_harness.GetBaseline(skill.Meta.Id));
    }

    [Fact]
    public void CompareWithBaseline_NoRegression_ReturnsFalse()
    {
        var skill = _skillManager.CreateManual("test-no-reg", ["noreg"], "Навык без регрессии.");
        var result1 = new SkillEvaluationResult
        {
            SkillId = skill.Meta.Id, SkillName = skill.Meta.Name, TotalTests = 2, PassedTests = 2,
            FailedTests = 0, Score = 0.7, TestResults = [], EvaluatedAt = DateTime.UtcNow.ToString("o")
        };
        _harness.RecordBaselineAsync(skill.Meta.Id, result1, "user", "pre-test");

        var result2 = new SkillEvaluationResult
        {
            SkillId = skill.Meta.Id, SkillName = skill.Meta.Name, TotalTests = 2, PassedTests = 2,
            FailedTests = 0, Score = 0.75, TestResults = [], EvaluatedAt = DateTime.UtcNow.ToString("o")
        };

        var regression = _harness.CompareWithBaseline(skill.Meta.Id, result2);

        Assert.False(regression.HasRegression);
        Assert.Equal(0.7, regression.PreviousScore);
        Assert.Equal(0.75, regression.CurrentScore);
        Assert.True(regression.ScoreDelta > 0);
    }

    [Fact]
    public void CompareWithBaseline_Regression_DetectsDrop()
    {
        var skill = _skillManager.CreateManual("test-reg", ["reg"], "Навык с регрессией.");
        var result1 = new SkillEvaluationResult
        {
            SkillId = skill.Meta.Id, SkillName = skill.Meta.Name, TotalTests = 2, PassedTests = 2,
            FailedTests = 0, Score = 0.8, TestResults = [], EvaluatedAt = DateTime.UtcNow.ToString("o")
        };
        _harness.RecordBaselineAsync(skill.Meta.Id, result1, "user", "pre-test");

        var result2 = new SkillEvaluationResult
        {
            SkillId = skill.Meta.Id, SkillName = skill.Meta.Name, TotalTests = 2, PassedTests = 0,
            FailedTests = 2, Score = 0.0, TestResults = [], EvaluatedAt = DateTime.UtcNow.ToString("o")
        };

        var regression = _harness.CompareWithBaseline(skill.Meta.Id, result2);

        Assert.True(regression.HasRegression);
        Assert.Equal(0.8, regression.PreviousScore);
        Assert.Equal(0.0, regression.CurrentScore);
        Assert.True(regression.ScoreDelta < -0.05);
    }

    [Fact]
    public void CompareWithBaseline_SmallDrop_BelowThreshold_NoRegression()
    {
        var skill = _skillManager.CreateManual("test-small-drop", ["drop"], "Навык с небольшим падением.");
        var result1 = new SkillEvaluationResult
        {
            SkillId = skill.Meta.Id, SkillName = skill.Meta.Name, TotalTests = 4, PassedTests = 4,
            FailedTests = 0, Score = 0.75, TestResults = [], EvaluatedAt = DateTime.UtcNow.ToString("o")
        };
        _harness.RecordBaselineAsync(skill.Meta.Id, result1, "user", "pre-test");

        // Drop of 0.03, threshold is 0.05 — should NOT be flagged as regression
        var result2 = new SkillEvaluationResult
        {
            SkillId = skill.Meta.Id, SkillName = skill.Meta.Name, TotalTests = 4, PassedTests = 3,
            FailedTests = 1, Score = 0.72, TestResults = [], EvaluatedAt = DateTime.UtcNow.ToString("o")
        };

        var regression = _harness.CompareWithBaseline(skill.Meta.Id, result2);

        Assert.False(regression.HasRegression);
        Assert.True(regression.ScoreDelta < 0);
        Assert.True(regression.ScoreDelta >= -0.05);
    }

    [Fact]
    public void HasBaseline_ReturnsTrue()
    {
        var skill = _skillManager.CreateManual("test-has-baseline", ["has"], "Навык с baseline.");
        var result = new SkillEvaluationResult
        {
            SkillId = skill.Meta.Id, SkillName = skill.Meta.Name, TotalTests = 1, PassedTests = 1,
            FailedTests = 0, Score = 1.0, TestResults = [], EvaluatedAt = DateTime.UtcNow.ToString("o")
        };
        _harness.RecordBaselineAsync(skill.Meta.Id, result, "user", "manual");

        Assert.True(_harness.HasBaseline(skill.Meta.Id));
    }

    [Fact]
    public void HasBaseline_ReturnsFalse_ForMissing()
    {
        Assert.False(_harness.HasBaseline("nonexistent-skill-id"));
    }

    [Fact]
    public void GetBaselineHistory_ReturnsAll()
    {
        for (int i = 0; i < 2; i++)
        {
            var skill = _skillManager.CreateManual($"test-history-{i}", [$"hist{i}"], "Навык.");
            var result = new SkillEvaluationResult
            {
                SkillId = skill.Meta.Id, SkillName = skill.Meta.Name, TotalTests = 1, PassedTests = 1,
                FailedTests = 0, Score = 1.0, TestResults = [], EvaluatedAt = DateTime.UtcNow.ToString("o")
            };
            _harness.RecordBaselineAsync(skill.Meta.Id, result);
        }

        var history = _harness.GetBaselineHistory();
        Assert.True(history.Count >= 2);
    }

    [Fact]
    public void GetBaselineHistory_FiltersBySkillId()
    {
        var skill = _skillManager.CreateManual("test-filter", ["filter"], "Навык для фильтра.");
        var result = new SkillEvaluationResult
        {
            SkillId = skill.Meta.Id, SkillName = skill.Meta.Name, TotalTests = 1, PassedTests = 1,
            FailedTests = 0, Score = 1.0, TestResults = [], EvaluatedAt = DateTime.UtcNow.ToString("o")
        };
        _harness.RecordBaselineAsync(skill.Meta.Id, result);

        var history = _harness.GetBaselineHistory(skill.Meta.Id);
        Assert.Single(history);
        Assert.Equal(skill.Meta.Id, history[0].SkillId);
    }

    [Fact]
    public void CompareWithBaseline_SameScore_NoRegression()
    {
        var skill = _skillManager.CreateManual("test-same", ["same"], "Навык с тем же скором.");
        var result1 = new SkillEvaluationResult
        {
            SkillId = skill.Meta.Id, SkillName = skill.Meta.Name, TotalTests = 3, PassedTests = 2,
            FailedTests = 1, Score = 0.67, TestResults = [], EvaluatedAt = DateTime.UtcNow.ToString("o")
        };
        _harness.RecordBaselineAsync(skill.Meta.Id, result1);

        var result2 = new SkillEvaluationResult
        {
            SkillId = skill.Meta.Id, SkillName = skill.Meta.Name, TotalTests = 3, PassedTests = 2,
            FailedTests = 1, Score = 0.67, TestResults = [], EvaluatedAt = DateTime.UtcNow.ToString("o")
        };

        var regression = _harness.CompareWithBaseline(skill.Meta.Id, result2);

        Assert.False(regression.HasRegression);
        Assert.Equal(0.0, regression.ScoreDelta);
    }

    // ---- Stub implementations ----

    private sealed class StubLlmClient : ILLMClient
    {
        private readonly string _responseText;
        public StubLlmClient(string responseText) => _responseText = responseText;
        public string ProviderName => "stub";
        public string ModelName => "stub-model";
        public Task<LlmResponse> CompleteAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default) =>
            Task.FromResult(new LlmResponse(_responseText, ProviderName, ModelName));
        public Task<LlmResponse> CompleteAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default) =>
            CompleteAsync(Roles.Main, messages, ct);
        public IAsyncEnumerable<string> StreamAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default) =>
            StreamAsync(messages, ct);
        public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatTurn> messages,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return _responseText;
        }
    }
}
