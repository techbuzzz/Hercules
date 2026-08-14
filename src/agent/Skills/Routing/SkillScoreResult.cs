using Hercules.Storage;

namespace Hercules.Skills.Routing;

/// <summary>
///     Результат scoring engine для одного навыка.
/// </summary>
public sealed class SkillScoreResult
{
    /// <summary>Оцениваемый навык.</summary>
    public required Skill Skill { get; init; }

    /// <summary>Итоговый взвешенный score [0..1].</summary>
    public double OverallScore { get; set; }

    /// <summary>
    ///     False если хотя бы один scorer пометил навык как неприемлемый
    ///     (e.g. policy violation, schema incompatibility).
    /// </summary>
    public bool IsEligible { get; set; } = true;

    /// <summary>Per-component breakdown.</summary>
    public Dictionary<string, ComponentScore> ComponentScores { get; } = new();

    /// <summary>
    ///     Основной метод маршрутизации: "embedding", "lexical", "hybrid".
    /// </summary>
    public string PrimaryMethod { get; set; } = "none";

    /// <summary>Соображения о неприемлемости (для логов/UI).</summary>
    public string? IneligibilityReason { get; set; }

    /// <summary>Инициализатор для object initializer.</summary>
    public SkillScoreResult() { }
}
