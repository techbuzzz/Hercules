namespace Hercules.Mesh.Eval;

/// <summary>
///     Результат выполнения eval-сценария.
/// </summary>
public sealed class MeshEvalResult
{
    /// <summary>ID выполненного сценария.</summary>
    public string ScenarioId { get; set; } = "";

    /// <summary>Имя сценария.</summary>
    public string ScenarioName { get; set; } = "";

    /// <summary>Тип сценария.</summary>
    public MeshEvalScenarioType Type { get; set; }

    /// <summary>Время начала выполнения (UTC).</summary>
    public DateTime StartTimeUtc { get; set; }

    /// <summary>Время окончания (UTC).</summary>
    public DateTime EndTimeUtc { get; set; }

    /// <summary>Длительность в миллисекундах.</summary>
    public long DurationMs => (long)(EndTimeUtc - StartTimeUtc).TotalMilliseconds;

    /// <summary>Overall pass/fail.</summary>
    public bool Passed { get; set; }

    /// <summary>Результат assertions.</summary>
    public List<MeshEvalAssertion> Assertions { get; set; } = new();

    /// <summary>Actual метрики (success_rate, latency, cost, etc.).</summary>
    public MeshEvalMetrics Metrics { get; set; } = new();

    /// <summary>Детали ошибки если failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Retry count для resilience сценариев.</summary>
    public int RetryCount { get; set; }

    /// <summary>Circuit breaker triggered count.</summary>
    public int CircuitBreakerTriggeredCount { get; set; }

    /// <summary>Peer failures simulated.</summary>
    public int PeerFailuresInjected { get; set; }

    /// <summary>Версия сценария.</summary>
    public string ScenarioVersion { get; set; } = "1.0.0";
}

/// <summary>
///     Метрики собранные во время выполнения сценария.
/// </summary>
public sealed class MeshEvalMetrics
{
    /// <summary>Success rate (0.0-1.0).</summary>
    public double SuccessRate { get; set; }

    /// <summary>Total attempts.</summary>
    public int TotalAttempts { get; set; }

    /// <summary>Successful attempts.</summary>
    public int SuccessfulAttempts { get; set; }

    /// <summary>Failed attempts.</summary>
    public int FailedAttempts { get; set; }

    /// <summary>Safety denials detected.</summary>
    public int SafetyDenials { get; set; }

    /// <summary>Correct safety denials (expected vs actual).</summary>
    public int CorrectSafetyDenials { get; set; }

    /// <summary>False positives (safe content denied).</summary>
    public int FalsePositives { get; set; }

    /// <summary>Routing accuracy (0.0-1.0).</summary>
    public double RoutingAccuracy { get; set; }

    /// <summary>Correct routing decisions.</summary>
    public int CorrectRoutingDecisions { get; set; }

    /// <summary>Average latency in ms.</summary>
    public double AvgLatencyMs { get; set; }

    /// <summary>P50 latency in ms.</summary>
    public double LatencyP50Ms { get; set; }

    /// <summary>P95 latency in ms.</summary>
    public double LatencyP95Ms { get; set; }

    /// <summary>P99 latency in ms.</summary>
    public double LatencyP99Ms { get; set; }

    /// <summary>Max latency in ms.</summary>
    public double MaxLatencyMs { get; set; }

    /// <summary>Min latency in ms.</summary>
    public double MinLatencyMs { get; set; }

    /// <summary>Total cost in USD.</summary>
    public decimal TotalCostUsd { get; set; }

    /// <summary>Cost per request in USD.</summary>
    public decimal CostPerRequestUsd { get; set; }

    /// <summary>Retry success rate (0.0-1.0).</summary>
    public double RetrySuccessRate { get; set; }

    /// <summary>Circuit breaker open count.</summary>
    public int CircuitBreakerOpens { get; set; }

    /// <summary>Circuit breaker half-open success.</summary>
    public int CircuitBreakerHalfOpenSuccess { get; set; }

    /// <summary>Degraded responses count.</summary>
    public int DegradedResponses { get; set; }

    /// <summary>Graceful fallback success rate.</summary>
    public double GracefulFallbackRate { get; set; }

    /// <summary>Chaos injection count.</summary>
    public int ChaosInjections { get; set; }

    /// <summary>Chaos recovery count.</summary>
    public int ChaosRecoveries { get; set; }
}

/// <summary>
///     Агрегированный результат suite run.
/// </summary>
public sealed class MeshEvalSuiteResult
{
    /// <summary>ID suite run.</summary>
    public string RunId { get; set; } = "";

    /// <summary>Время начала (UTC).</summary>
    public DateTime StartTimeUtc { get; set; }

    /// <summary>Время окончания (UTC).</summary>
    public DateTime EndTimeUtc { get; set; }

    /// <summary>Total duration in seconds.</summary>
    public double DurationSeconds => (EndTimeUtc - StartTimeUtc).TotalSeconds;

    /// <summary>Все выполненные результаты.</summary>
    public List<MeshEvalResult> Results { get; set; } = new();

    /// <summary>Overall pass/fail для всего suite.</summary>
    public bool Passed => Results.Count > 0 && Results.All(r => r.Passed);

    /// <summary>Pass count.</summary>
    public int PassedCount => Results.Count(r => r.Passed);

    /// <summary>Fail count.</summary>
    public int FailedCount => Results.Count - PassedCount;

    /// <summary>Pass rate (0.0-1.0).</summary>
    public double PassRate => Results.Count > 0 ? (double)PassedCount / Results.Count : 0.0;

    /// <summary>Агрегированные метрики.</summary>
    public MeshEvalMetrics AggregateMetrics { get; set; } = new();

    /// <summary>Regression relative to baseline (negative = improvement).</summary>
    public double? RegressionVsBaseline { get; set; }

    /// <summary>Has regression (pass rate dropped vs baseline).</summary>
    public bool HasRegression { get; set; }
}
