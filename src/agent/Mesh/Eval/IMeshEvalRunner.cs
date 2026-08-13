using Hercules.Config;

namespace Hercules.Mesh.Eval;

/// <summary>
///     Interface для mesh evaluation runner.
///     Выполняет сценарии, собирает метрики, формирует отчёты.
/// </summary>
public interface IMeshEvalRunner
{
    /// <summary>Текущая конфигурация.</summary>
    MeshEvalConfig Config { get; }

    /// <summary>
    ///     Загрузить все сценарии из директории.
    /// </summary>
    Task<IReadOnlyList<MeshEvalScenario>> LoadScenariosAsync(CancellationToken ct = default);

    /// <summary>
    ///     Выполнить один сценарий.
    /// </summary>
    Task<MeshEvalResult> RunScenarioAsync(MeshEvalScenario scenario, CancellationToken ct = default);

    /// <summary>
    ///     Выполнить все сценарии suite.
    /// </summary>
    Task<MeshEvalSuiteResult> RunAllAsync(CancellationToken ct = default);

    /// <summary>
    ///     Выполнить сценарии по типу.
    /// </summary>
    Task<MeshEvalSuiteResult> RunByTypeAsync(MeshEvalScenarioType type, CancellationToken ct = default);

    /// <summary>
    ///     Сохранить результат suite в файл.
    /// </summary>
    Task SaveResultAsync(MeshEvalSuiteResult result, CancellationToken ct = default);

    /// <summary>
    ///     Получить baseline результат для сравнения regression.
    /// </summary>
    Task<MeshEvalSuiteResult?> LoadBaselineAsync(CancellationToken ct = default);

    /// <summary>
    ///     Сравнить текущий результат с baseline и вернуть regression.
    /// </summary>
    MeshEvalSuiteResult CompareWithBaseline(MeshEvalSuiteResult current, MeshEvalSuiteResult baseline);
}
