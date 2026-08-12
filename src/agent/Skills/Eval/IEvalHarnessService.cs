namespace Hercules.Skills.Eval;

/// <summary>
///     Интерфейс eval harness: запуск оценки, baseline management, regression detection.
/// </summary>
public interface IEvalHarnessService
{
    /// <summary>
    ///     Запустить eval harness для навыка: выполняет test suite,
    ///     сравнивает с baseline, возвращает RegressionResult.
    /// </summary>
    Task<RegressionResult> RunHarnessAsync(string skillId, CancellationToken ct = default);

    /// <summary>
    ///     Записать текущий результат как baseline.
    /// </summary>
    Task<BaselineRecord> RecordBaselineAsync(
        string skillId,
        SkillEvaluationResult result,
        string? recordedBy = null,
        string? reason = null,
        CancellationToken ct = default);

    /// <summary>
    ///     Сравнить текущий результат с baseline.
    /// </summary>
    RegressionResult CompareWithBaseline(string skillId, SkillEvaluationResult currentResult);

    /// <summary>
    ///     Получить текущий baseline для навыка.
    /// </summary>
    BaselineRecord? GetBaseline(string skillId);

    /// <summary>
    ///     Проверить существование baseline.
    /// </summary>
    bool HasBaseline(string skillId);

    /// <summary>
    ///     Получить историю baseline-записей.
    /// </summary>
    List<BaselineRecord> GetBaselineHistory(string? skillId = null);
}
