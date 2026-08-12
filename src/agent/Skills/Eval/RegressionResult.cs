namespace Hercules.Skills.Eval;

/// <summary>
///     Результат сравнения текущей оценки с baseline.
///     Если HasRegression == true и порог превышен — изменения блокируются.
/// </summary>
public sealed class RegressionResult
{
    /// <summary>True если обнаружена регрессия (падение score > порога).</summary>
    public bool HasRegression { get; init; }

    /// <summary>Разница: current - previous (отрицательная = регрессия).</summary>
    public double ScoreDelta { get; init; }

    /// <summary>Score до изменений (baseline).</summary>
    public double PreviousScore { get; init; }

    /// <summary>Текущий score.</summary>
    public double CurrentScore { get; init; }

    /// <summary>ID baseline, с которым сравнивали.</summary>
    public string? BaselineId { get; init; }

    /// <summary>Когда был записан baseline.</summary>
    public string? BaselineRecordedAt { get; init; }

    /// <summary>Когда запущена текущая оценка.</summary>
    public string EvaluatedAt { get; init; } = DateTime.UtcNow.ToString("o");

    /// <summary>Список regression-тестов (тесты, которые стали хуже).</summary>
    public List<RegressionDetail> BlockedReasons { get; init; } = [];

}

/// <summary>
///     Детали одного regression-теста.
/// </summary>
public sealed class RegressionDetail
{
    public required string TestName { get; init; }
    public double PreviousScore { get; init; }
    public double CurrentScore { get; init; }
    public string? Reason { get; init; }
}
