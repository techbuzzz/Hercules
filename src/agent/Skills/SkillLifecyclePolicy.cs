using Hercules.Storage;

namespace Hercules.Skills;

/// <summary>
///     Действия жизненного цикла навыка, требующие проверки политики.
///     Каждое действие имеет фиксированный risk level.
/// </summary>
public enum SkillAction
{
    Create,
    CreateWithAi,
    Improve,
    Update,
    Deprecate,
    Rollback,
    Delete,
    Evaluate
}

/// <summary>
///     Risk level действия: определяет, нужен ли human approval.
/// </summary>
public enum SkillActionRisk
{
    /// <summary>Выполняется автоматически без подтверждения.</summary>
    Low,

    /// <summary>Требуется подтверждение пользователя (API возвращает 202 + approvalRequired).</summary>
    Medium,

    /// <summary>Выполняется только после явного human-gate подтверждения.</summary>
    High
}

/// <summary>
///     Результат проверки policy: можно ли выполнить действие без дополнительного подтверждения.
/// </summary>
public readonly record struct PolicyCheckResult(
    SkillActionRisk Risk,
    bool NeedsHumanApproval,
    string? Reason)
{
    public static PolicyCheckResult Allowed(SkillActionRisk risk) =>
        new(risk, false, null);

    public static PolicyCheckResult Blocked(SkillActionRisk risk, string reason) =>
        new(risk, true, reason);
}

/// <summary>
///     Политика жизненного цикла навыков: определяет, требуется ли approval
///     для конкретного действия в текущем контексте навыка.
///     High-risk действия (Improve при SuccessRate &lt; 0.4, Rollback, Deprecate)
///     требуют явного human-gate подтверждения.
/// </summary>
public sealed class SkillLifecyclePolicy
{
    private const double HighRiskThreshold = 0.4;
    private const double MediumRiskThreshold = 0.6;

    /// <summary>
    ///     Проверить, требуется ли approval для действия над навыком.
    ///     Возвращает PolicyCheckResult с уровнем риска и причиной (если нужен approval).
    /// </summary>
    /// <param name="skill">Навык, над которым выполняется действие (null для Create).</param>
    /// <param name="action">Действие.</param>
    public PolicyCheckResult RequiresApproval(Skill? skill, SkillAction action)
    {
        SkillActionRisk risk = GetBaseRisk(action);

        // Переопределяем risk Improve в зависимости от SuccessRate
        if (action == SkillAction.Improve && skill is not null)
        {
            risk = GetImproveRisk(skill.Meta.SuccessRate);
        }

        // Deprecated-навыки: любые изменения — High risk
        if (skill?.Meta.DeprecatedAt is not null &&
            action is SkillAction.Improve or SkillAction.Update or SkillAction.Rollback)
        {
            return PolicyCheckResult.Blocked(
                SkillActionRisk.High,
                $"Навык '{skill.Meta.Name}' помечен deprecated ({skill.Meta.DeprecationReason}). Изменение deprecated навыка — High risk.");
        }

        if (risk == SkillActionRisk.High)
        {
            return PolicyCheckResult.Blocked(risk, $"Action '{action}' классифицирован как High risk.");
        }

        if (risk == SkillActionRisk.Medium)
        {
            return PolicyCheckResult.Blocked(risk, $"Action '{action}' классифицирован как Medium risk.");
        }

        return PolicyCheckResult.Allowed(risk);
    }

    /// <summary>
    ///     Только базовый риск действия (без учёта контекста навыка).
    ///     Используется для быстрой фильтрации в UI/API.
    /// </summary>
    public static SkillActionRisk GetBaseRisk(SkillAction action) =>
        action switch
        {
            SkillAction.Create => SkillActionRisk.Low,
            SkillAction.CreateWithAi => SkillActionRisk.Medium,
            SkillAction.Improve => SkillActionRisk.High,
            SkillAction.Update => SkillActionRisk.Low,
            SkillAction.Deprecate => SkillActionRisk.High,
            SkillAction.Rollback => SkillActionRisk.High,
            SkillAction.Delete => SkillActionRisk.High,
            SkillAction.Evaluate => SkillActionRisk.Low,
            _ => SkillActionRisk.Low
        };

    private static SkillActionRisk GetImproveRisk(double successRate) =>
        successRate <= HighRiskThreshold
            ? SkillActionRisk.High
            : successRate < MediumRiskThreshold
                ? SkillActionRisk.Medium
                : SkillActionRisk.Low;
}
