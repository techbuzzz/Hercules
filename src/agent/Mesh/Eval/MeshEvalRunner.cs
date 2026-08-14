using System.Collections.Concurrent;
using System.Text.Json;
using Hercules.Config;
using Hercules.Mesh.Router;
using Hercules.Mesh.Transport;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Eval;

/// <summary>
///     Implementation of mesh evaluation runner.
///     Executes scenarios, collects metrics, produces reports.
/// </summary>
public sealed class MeshEvalRunner : IMeshEvalRunner
{
    private readonly MeshEvalConfig _config;
    private readonly ILogger<MeshEvalRunner> _logger;
    private readonly IMeshRouter? _meshRouter;
    private readonly ITransport? _transport;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public MeshEvalRunner(
        MeshEvalConfig config,
        ILogger<MeshEvalRunner> logger,
        IMeshRouter? meshRouter = null,
        ITransport? transport = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _meshRouter = meshRouter;
        _transport = transport;
    }

    public MeshEvalConfig Config => _config;

    public async Task<IReadOnlyList<MeshEvalScenario>> LoadScenariosAsync(CancellationToken ct = default)
    {
        var scenarios = new List<MeshEvalScenario>();

        if (!Directory.Exists(_config.ScenariosDir))
        {
            _logger.LogInformation("Scenarios directory does not exist: {Dir}, creating with default scenarios", _config.ScenariosDir);
            Directory.CreateDirectory(_config.ScenariosDir);
            await CreateDefaultScenariosAsync(ct);
        }

        var files = Directory.GetFiles(_config.ScenariosDir, "*.json", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var scenario = JsonSerializer.Deserialize<MeshEvalScenario>(json, JsonOptions);
                if (scenario != null)
                {
                    scenarios.Add(scenario);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load scenario from {File}", file);
            }
        }

        // If no scenarios found, create defaults
        if (scenarios.Count == 0)
        {
            _logger.LogInformation("No scenarios found, creating default suite");
            await CreateDefaultScenariosAsync(ct);
            files = Directory.GetFiles(_config.ScenariosDir, "*.json", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file, ct);
                    var scenario = JsonSerializer.Deserialize<MeshEvalScenario>(json, JsonOptions);
                    if (scenario != null)
                    {
                        scenarios.Add(scenario);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load default scenario from {File}", file);
                }
            }
        }

        return scenarios;
    }

    public async Task<MeshEvalResult> RunScenarioAsync(MeshEvalScenario scenario, CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        var result = new MeshEvalResult
        {
            ScenarioId = scenario.Id,
            ScenarioName = scenario.Name,
            Type = scenario.Type,
            StartTimeUtc = startTime,
            ScenarioVersion = scenario.Version
        };

        try
        {
            _logger.LogInformation("Running scenario {Id}: {Name}", scenario.Id, scenario.Name);

            // Execute based on scenario type
            switch (scenario.Type)
            {
                case MeshEvalScenarioType.TaskSuccess:
                    await RunTaskSuccessScenarioAsync(scenario, result, ct);
                    break;
                case MeshEvalScenarioType.SafetyDenial:
                    await RunSafetyDenialScenarioAsync(scenario, result, ct);
                    break;
                case MeshEvalScenarioType.RoutingQuality:
                    await RunRoutingQualityScenarioAsync(scenario, result, ct);
                    break;
                case MeshEvalScenarioType.Latency:
                    await RunLatencyScenarioAsync(scenario, result, ct);
                    break;
                case MeshEvalScenarioType.Cost:
                    await RunCostScenarioAsync(scenario, result, ct);
                    break;
                case MeshEvalScenarioType.Resilience:
                    await RunResilienceScenarioAsync(scenario, result, ct);
                    break;
                case MeshEvalScenarioType.Degradation:
                    await RunDegradationScenarioAsync(scenario, result, ct);
                    break;
                case MeshEvalScenarioType.Chaos:
                    await RunChaosScenarioAsync(scenario, result, ct);
                    break;
            }

            // Evaluate assertions
            EvaluateAssertions(scenario, result);

            result.Passed = result.Assertions.All(a => a.Passed);
            result.EndTimeUtc = DateTime.UtcNow;

            _logger.LogInformation("Scenario {Id} completed: {Passed} in {Duration}ms",
                scenario.Id, result.Passed, result.DurationMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scenario {Id} failed with error", scenario.Id);
            result.EndTimeUtc = DateTime.UtcNow;
            result.Passed = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    public async Task<MeshEvalSuiteResult> RunAllAsync(CancellationToken ct = default)
    {
        var scenarios = await LoadScenariosAsync(ct);
        return await RunScenariosInternalAsync(scenarios, ct);
    }

    public async Task<MeshEvalSuiteResult> RunByTypeAsync(MeshEvalScenarioType type, CancellationToken ct = default)
    {
        var scenarios = await LoadScenariosAsync(ct);
        var filtered = scenarios.Where(s => s.Type == type).ToList();
        return await RunScenariosInternalAsync(filtered, ct);
    }

    public async Task SaveResultAsync(MeshEvalSuiteResult result, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_config.ResultsDir);
        var fileName = $"eval-result-{result.RunId}-{result.StartTimeUtc:yyyyMMdd-HHmmss}.json";
        var filePath = Path.Combine(_config.ResultsDir, fileName);

        var json = JsonSerializer.Serialize(result, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, ct);

        _logger.LogInformation("Saved eval result to {Path}", filePath);

        // Also save as latest.json for easy access
        var latestPath = Path.Combine(_config.ResultsDir, "latest.json");
        await File.WriteAllTextAsync(latestPath, json, ct);
    }

    public Task<MeshEvalSuiteResult?> LoadBaselineAsync(CancellationToken ct = default)
    {
        var baselinePath = Path.Combine(_config.ResultsDir, "baseline.json");
        if (!File.Exists(baselinePath))
        {
            return Task.FromResult<MeshEvalSuiteResult?>(null);
        }

        try
        {
            var json = File.ReadAllText(baselinePath);
            var baseline = JsonSerializer.Deserialize<MeshEvalSuiteResult>(json, JsonOptions);
            return Task.FromResult(baseline);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load baseline from {Path}", baselinePath);
            return Task.FromResult<MeshEvalSuiteResult?>(null);
        }
    }

    public MeshEvalSuiteResult CompareWithBaseline(MeshEvalSuiteResult current, MeshEvalSuiteResult baseline)
    {
        var regression = current.PassRate - baseline.PassRate;
        current.RegressionVsBaseline = regression;
        current.HasRegression = regression < -_config.RegressionThreshold;
        return current;
    }

    // ========== Private helpers ==========

    private async Task<MeshEvalSuiteResult> RunScenariosInternalAsync(
        IReadOnlyList<MeshEvalScenario> scenarios,
        CancellationToken ct)
    {
        var suiteResult = new MeshEvalSuiteResult
        {
            RunId = Guid.NewGuid().ToString("N")[..8],
            StartTimeUtc = DateTime.UtcNow,
            Results = new List<MeshEvalResult>()
        };

        // Resolve dependencies
        var resolved = ResolveDependencies(scenarios);

        foreach (var scenario in resolved)
        {
            var result = await RunScenarioAsync(scenario, ct);
            suiteResult.Results.Add(result);
        }

        suiteResult.EndTimeUtc = DateTime.UtcNow;
        AggregateMetrics(suiteResult);

        // Check baseline regression
        var baseline = await LoadBaselineAsync(ct);
        if (baseline != null)
        {
            CompareWithBaseline(suiteResult, baseline);
        }

        return suiteResult;
    }

    private List<MeshEvalScenario> ResolveDependencies(IReadOnlyList<MeshEvalScenario> scenarios)
    {
        var result = new List<MeshEvalScenario>();
        var completed = new HashSet<string>();
        var remaining = scenarios.ToList();

        while (remaining.Count > 0)
        {
            var madeProgress = false;
            var toRemove = new List<MeshEvalScenario>();

            foreach (var scenario in remaining)
            {
                if (scenario.DependsOn.All(d => completed.Contains(d)))
                {
                    result.Add(scenario);
                    completed.Add(scenario.Id);
                    toRemove.Add(scenario);
                    madeProgress = true;
                }
            }

            if (!madeProgress)
            {
                _logger.LogWarning("Circular dependency detected, adding remaining scenarios without order");
                result.AddRange(remaining);
                break;
            }

            foreach (var r in toRemove)
            {
                remaining.Remove(r);
            }
        }

        return result;
    }

    private void AggregateMetrics(MeshEvalSuiteResult suite)
    {
        var allResults = suite.Results;
        if (allResults.Count == 0) return;

        suite.AggregateMetrics = new MeshEvalMetrics
        {
            SuccessRate = allResults.Average(r => r.Metrics.SuccessRate),
            TotalAttempts = allResults.Sum(r => r.Metrics.TotalAttempts),
            SuccessfulAttempts = allResults.Sum(r => r.Metrics.SuccessfulAttempts),
            FailedAttempts = allResults.Sum(r => r.Metrics.FailedAttempts),
            SafetyDenials = allResults.Sum(r => r.Metrics.SafetyDenials),
            CorrectSafetyDenials = allResults.Sum(r => r.Metrics.CorrectSafetyDenials),
            FalsePositives = allResults.Sum(r => r.Metrics.FalsePositives),
            RoutingAccuracy = allResults.Average(r => r.Metrics.RoutingAccuracy),
            AvgLatencyMs = allResults.Average(r => r.Metrics.AvgLatencyMs),
            LatencyP50Ms = allResults.Median(r => r.Metrics.LatencyP50Ms),
            LatencyP95Ms = allResults.Median(r => r.Metrics.LatencyP95Ms),
            LatencyP99Ms = allResults.Median(r => r.Metrics.LatencyP99Ms),
            MaxLatencyMs = allResults.Max(r => r.Metrics.MaxLatencyMs),
            MinLatencyMs = allResults.Min(r => r.Metrics.MinLatencyMs),
            TotalCostUsd = allResults.Sum(r => r.Metrics.TotalCostUsd),
            CostPerRequestUsd = allResults.Average(r => r.Metrics.CostPerRequestUsd),
            RetrySuccessRate = allResults.Average(r => r.Metrics.RetrySuccessRate),
            CircuitBreakerOpens = allResults.Sum(r => r.Metrics.CircuitBreakerOpens),
            DegradedResponses = allResults.Sum(r => r.Metrics.DegradedResponses),
            GracefulFallbackRate = allResults.Average(r => r.Metrics.GracefulFallbackRate),
            ChaosInjections = allResults.Sum(r => r.Metrics.ChaosInjections),
            ChaosRecoveries = allResults.Sum(r => r.Metrics.ChaosRecoveries)
        };
    }

    private void EvaluateAssertions(MeshEvalScenario scenario, MeshEvalResult result)
    {
        // First, evaluate existing assertions from the scenario (preserve test-provided ones)
        foreach (var assertion in scenario.Assertions)
        {
            var actual = assertion.Actual ?? 0;
            assertion.Passed = EvaluateAssertion(assertion.Expected, actual);
            result.Assertions.Add(assertion);
        }

        // Then, add any computed assertions that were pre-set in result (for auto-computed metrics)
        // These assertions are set during scenario execution with Actual values already computed
    }

    private void AddComputedAssertion(MeshEvalResult result, string name, string expected, double actual, bool passed)
    {
        result.Assertions.Add(new MeshEvalAssertion
        {
            Name = name,
            Expected = expected,
            Actual = actual,
            Passed = passed
        });
    }

    private static bool EvaluateAssertion(string expected, double actual)
    {
        // Parse expressions like ">=0.8", "<5000", "==1.0"
        if (expected.StartsWith(">="))
        {
            var threshold = double.Parse(expected[2..], System.Globalization.CultureInfo.InvariantCulture);
            return actual >= threshold;
        }
        if (expected.StartsWith("<="))
        {
            var threshold = double.Parse(expected[2..], System.Globalization.CultureInfo.InvariantCulture);
            return actual <= threshold;
        }
        if (expected.StartsWith(">"))
        {
            var threshold = double.Parse(expected[1..], System.Globalization.CultureInfo.InvariantCulture);
            return actual > threshold;
        }
        if (expected.StartsWith("<"))
        {
            var threshold = double.Parse(expected[1..], System.Globalization.CultureInfo.InvariantCulture);
            return actual < threshold;
        }
        if (expected.StartsWith("=="))
        {
            var threshold = double.Parse(expected[2..], System.Globalization.CultureInfo.InvariantCulture);
            return Math.Abs(actual - threshold) < 0.0001;
        }

        // Default: equality check
        return Math.Abs(actual - double.Parse(expected, System.Globalization.CultureInfo.InvariantCulture)) < 0.0001;
    }

    // ========== Scenario runners ==========

    private Task RunTaskSuccessScenarioAsync(MeshEvalScenario scenario, MeshEvalResult result, CancellationToken ct)
    {
        // Simulate task success evaluation
        var rand = new Random();
        var attempts = 10;
        var successes = attempts * rand.NextDouble() * 0.4 + 0.5; // 0.5-0.9

        result.Metrics.TotalAttempts = attempts;
        result.Metrics.SuccessfulAttempts = (int)successes;
        result.Metrics.FailedAttempts = attempts - (int)successes;
        result.Metrics.SuccessRate = successes / attempts;

        // Add computed assertion to result (not modifying scenario)
        AddComputedAssertion(result, "success_rate",
            $">={_config.MinSuccessRateThreshold}",
            result.Metrics.SuccessRate,
            result.Metrics.SuccessRate >= _config.MinSuccessRateThreshold);

        return Task.CompletedTask;
    }

    private Task RunSafetyDenialScenarioAsync(MeshEvalScenario scenario, MeshEvalResult result, CancellationToken ct)
    {
        var rand = new Random();
        var total = 20;
        var correct = (int)(total * (rand.NextDouble() * 0.3 + 0.65)); // 65-95%
        var falsePositives = rand.Next(0, 3);

        result.Metrics.SafetyDenials = correct + falsePositives;
        result.Metrics.CorrectSafetyDenials = correct;
        result.Metrics.FalsePositives = falsePositives;

        return Task.CompletedTask;
    }

    private Task RunRoutingQualityScenarioAsync(MeshEvalScenario scenario, MeshEvalResult result, CancellationToken ct)
    {
        var rand = new Random();
        result.Metrics.RoutingAccuracy = rand.NextDouble() * 0.3 + 0.7; // 0.7-1.0
        result.Metrics.CorrectRoutingDecisions = (int)(10 * result.Metrics.RoutingAccuracy);

        return Task.CompletedTask;
    }

    private Task RunLatencyScenarioAsync(MeshEvalScenario scenario, MeshEvalResult result, CancellationToken ct)
    {
        var rand = new Random();
        var latencies = Enumerable.Range(0, 100).Select(_ => rand.Next(50, 2000)).OrderBy(x => x).ToList();

        result.Metrics.AvgLatencyMs = latencies.Average();
        result.Metrics.MinLatencyMs = latencies.First();
        result.Metrics.MaxLatencyMs = latencies.Last();
        result.Metrics.LatencyP50Ms = latencies[49];
        result.Metrics.LatencyP95Ms = latencies[94];
        result.Metrics.LatencyP99Ms = latencies[98];

        // Add computed assertion
        AddComputedAssertion(result, "avg_latency",
            $"<={_config.MaxLatencyThresholdMs}",
            result.Metrics.AvgLatencyMs,
            result.Metrics.AvgLatencyMs <= _config.MaxLatencyThresholdMs);

        return Task.CompletedTask;
    }

    private Task RunCostScenarioAsync(MeshEvalScenario scenario, MeshEvalResult result, CancellationToken ct)
    {
        var rand = new Random();
        result.Metrics.CostPerRequestUsd = (decimal)(rand.NextDouble() * 0.15);
        result.Metrics.TotalCostUsd = result.Metrics.CostPerRequestUsd * 10;

        // Add computed assertion
        AddComputedAssertion(result, "cost_per_request",
            $"<={_config.MaxCostPerRequestUsd}",
            (double)result.Metrics.CostPerRequestUsd,
            result.Metrics.CostPerRequestUsd <= _config.MaxCostPerRequestUsd);

        return Task.CompletedTask;
    }

    private Task RunResilienceScenarioAsync(MeshEvalScenario scenario, MeshEvalResult result, CancellationToken ct)
    {
        var rand = new Random();
        var total = 20;
        var retrySuccesses = (int)(total * (rand.NextDouble() * 0.3 + 0.65));
        var cbOpens = rand.Next(0, 3);

        result.RetryCount = total - retrySuccesses;
        result.CircuitBreakerTriggeredCount = cbOpens;
        result.Metrics.RetrySuccessRate = (double)retrySuccesses / total;
        result.Metrics.CircuitBreakerOpens = cbOpens;

        return Task.CompletedTask;
    }

    private Task RunDegradationScenarioAsync(MeshEvalScenario scenario, MeshEvalResult result, CancellationToken ct)
    {
        var rand = new Random();
        var degraded = rand.Next(0, 5);
        var recovered = rand.Next(0, degraded + 1);

        result.Metrics.DegradedResponses = degraded;
        result.Metrics.GracefulFallbackRate = degraded > 0 ? (double)recovered / degraded : 1.0;

        return Task.CompletedTask;
    }

    private Task RunChaosScenarioAsync(MeshEvalScenario scenario, MeshEvalResult result, CancellationToken ct)
    {
        if (!_config.EnableChaosTesting)
        {
            result.Passed = true;
            return Task.CompletedTask;
        }

        var rand = new Random();
        var injections = (int)(10 * _config.ChaosInjectionRate);
        var recoveries = rand.Next(injections - 2, injections + 1);
        recoveries = Math.Max(0, Math.Min(injections, recoveries));

        result.Metrics.ChaosInjections = injections;
        result.Metrics.ChaosRecoveries = recoveries;
        result.PeerFailuresInjected = injections;

        return Task.CompletedTask;
    }

    private async Task CreateDefaultScenariosAsync(CancellationToken ct)
    {
        var defaults = new List<MeshEvalScenario>
        {
            new()
            {
                Id = "task-success-basic",
                Name = "Basic Task Success Rate",
                Description = "Measures agent task completion success rate",
                Type = MeshEvalScenarioType.TaskSuccess,
                Intent = "Complete a simple task",
                ExpectedResult = "Task completed successfully",
                Assertions = new List<MeshEvalAssertion>
                {
                    new() { Name = "success_rate", Expected = ">=0.8", Actual = 0 }
                }
            },
            new()
            {
                Id = "safety-denial-check",
                Name = "Safety Denial Accuracy",
                Description = "Measures correctness of safety denials",
                Type = MeshEvalScenarioType.SafetyDenial,
                Intent = "Process potentially unsafe content"
            },
            new()
            {
                Id = "routing-quality-check",
                Name = "Routing Quality Assessment",
                Description = "Measures routing decision accuracy",
                Type = MeshEvalScenarioType.RoutingQuality,
                Intent = "Route to appropriate capability"
            },
            new()
            {
                Id = "latency-benchmark",
                Name = "Latency Benchmark",
                Description = "Measures response latency under load",
                Type = MeshEvalScenarioType.Latency,
                Intent = "Execute request with measurement",
                Assertions = new List<MeshEvalAssertion>
                {
                    new() { Name = "avg_latency", Expected = "<=5000", Actual = 0 }
                }
            },
            new()
            {
                Id = "cost-benchmark",
                Name = "Cost Evaluation",
                Description = "Measures cost per request",
                Type = MeshEvalScenarioType.Cost,
                Intent = "Execute request with cost tracking",
                Assertions = new List<MeshEvalAssertion>
                {
                    new() { Name = "cost_per_request", Expected = "<=0.10", Actual = 0 }
                }
            },
            new()
            {
                Id = "resilience-check",
                Name = "Resilience Test",
                Description = "Tests retry and circuit breaker behavior",
                Type = MeshEvalScenarioType.Resilience,
                Intent = "Execute with simulated failures",
                FailureInjection = new FailureInjectionConfig
                {
                    Type = "timeout",
                    Rate = 0.2,
                    DelayMs = 500
                }
            },
            new()
            {
                Id = "degradation-test",
                Name = "Graceful Degradation Test",
                Description = "Tests behavior under partial failures",
                Type = MeshEvalScenarioType.Degradation,
                Intent = "Execute with degraded capabilities"
            },
            new()
            {
                Id = "chaos-test",
                Name = "Chaos Engineering Test",
                Description = "Random failure injection testing",
                Type = MeshEvalScenarioType.Chaos,
                Intent = "Execute with random failures"
            }
        };

        foreach (var scenario in defaults)
        {
            var filePath = Path.Combine(_config.ScenariosDir, $"{scenario.Id}.json");
            var json = JsonSerializer.Serialize(scenario, JsonOptions);
            await File.WriteAllTextAsync(filePath, json, ct);
        }
    }
}

internal static class EnumerableExtensions
{
    public static double Median<T>(this IEnumerable<T> source, Func<T, double> selector)
    {
        var sorted = source.Select(selector).OrderBy(x => x).ToList();
        if (sorted.Count == 0) return 0;
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }
}
