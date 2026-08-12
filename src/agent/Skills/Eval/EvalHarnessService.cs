using Hercules.Config;
using Hercules.Storage;
using Microsoft.Extensions.Logging;

namespace Hercules.Skills.Eval;

/// <summary>
///     Eval harness: запуск оценки навыка, сравнение с baseline, regression detection.
/// </summary>
public sealed class EvalHarnessService : IEvalHarnessService
{
    private readonly BaselineManager _baselineManager;
    private readonly SkillEvaluationEngine _engine;
    private readonly FileSkillRepository _repo;
    private readonly EvalConfig _config;
    private readonly ILogger<EvalHarnessService> _logger;

    public EvalHarnessService(
        BaselineManager baselineManager,
        SkillEvaluationEngine engine,
        FileSkillRepository repo,
        EvalConfig config,
        ILogger<EvalHarnessService> logger)
    {
        _baselineManager = baselineManager ?? throw new ArgumentNullException(nameof(baselineManager));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<RegressionResult> RunHarnessAsync(string skillId, CancellationToken ct = default)
    {
        Skill? skill = _repo.Load(skillId);
        if (skill is null)
        {
            _logger.LogWarning("RunHarnessAsync: skill '{SkillId}' not found", skillId);
            return new RegressionResult
            {
                HasRegression = false,
                ScoreDelta = 0,
                PreviousScore = 0,
                CurrentScore = 0,
                EvaluatedAt = DateTime.UtcNow.ToString("o"),
                BlockedReasons = []
            };
        }

        // Загружаем test suite
        SkillTestSuite? testSuite = SkillEvaluationEngine.LoadTestSuite(skillId, _repo.SkillsDirectory);

        // Если нет test suite — генерируем deterministic fixtures
        if (testSuite is null || testSuite.Tests.Count == 0)
        {
            _logger.LogInformation(
                "No test suite for skill '{SkillId}', generating {Count} deterministic fixtures",
                skillId, _config.DefaultFixtureCount);

            var generator = new SkillTestGenerator(
                LoggerFactory.Create(b => b.AddConsole()).CreateLogger<SkillTestGenerator>());

            var fixtures = generator.GenerateDeterministicFixtures(
                skill, _config.DefaultFixtureCount);

            testSuite = new SkillTestSuite
            {
                Version = "1.0",
                Description = "Auto-generated deterministic fixtures",
                Tests = fixtures
            };
        }

        // Запускаем оценку
        var result = await _engine.EvaluateAsync(skill, testSuite, ct);

        // Сравниваем с baseline
        return CompareWithBaseline(skillId, result);
    }

    /// <inheritdoc />
    public Task<BaselineRecord> RecordBaselineAsync(
        string skillId,
        SkillEvaluationResult result,
        string? recordedBy = null,
        string? reason = null,
        CancellationToken ct = default)
    {
        Skill? skill = _repo.Load(skillId);
        var version = skill?.Meta.Version.ToString() ?? "unknown";

        var record = _baselineManager.SaveBaseline(
            skillId, version, result, recordedBy, reason);

        return Task.FromResult(record);
    }

    /// <inheritdoc />
    public RegressionResult CompareWithBaseline(string skillId, SkillEvaluationResult currentResult)
    {
        BaselineRecord? baseline = _baselineManager.LoadBaseline(skillId);

        if (baseline is null)
        {
            // Нет baseline — создаём его автоматически
            _logger.LogInformation(
                "No baseline for skill '{SkillId}', recording current result as baseline",
                skillId);

            var record = _baselineManager.SaveBaseline(
                skillId,
                "unknown",
                currentResult,
                "system",
                "auto-baseline");

            return new RegressionResult
            {
                HasRegression = false,
                ScoreDelta = 0,
                PreviousScore = currentResult.Score,
                CurrentScore = currentResult.Score,
                BaselineId = record.Id,
                BaselineRecordedAt = record.RecordedAt,
                EvaluatedAt = DateTime.UtcNow.ToString("o"),
                BlockedReasons = []
            };
        }

        double delta = currentResult.Score - baseline.Score;
        bool hasRegression = delta < -_config.BaselineComparisonThreshold;

        var blockedReasons = new List<RegressionDetail>();
        if (hasRegression && currentResult.TestResults.Count > 0 && baseline.TestResults != null)
        {
            // Сравниваем каждый тест
            var baselineMap = baseline.TestResults.ToDictionary(
                t => t.Name, t => t);

            foreach (var current in currentResult.TestResults)
            {
                if (!baselineMap.TryGetValue(current.Name, out var prev))
                {
                    continue;
                }

                double testDelta = (current.Passed ? 1.0 : 0.0) - prev.Score;
                if (testDelta < -_config.BaselineComparisonThreshold)
                {
                    blockedReasons.Add(new RegressionDetail
                    {
                        TestName = current.Name,
                        PreviousScore = prev.Score,
                        CurrentScore = current.Passed ? 1.0 : 0.0,
                        Reason = $"Test regressed: {prev.FailureReason} → {current.FailureReason}"
                    });
                }
            }
        }

        _logger.LogInformation(
            "Regression check for '{SkillId}': baseline={BaselineScore:F2}, current={CurrentScore:F2}, delta={Delta:F2}, hasRegression={HasRegression}",
            skillId, baseline.Score, currentResult.Score, delta, hasRegression);

        return new RegressionResult
        {
            HasRegression = hasRegression,
            ScoreDelta = Math.Round(delta, 4),
            PreviousScore = baseline.Score,
            CurrentScore = currentResult.Score,
            BaselineId = baseline.Id,
            BaselineRecordedAt = baseline.RecordedAt,
            EvaluatedAt = DateTime.UtcNow.ToString("o"),
            BlockedReasons = blockedReasons
        };
    }

    /// <inheritdoc />
    public BaselineRecord? GetBaseline(string skillId) => _baselineManager.LoadBaseline(skillId);

    /// <inheritdoc />
    public bool HasBaseline(string skillId) => _baselineManager.HasBaseline(skillId);

    /// <inheritdoc />
    public List<BaselineRecord> GetBaselineHistory(string? skillId = null)
    {
        var all = _baselineManager.GetAllBaselines();
        if (!string.IsNullOrEmpty(skillId))
        {
            return all.Where(b => b.SkillId == skillId).ToList();
        }
        return all;
    }
}
