using System.Text.Json.Serialization;

namespace Hercules.Skills;

/// <summary>
///     Skill manifest — декларативная карточка навыка для маркетплейса, импорта и совместимости.
///
///     Поля:
///
///     - **ID, Name, Description, Version** — базовая идентификация (в SkillMeta).
///     - **SchemaVersion** — версия формата манифеста (semver).
///     - **Owner** — автор / maintainer.
///     - **MinHerculesVersion / MaxHerculesVersion** — диапазон совместимых версий Hercules.
///     - **InputSchemaVersion / OutputSchemaVersion** — версии схем ввода/вывода навыка.
///     - **RequiredTools** — список имён необходимых инструментов.
///     - **Permissions** — запрошенные permissions (Read, Write, Network, Memory, etc.).
///     - **ModelRequirements** — минимальные требования к модели.
///     - **RiskLevel** — уровень риска навыка.
///     - **Budget** — декларативные бюджетные лимиты.
///
///     Сериализуется в skill.meta.json и skill.package.json (через SkillPackageManifest).
/// </summary>
public sealed class SkillManifest
{
    /// <summary>Версия формата манифеста (semver). Текущая: "1.0.0".</summary>
    public string SchemaVersion { get; set; } = "1.0.0";

    /// <summary>Автор или maintainer навыка.</summary>
    public string? Owner { get; set; }

    /// <summary>
    ///     Минимальная совместимая версия Hercules (semver, например "1.0.0").
    ///     Если Hercules старше этой версии — навык несовместим.
    /// </summary>
    public string? MinHerculesVersion { get; set; }

    /// <summary>
    ///     Максимальная совместимая версия Hercules (semver, например "2.0.0").
    ///     Если Hercules новее этой версии — навык может быть несовместим.
    ///     Null = без ограничения.
    /// </summary>
    public string? MaxHerculesVersion { get; set; }

    /// <summary>
    ///     Версия схемы входных данных навыка (semver).
    ///     Позволяет определять breaking changes в input contract.
    /// </summary>
    public string? InputSchemaVersion { get; set; }

    /// <summary>
    ///     Версия схемы выходных данных навыка (semver).
    ///     Позволяет определять breaking changes в output contract.
    /// </summary>
    public string? OutputSchemaVersion { get; set; }

    /// <summary>
    ///     Список имён инструментов, требуемых для работы навыка.
    ///     Если RequiredTools ⊄ зарегистрированных — навык не активируется.
    /// </summary>
    public List<string> RequiredTools { get; set; } = new();

    /// <summary>
    ///     Запрошенные permissions навыка: Read, Write, Network, Memory, Shell, CodeExecution, etc.
    ///     Используется least-privilege проверкой при импорте.
    /// </summary>
    public List<string> Permissions { get; set; } = new();

    /// <summary>
    ///     Минимальные требования к LLM-модели (например, "context≥8k, function_calling").
    ///     Свободная строка для гибкости; runtime может парсить known patterns.
    /// </summary>
    public string? ModelRequirements { get; set; }

    /// <summary>
    ///     Уровень риска навыка.
    ///     Low = read-only, не изменяет внешнего состояния.
    ///     Medium = может изменять локальное состояние.
    ///     High = изменяет внешние системы.
    ///     Critical = потенциально необратимые действия.
    /// </summary>
    public SkillRiskLevel RiskLevel { get; set; } = SkillRiskLevel.Low;

    /// <summary>
    ///     Декларативные бюджетные лимиты навыка.
    ///     Это soft limits — runtime может усиливать/ослаблять.
    /// </summary>
    public SkillManifestBudget? Budget { get; set; }
}

/// <summary>
///     Уровень риска навыка.
/// </summary>
public enum SkillRiskLevel
{
    /// <summary>Read-only, не изменяет внешнего состояния.</summary>
    Low = 0,

    /// <summary>Может изменять локальное состояние.</summary>
    Medium = 1,

    /// <summary>Изменяет внешние системы.</summary>
    High = 2,

    /// <summary>Потенциально необратимые действия.</summary>
    Critical = 3
}

/// <summary>
///     Декларативные бюджетные лимиты навыка.
/// </summary>
public sealed class SkillManifestBudget
{
    /// <summary>Максимум токенов на один вызов навыка.</summary>
    public int MaxTokensPerCall { get; set; } = 0;

    /// <summary>Максимум вызовов в минуту.</summary>
    public int MaxCallsPerMinute { get; set; } = 0;

    /// <summary>Максимум стоимости на один вызов (USD).</summary>
    public decimal MaxCostPerCallUsd { get; set; } = 0;
}
