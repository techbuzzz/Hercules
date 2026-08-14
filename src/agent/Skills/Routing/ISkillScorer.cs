using Hercules.Storage;

namespace Hercules.Skills.Routing;

/// <summary>
///     Один scorer возвращает normalized score [0..1] для одного навыка.
///     Skipping logic (e.g. embedding unavailable) возвращает null.
/// </summary>
/// <param name="input">Нормализованный входной запрос.</param>
/// <param name="skill">Оцениваемый навык.</param>
/// <param name="ct">Cancellation token.</param>
public readonly record struct ComponentScore(
    string ComponentName,
    double Value,         // 0..1
    bool IsEligible,       // false → навык полностью исключён из результата
    string? Details = null // опциональная диагностика
);

/// <summary>
///     Компонент scoring engine. Каждый scorer отвечает за одну dimension
///     (embedding, lexical, schema, quality, latency, policy).
/// </summary>
public interface ISkillScorer
{
    /// <summary>Имя компонента (для traceability).</summary>
    string ComponentName { get; }

    /// <summary>
    ///     Оценить навык по одному измерению.
    ///     Returns null if the scorer cannot evaluate (e.g. embedding provider offline).
    ///     When IsEligible=false, the skill is excluded from the final ranking.
    /// </summary>
    ValueTask<ComponentScore?> ScoreAsync(string input, Skill skill, CancellationToken ct = default);
}
