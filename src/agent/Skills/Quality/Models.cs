namespace Hercules.Skills.Quality;

/// <summary>
///     Типы событий качества навыка для записи в SkillQualityStore.
/// </summary>
public enum SkillQualityEvent
{
    ToolFallback,
    UserCorrection,
    SafetyDenial,
    LatencySample,
    CostSample,
}

/// <summary>
///     Per-version метрики качества одного навыка.
///     Все rate-поля — [0..1]. AvgLatencyMs и AvgCostUsd — абсолютные значения.
/// </summary>
public sealed class SkillQualityMetrics
{
    /// <summary>ID навыка.</summary>
    public string SkillId { get; set; } = "";

    /// <summary>Версия навыка.</summary>
    public int Version { get; set; } = 1;

    /// <summary>
    ///     Доля успешных вызовов: 1 - (fallbackCount / totalCalls).
    /// </summary>
    public double AcceptanceRate { get; set; } = 1.0;

    /// <summary>
    ///     Средний eval harness score (0..1) из последнего запуска.
    ///     0 = нет оценки.
    /// </summary>
    public double TestScore { get; set; } = 0.0;

    /// <summary>
    ///     Доля вызовов, где пользователь внёс корректировку: userCorrectionCount / totalCalls.
    /// </summary>
    public double UserCorrectionRate { get; set; } = 0.0;

    /// <summary>
    ///     Доля вызовов с fallback на другой навык: fallbackCount / totalCalls.
    /// </summary>
    public double FallbackRate { get; set; } = 0.0;

    /// <summary>
    ///     Средняя latency вызова в миллисекундах.
    /// </summary>
    public double AvgLatencyMs { get; set; } = 0.0;

    /// <summary>
    ///     Средняя стоимость вызова в USD.
    /// </summary>
    public double AvgCostUsd { get; set; } = 0.0;

    /// <summary>
    ///     Общее число вызовов навыка.
    /// </summary>
    public int TotalCalls { get; set; } = 0;

    /// <summary>
    ///     Число блокировок по safety/approval.
    /// </summary>
    public int SafetyDenialCount { get; set; } = 0;

    /// <summary>
    ///     Timestamp последнего обновления (UTC ISO 8601).
    /// </summary>
    public string UpdatedAt { get; set; } = DateTime.UtcNow.ToString("o");

    // ─── Internal counters for incremental updates ─────────────────────────────

    internal int FallbackCount { get; set; } = 0;
    internal int UserCorrectionCount { get; set; } = 0;
    internal double TotalLatencyMs { get; set; } = 0.0;
    internal double TotalCostUsd { get; set; } = 0.0;
}

/// <summary>
///     Итоговый composite quality score для API-ответа.
/// </summary>
public sealed class SkillQualityScore
{
    public required string SkillId { get; init; }
    public required int Version { get; init; }
    public double CompositeScore { get; init; }
    public required bool IsReliable { get; init; }
    public string? Reason { get; init; }
    public double? AcceptanceRate { get; init; }
    public double? TestScore { get; init; }
    public double? UserCorrectionRate { get; init; }
    public double? FallbackRate { get; init; }
    public double? AvgLatencyMs { get; init; }
    public double? AvgCostUsd { get; init; }
    public int TotalCalls { get; init; }
    public int SafetyDenialCount { get; init; }
}
