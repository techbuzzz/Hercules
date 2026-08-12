using System.Text.Encodings.Web;
using System.Text.Json;
using Hercules.Agent;
using Hercules.Storage;
using Microsoft.Extensions.Logging;

namespace Hercules.Skills;

/// <summary>
///     Результат одного тест-кейса.
/// </summary>
public sealed class SkillTestResult
{
    public required string Name { get; init; }
    public required string Input { get; init; }
    public required string Output { get; init; }
    public required string Confidence { get; init; }
    public required string Mode { get; init; }
    public required bool Passed { get; init; }
    public string? FailureReason { get; init; }
}

/// <summary>
///     Результат оценки всего набора тестов навыка.
/// </summary>
public sealed class SkillEvaluationResult
{
    public required string SkillId { get; init; }
    public required string SkillName { get; init; }
    public required int TotalTests { get; init; }
    public required int PassedTests { get; init; }
    public required int FailedTests { get; init; }
    public required double Score { get; init; }
    public required List<SkillTestResult> TestResults { get; init; }
    public required string EvaluatedAt { get; init; }
    public string? Error { get; init; }
}

/// <summary>
///     Движок оценки навыков: выполняет SkillTestSuite через AgentCore,
///     проверяет ExpectedContains, MinConfidence, ExpectedMode.
///     Таймаут на тест = 60s, общий бюджет = 120s.
/// </summary>
public sealed class SkillEvaluationEngine
{
    private readonly AgentCore _agent;
    private readonly ILogger<SkillEvaluationEngine> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    ///     Таймаут на один тест (секунды).
    /// </summary>
    public int PerTestTimeoutSeconds { get; init; } = 60;

    /// <summary>
    ///     Общий таймаут на всю оценку (секунды).
    /// </summary>
    public int TotalTimeoutSeconds { get; init; } = 120;

    public SkillEvaluationEngine(AgentCore agent, ILogger<SkillEvaluationEngine> logger)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Конструктор с явными таймаутами (для тестов).
    /// </summary>
    public SkillEvaluationEngine(AgentCore agent, ILogger<SkillEvaluationEngine> logger,
        int perTestTimeoutSeconds = 60, int totalTimeoutSeconds = 120)
        : this(agent, logger)
    {
        PerTestTimeoutSeconds = perTestTimeoutSeconds;
        TotalTimeoutSeconds = totalTimeoutSeconds;
    }

    /// <summary>
    ///     Запустить оценку навыка по test suite.
    ///     Если testSuite == null — загружает skill.tests.json из пакета (по соглашению).
    /// </summary>
    public async Task<SkillEvaluationResult> EvaluateAsync(
        Skill skill,
        SkillTestSuite? testSuite,
        CancellationToken ct = default)
    {
        if (skill is null) throw new ArgumentNullException(nameof(skill));

        var startedAt = DateTime.UtcNow;

        if (testSuite is null || testSuite.Tests.Count == 0)
        {
            return new SkillEvaluationResult
            {
                SkillId = skill.Meta.Id,
                SkillName = skill.Meta.Name,
                TotalTests = 0,
                PassedTests = 0,
                FailedTests = 0,
                Score = 0,
                TestResults = [],
                EvaluatedAt = startedAt.ToString("o"),
                Error = "Test suite пуст или не предоставлен."
            };
        }

        var testResults = new List<SkillTestResult>();
        var totalBudget = TimeSpan.FromSeconds(TotalTimeoutSeconds);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(totalBudget);

        var passed = 0;
        foreach (SkillTestCase testCase in testSuite.Tests)
        {
            if (cts.IsCancellationRequested)
            {
                _logger.LogWarning("SkillEvaluation: общий таймаут ({Total}s) reached, прекращаю",
                    TotalTimeoutSeconds);
                break;
            }

            SkillTestResult result = await RunSingleTestAsync(skill, testCase, cts.Token);
            testResults.Add(result);
            if (result.Passed) passed++;
        }

        var score = testResults.Count > 0
            ? Math.Round((double)passed / testResults.Count, 2)
            : 0.0;

        _logger.LogInformation(
            "SkillEvaluation: '{Name}' — {Passed}/{Total} passed, score={Score:P0}",
            skill.Meta.Name, passed, testResults.Count, score);

        return new SkillEvaluationResult
        {
            SkillId = skill.Meta.Id,
            SkillName = skill.Meta.Name,
            TotalTests = testResults.Count,
            PassedTests = passed,
            FailedTests = testResults.Count - passed,
            Score = score,
            TestResults = testResults,
            EvaluatedAt = startedAt.ToString("o")
        };
    }

    private async Task<SkillTestResult> RunSingleTestAsync(
        Skill skill, SkillTestCase testCase, CancellationToken ct)
    {
        using var perTestCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        perTestCts.CancelAfter(TimeSpan.FromSeconds(PerTestTimeoutSeconds));

        try
        {
            AgentResponse response = await _agent.EvaluateSkillAsync(skill, testCase.Input, perTestCts.Token);

            string? failureReason = null;
            bool passed = true;

            // 1. Проверка ExpectedContains
            if (!string.IsNullOrWhiteSpace(testCase.ExpectedContains))
            {
                if (!response.Answer.Contains(testCase.ExpectedContains, StringComparison.OrdinalIgnoreCase))
                {
                    failureReason = $"ExpectedContains не найден: '{testCase.ExpectedContains}'";
                    passed = false;
                }
            }

            // 2. Проверка MinConfidence
            if (passed && !string.IsNullOrWhiteSpace(testCase.MinConfidence))
            {
                var level = ConfidenceLevel(testCase.MinConfidence);
                var actual = ConfidenceLevel(response.Confidence);
                if (actual < level)
                {
                    failureReason = $"Confidence too low: expected >={testCase.MinConfidence}, got {response.Confidence}";
                    passed = false;
                }
            }

            // 3. Проверка ExpectedMode
            if (passed && !string.IsNullOrWhiteSpace(testCase.ExpectedMode))
            {
                if (!string.Equals(response.Mode, testCase.ExpectedMode, StringComparison.OrdinalIgnoreCase))
                {
                    failureReason = $"Mode mismatch: expected={testCase.ExpectedMode}, got={response.Mode}";
                    passed = false;
                }
            }

            return new SkillTestResult
            {
                Name = testCase.Name,
                Input = testCase.Input,
                Output = response.Answer,
                Confidence = response.Confidence,
                Mode = response.Mode,
                Passed = passed,
                FailureReason = failureReason
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new SkillTestResult
            {
                Name = testCase.Name,
                Input = testCase.Input,
                Output = "",
                Confidence = "low",
                Mode = "unknown",
                Passed = false,
                FailureReason = $"Test timed out after {PerTestTimeoutSeconds}s."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SkillEvaluation: test '{Name}' threw", testCase.Name);
            return new SkillTestResult
            {
                Name = testCase.Name,
                Input = testCase.Input,
                Output = "",
                Confidence = "low",
                Mode = "error",
                Passed = false,
                FailureReason = $"Exception: {ex.Message}"
            };
        }
    }

    /// <summary>
    ///     Загрузить test suite из файла skill.tests.json в директории навыков.
    /// </summary>
    public static SkillTestSuite? LoadTestSuite(string skillId, string skillsDirectory)
    {
        var path = Path.Combine(skillsDirectory, $"skill.{skillId}.tests.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SkillTestSuite>(json, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    private static int ConfidenceLevel(string confidence) =>
        confidence.ToLowerInvariant() switch
        {
            "high" => 3,
            "medium" => 2,
            "low" => 1,
            _ => 0
        };
}
