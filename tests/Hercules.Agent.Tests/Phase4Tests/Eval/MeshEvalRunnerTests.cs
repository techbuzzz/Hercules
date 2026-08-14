using Hercules.Config;
using Hercules.Mesh.Eval;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Phase4Tests.Eval;

/// <summary>
///     Unit tests for the Mesh Evaluation Runner (task_052).
/// </summary>
public class MeshEvalRunnerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly MeshEvalConfig _config;
    private readonly MeshEvalRunner _runner;

    public MeshEvalRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-eval-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _config = new MeshEvalConfig
        {
            Enabled = true,
            ScenariosDir = Path.Combine(_tempDir, "scenarios"),
            ResultsDir = Path.Combine(_tempDir, "results"),
            MaxScenarioDurationSeconds = 30,
            MinSuccessRateThreshold = 0.7,
            MaxLatencyThresholdMs = 5000,
            MaxCostPerRequestUsd = 0.15m,
            BlockOnRegression = true,
            EnableChaosTesting = false
        };

        Directory.CreateDirectory(_config.ScenariosDir);
        Directory.CreateDirectory(_config.ResultsDir);

        _runner = new MeshEvalRunner(_config, NullLogger<MeshEvalRunner>.Instance);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { }
    }

    // =====================================================================
    // Configuration tests
    // =====================================================================

    [Fact]
    public void Config_Defaults_HaveReasonableValues()
    {
        var defaultConfig = new MeshEvalConfig();

        Assert.False(defaultConfig.Enabled);
        Assert.Equal("data/mesh-eval/scenarios", defaultConfig.ScenariosDir);
        Assert.Equal("data/mesh-eval/results", defaultConfig.ResultsDir);
        Assert.Equal(120, defaultConfig.MaxScenarioDurationSeconds);
        Assert.True(defaultConfig.EnableTaskSuccessEval);
        Assert.True(defaultConfig.EnableSafetyDenialEval);
        Assert.True(defaultConfig.EnableRoutingQualityEval);
        Assert.True(defaultConfig.EnableLatencyEval);
        Assert.True(defaultConfig.EnableCostEval);
        Assert.True(defaultConfig.EnableResilienceEval);
        Assert.True(defaultConfig.EnableDegradationEval);
        Assert.Equal(0.8, defaultConfig.MinSuccessRateThreshold);
        Assert.Equal(5000, defaultConfig.MaxLatencyThresholdMs);
        Assert.Equal(0.10m, defaultConfig.MaxCostPerRequestUsd);
        Assert.True(defaultConfig.BlockOnRegression);
        Assert.Equal(0.05, defaultConfig.RegressionThreshold);
        Assert.False(defaultConfig.EnableChaosTesting);
    }

    [Fact]
    public void Config_CanSetAllProperties()
    {
        var config = new MeshEvalConfig
        {
            Enabled = true,
            ScenariosDir = "/custom/scenarios",
            ResultsDir = "/custom/results",
            MaxScenarioDurationSeconds = 60,
            EnableTaskSuccessEval = false,
            EnableSafetyDenialEval = false,
            EnableRoutingQualityEval = false,
            EnableLatencyEval = false,
            EnableCostEval = false,
            EnableResilienceEval = false,
            EnableDegradationEval = false,
            EnableChaosTesting = true,
            ChaosInjectionRate = 0.25,
            MinSuccessRateThreshold = 0.9,
            MaxLatencyThresholdMs = 3000,
            MaxCostPerRequestUsd = 0.05m,
            BlockOnRegression = false,
            RegressionThreshold = 0.1
        };

        Assert.True(config.Enabled);
        Assert.Equal("/custom/scenarios", config.ScenariosDir);
        Assert.Equal("/custom/results", config.ResultsDir);
        Assert.Equal(60, config.MaxScenarioDurationSeconds);
        Assert.False(config.EnableTaskSuccessEval);
        Assert.False(config.EnableSafetyDenialEval);
        Assert.False(config.EnableRoutingQualityEval);
        Assert.False(config.EnableLatencyEval);
        Assert.False(config.EnableCostEval);
        Assert.False(config.EnableResilienceEval);
        Assert.False(config.EnableDegradationEval);
        Assert.True(config.EnableChaosTesting);
        Assert.Equal(0.25, config.ChaosInjectionRate);
        Assert.Equal(0.9, config.MinSuccessRateThreshold);
        Assert.Equal(3000, config.MaxLatencyThresholdMs);
        Assert.Equal(0.05m, config.MaxCostPerRequestUsd);
        Assert.False(config.BlockOnRegression);
        Assert.Equal(0.1, config.RegressionThreshold);
    }

    // =====================================================================
    // Scenario loading tests
    // =====================================================================

    [Fact]
    public async Task LoadScenariosAsync_CreatesDefaultScenarios_WhenDirectoryEmpty()
    {
        var scenarios = await _runner.LoadScenariosAsync();

        Assert.NotEmpty(scenarios);
        Assert.Contains(scenarios, s => s.Type == MeshEvalScenarioType.TaskSuccess);
        Assert.Contains(scenarios, s => s.Type == MeshEvalScenarioType.Latency);
        Assert.Contains(scenarios, s => s.Type == MeshEvalScenarioType.Cost);
    }

    [Fact]
    public async Task LoadScenariosAsync_LoadsCustomScenario()
    {
        var scenario = new MeshEvalScenario
        {
            Id = "custom-test",
            Name = "Custom Test",
            Type = MeshEvalScenarioType.TaskSuccess,
            Intent = "Test custom scenario"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(scenario, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
        var filePath = Path.Combine(_config.ScenariosDir, "custom-test.json");
        await File.WriteAllTextAsync(filePath, json);

        var scenarios = await _runner.LoadScenariosAsync();

        Assert.Contains(scenarios, s => s.Id == "custom-test");
    }

    [Fact]
    public async Task LoadScenariosAsync_SkipsInvalidFiles()
    {
        var invalidPath = Path.Combine(_config.ScenariosDir, "invalid.json");
        await File.WriteAllTextAsync(invalidPath, "{ invalid json }");

        var scenarios = await _runner.LoadScenariosAsync();

        Assert.DoesNotContain(scenarios, s => s.Id == "invalid.json");
    }

    // =====================================================================
    // Scenario execution tests
    // =====================================================================

    [Fact]
    public async Task RunScenarioAsync_TaskSuccess_SetsCorrectMetrics()
    {
        var scenario = new MeshEvalScenario
        {
            Id = "test-task-success",
            Name = "Test Task Success",
            Type = MeshEvalScenarioType.TaskSuccess,
            Intent = "Test intent"
        };

        var result = await _runner.RunScenarioAsync(scenario);

        Assert.Equal("test-task-success", result.ScenarioId);
        Assert.Equal(MeshEvalScenarioType.TaskSuccess, result.Type);
        Assert.True(result.DurationMs >= 0);
        Assert.True(result.Metrics.TotalAttempts > 0);
        Assert.True(result.Metrics.SuccessRate >= 0 && result.Metrics.SuccessRate <= 1);
    }

    [Fact]
    public async Task RunScenarioAsync_Latency_SetsLatencyMetrics()
    {
        var scenario = new MeshEvalScenario
        {
            Id = "test-latency",
            Name = "Test Latency",
            Type = MeshEvalScenarioType.Latency,
            Intent = "Test latency"
        };

        var result = await _runner.RunScenarioAsync(scenario);

        Assert.True(result.Metrics.AvgLatencyMs > 0);
        Assert.True(result.Metrics.MinLatencyMs > 0);
        Assert.True(result.Metrics.MaxLatencyMs >= result.Metrics.MinLatencyMs);
        Assert.True(result.Metrics.LatencyP50Ms > 0);
        Assert.True(result.Metrics.LatencyP95Ms >= result.Metrics.LatencyP50Ms);
    }

    [Fact]
    public async Task RunScenarioAsync_Cost_SetsCostMetrics()
    {
        var scenario = new MeshEvalScenario
        {
            Id = "test-cost",
            Name = "Test Cost",
            Type = MeshEvalScenarioType.Cost,
            Intent = "Test cost"
        };

        var result = await _runner.RunScenarioAsync(scenario);

        Assert.True(result.Metrics.CostPerRequestUsd >= 0);
        Assert.True(result.Metrics.TotalCostUsd >= 0);
    }

    [Fact]
    public async Task RunScenarioAsync_Resilience_SetsRetryMetrics()
    {
        var scenario = new MeshEvalScenario
        {
            Id = "test-resilience",
            Name = "Test Resilience",
            Type = MeshEvalScenarioType.Resilience,
            Intent = "Test resilience"
        };

        var result = await _runner.RunScenarioAsync(scenario);

        Assert.True(result.RetryCount >= 0);
        Assert.True(result.CircuitBreakerTriggeredCount >= 0);
        Assert.True(result.Metrics.RetrySuccessRate >= 0 && result.Metrics.RetrySuccessRate <= 1);
    }

    [Fact]
    public async Task RunScenarioAsync_Degradation_SetsDegradationMetrics()
    {
        var scenario = new MeshEvalScenario
        {
            Id = "test-degradation",
            Name = "Test Degradation",
            Type = MeshEvalScenarioType.Degradation,
            Intent = "Test degradation"
        };

        var result = await _runner.RunScenarioAsync(scenario);

        Assert.True(result.Metrics.DegradedResponses >= 0);
        Assert.True(result.Metrics.GracefulFallbackRate >= 0 && result.Metrics.GracefulFallbackRate <= 1);
    }

    [Fact]
    public async Task RunScenarioAsync_ChaosDisabled_SkipsChaos()
    {
        var scenario = new MeshEvalScenario
        {
            Id = "test-chaos",
            Name = "Test Chaos",
            Type = MeshEvalScenarioType.Chaos,
            Intent = "Test chaos"
        };

        var result = await _runner.RunScenarioAsync(scenario);

        Assert.Equal(0, result.Metrics.ChaosInjections);
        Assert.True(result.Passed);
    }

    // =====================================================================
    // Suite execution tests
    // =====================================================================

    [Fact]
    public async Task RunAllAsync_ExecutesAllScenarios()
    {
        var result = await _runner.RunAllAsync();

        Assert.NotEmpty(result.Results);
        Assert.NotEmpty(result.RunId);
        Assert.True(result.DurationSeconds >= 0);
        Assert.True(result.PassedCount + result.FailedCount == result.Results.Count);
        Assert.Equal(result.Results.Count, result.PassedCount + result.FailedCount);
    }

    [Fact]
    public async Task RunByTypeAsync_FiltersCorrectly()
    {
        var result = await _runner.RunByTypeAsync(MeshEvalScenarioType.Latency);

        Assert.All(result.Results, r => Assert.Equal(MeshEvalScenarioType.Latency, r.Type));
    }

    // =====================================================================
    // Result persistence tests
    // =====================================================================

    [Fact]
    public async Task SaveResultAsync_SavesToFile()
    {
        var suiteResult = new MeshEvalSuiteResult
        {
            RunId = "test-run",
            StartTimeUtc = DateTime.UtcNow,
            EndTimeUtc = DateTime.UtcNow,
            Results = new List<MeshEvalResult>
            {
                new()
                {
                    ScenarioId = "test",
                    ScenarioName = "Test",
                    Type = MeshEvalScenarioType.TaskSuccess,
                    StartTimeUtc = DateTime.UtcNow,
                    EndTimeUtc = DateTime.UtcNow,
                    Passed = true
                }
            }
        };

        await _runner.SaveResultAsync(suiteResult);

        var latestPath = Path.Combine(_config.ResultsDir, "latest.json");
        Assert.True(File.Exists(latestPath));
    }

    [Fact]
    public async Task LoadBaselineAsync_ReturnsNull_WhenNoBaseline()
    {
        var baseline = await _runner.LoadBaselineAsync();

        Assert.Null(baseline);
    }

    [Fact]
    public async Task LoadBaselineAsync_LoadsExistingBaseline()
    {
        // Create baseline
        var baseline = new MeshEvalSuiteResult
        {
            RunId = "baseline-run",
            StartTimeUtc = DateTime.UtcNow.AddDays(-1),
            EndTimeUtc = DateTime.UtcNow.AddDays(-1),
            Results = new List<MeshEvalResult>()
        };

        await _runner.SaveResultAsync(baseline);

        // Rename to baseline.json
        var baselinePath = Path.Combine(_config.ResultsDir, "baseline.json");
        File.Copy(Path.Combine(_config.ResultsDir, "latest.json"), baselinePath, true);

        var loaded = await _runner.LoadBaselineAsync();

        Assert.NotNull(loaded);
        Assert.Equal("baseline-run", loaded.RunId);
    }

    // =====================================================================
    // Regression comparison tests
    // =====================================================================

    [Fact]
    public void CompareWithBaseline_CalculatesRegression()
    {
        var current = new MeshEvalSuiteResult
        {
            RunId = "current",
            Results = new List<MeshEvalResult>
            {
                new() { Passed = true },
                new() { Passed = true },
                new() { Passed = false },
                new() { Passed = true }
            }
        };

        var baseline = new MeshEvalSuiteResult
        {
            RunId = "baseline",
            Results = new List<MeshEvalResult>
            {
                new() { Passed = true },
                new() { Passed = true },
                new() { Passed = true },
                new() { Passed = true }
            }
        };

        var result = _runner.CompareWithBaseline(current, baseline);

        Assert.NotNull(result.RegressionVsBaseline);
        Assert.True(result.RegressionVsBaseline < 0); // Current worse than baseline
        Assert.True(result.HasRegression);
    }

    [Fact]
    public void CompareWithBaseline_NoRegression_WhenBetter()
    {
        var current = new MeshEvalSuiteResult
        {
            RunId = "current",
            Results = new List<MeshEvalResult>
            {
                new() { Passed = true },
                new() { Passed = true },
                new() { Passed = true },
                new() { Passed = true }
            }
        };

        var baseline = new MeshEvalSuiteResult
        {
            RunId = "baseline",
            Results = new List<MeshEvalResult>
            {
                new() { Passed = true },
                new() { Passed = true },
                new() { Passed = false },
                new() { Passed = true }
            }
        };

        var result = _runner.CompareWithBaseline(current, baseline);

        Assert.NotNull(result.RegressionVsBaseline);
        Assert.True(result.RegressionVsBaseline > 0); // Current better than baseline
        Assert.False(result.HasRegression);
    }

    // =====================================================================
    // Assertion evaluation tests
    // =====================================================================

    [Fact]
    public async Task RunScenarioAsync_EvaluatesAssertions_Correctly()
    {
        var scenario = new MeshEvalScenario
        {
            Id = "test-assertions",
            Name = "Test Assertions",
            Type = MeshEvalScenarioType.SafetyDenial, // Use type without computed assertion
            Intent = "Test",
            Assertions = new List<MeshEvalAssertion>
            {
                new() { Name = "safety_accuracy", Expected = ">=0.5", Actual = 0.8 },
                new() { Name = "false_positive_rate", Expected = "<0.5", Actual = 0.2 }
            }
        };

        // Verify scenario assertions are set
        Assert.NotNull(scenario.Assertions);
        Assert.Equal(2, scenario.Assertions.Count);

        var result = await _runner.RunScenarioAsync(scenario);

        // Debug: check what we got
        Assert.NotNull(result.Assertions);
        Assert.Equal(2, result.Assertions.Count); // Should have 2 assertions from scenario
        Assert.True(result.Assertions.All(a => a.Passed), "All assertions should pass");
    }

    [Fact]
    public void EvaluateAssertions_AddsScenarioAssertionsToResult()
    {
        // Test the EvaluateAssertions method directly
        var scenario = new MeshEvalScenario
        {
            Id = "test",
            Assertions = new List<MeshEvalAssertion>
            {
                new() { Name = "test1", Expected = ">=0.5", Actual = 0.8 },
                new() { Name = "test2", Expected = "<0.5", Actual = 0.2 }
            }
        };

        var result = new MeshEvalResult
        {
            ScenarioId = scenario.Id,
            Assertions = new List<MeshEvalAssertion>()
        };

        // Use reflection to call EvaluateAssertions
        var method = typeof(MeshEvalRunner).GetMethod("EvaluateAssertions",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);

        method.Invoke(_runner, new object[] { scenario, result });

        Assert.Equal(2, result.Assertions.Count);
        Assert.True(result.Assertions.All(a => a.Passed));
    }

    [Fact]
    public async Task RunScenarioAsync_FailsAssertion_WhenBelowThreshold()
    {
        var scenario = new MeshEvalScenario
        {
            Id = "test-fail",
            Name = "Test Fail",
            Type = MeshEvalScenarioType.SafetyDenial, // Use type without computed assertion
            Intent = "Test",
            Assertions = new List<MeshEvalAssertion>
            {
                new() { Name = "safety_accuracy", Expected = ">=0.9", Actual = 0.5 }
            }
        };

        var result = await _runner.RunScenarioAsync(scenario);

        // Debug: check what we got
        Assert.NotNull(result.Assertions);
        Assert.Equal(1, result.Assertions.Count);
        Assert.False(result.Assertions[0].Passed); // Should fail since 0.5 < 0.9
        Assert.False(result.Passed);
    }

    // =====================================================================
    // Dependency resolution tests
    // =====================================================================

    [Fact]
    public void ResolveDependencies_RespectsOrder()
    {
        var scenarios = new List<MeshEvalScenario>
        {
            new() { Id = "a", DependsOn = new List<string> { "b", "c" } },
            new() { Id = "b", DependsOn = new List<string> { "c" } },
            new() { Id = "c", DependsOn = new List<string>() }
        };

        // Use reflection to test internal method
        var method = typeof(MeshEvalRunner).GetMethod("ResolveDependencies",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);

        var runner = new MeshEvalRunner(_config, NullLogger<MeshEvalRunner>.Instance);
        var resolved = (List<MeshEvalScenario>)(method.Invoke(runner, new object[] { scenarios })!);

        // c should come before b, and b should come before a
        var cIndex = resolved.FindIndex(s => s.Id == "c");
        var bIndex = resolved.FindIndex(s => s.Id == "b");
        var aIndex = resolved.FindIndex(s => s.Id == "a");

        Assert.True(cIndex < bIndex, "c should come before b");
        Assert.True(bIndex < aIndex, "b should come before a");
    }
}
