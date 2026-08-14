using Hercules.Storage;

namespace Hercules.Skills.Routing.Deterministic;

/// <summary>
///     Task 023: Результат детерминированной маршрутизации (без embedding).
///     Возвращает best matching skill или null (direct LLM mode).
/// </summary>
/// <param name="MatchedSkill">Найденный навык или null.</param>
/// <param name="Score">Итоговая взвешенная оценка [0..1].</param>
/// <param name="MatchedMethods">Список совпавших методов: "keyword", "tag", "type".</param>
public readonly record struct DeterministicRouteResult(
    Skill? MatchedSkill,
    double Score,
    IReadOnlyList<string> MatchedMethods)
{
    public bool IsSkill => MatchedSkill is not null;

    /// <summary>Создаёт пустой результат (direct mode).</summary>
    public static DeterministicRouteResult None => new(null, 0, Array.Empty<string>());
}
