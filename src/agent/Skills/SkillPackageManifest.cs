using System.Text.Json.Serialization;

namespace Hercules.Skills;

/// <summary>
///     Декларация инструмента, используемого навыком.
///     Навык может требовать HTTP, файловую систему, shell, БД, GPIO/MQTT —
///     реестр инструментов загружает только разрешённые через allow/deny-списки.
/// </summary>
public sealed class ToolDeclaration
{
    /// <summary>Имя инструмента (должно совпадать с зарегистрированным ITool.Name).</summary>
    public string Name { get; set; } = "";

    /// <summary>Описание того, как навык использует этот инструмент (для документации).</summary>
    public string Description { get; set; } = "";

    /// <summary>Обязательный ли инструмент для навыка (если true и не зарегистрирован — навык не активируется).</summary>
    public bool Required { get; set; } = true;
}

/// <summary>
///     Тест-кейс для навыка: вход → ожидаемый выход (или критерий проверки).
///     Используется для валидации навыка при импорте и улучшении.
///     JudgedBy: null=legacy(deterministic), "deterministic", "llm" (seed-based reproducibility).
/// </summary>
public sealed class SkillTestCase
{
    public string Name { get; set; } = "";

    /// <summary>Входной запрос пользователя.</summary>
    public string Input { get; set; } = "";

    /// <summary>
    ///     Ожидаемый ответ или его часть. Если пусто — проверяется только отсутствие ошибки.
    /// </summary>
    public string? ExpectedContains { get; set; }

    /// <summary>Минимальная ожидаемая уверенность (high | medium | low).</summary>
    public string? MinConfidence { get; set; }

    /// <summary>Ожидаемый режим ответа: skill | direct | tool. Если null — любой.</summary>
    public string? ExpectedMode { get; set; }

    /// <summary>Тип проверки: deterministic (default) | llm. Null treated as deterministic.</summary>
    public string? JudgedBy { get; set; }

    /// <summary>Prompt для LLM-judge (используется только при JudgedBy="llm").</summary>
    public string? JudgePrompt { get; set; }
}

/// <summary>
///     Набор тестов для навыка (skill.tests.json внутри пакета).
/// </summary>
public sealed class SkillTestSuite
{
    /// <summary>Версия формата тестов.</summary>
    public string? Version { get; set; } = "1.0";

    /// <summary>Описание набора тестов.</summary>
    public string? Description { get; set; }

    /// <summary>Список тест-кейсов.</summary>
    public List<SkillTestCase> Tests { get; set; } = new();
}

/// <summary>
///     Манифест пакета навыка (skill.package.json, ZIP only).
///     Описывает содержимое пакета: метаданные навыка, инструменты, тесты, версия формата.
///     Для folder-структуры авторитетные данные лежат в skill.meta.json, skill.prompt.md, etc.
/// </summary>
public sealed class SkillPackageManifest
{
    /// <summary>Версия формата пакета (1 = текущая).</summary>
    public int PackageVersion { get; set; } = 1;

    /// <summary>
    ///     Формат пакета: "folder" (directory) или "zip" (.skillpkg).
    ///     Для ZIP-пакетов это поле всегда "zip"; для folder-экспорта ставится "folder".
    /// </summary>
    public string PackageFormat { get; set; } = "zip";

    /// <summary>
    ///     Версия спецификации формата пакета (semver). Сигнализирует о breaking changes в формате.
    ///     Текущая версия: "1.0.0".
    /// </summary>
    public string PackageSpecVersion { get; set; } = "1.0.0";

    /// <summary>Метаданные навыка (id, name, description, phrase_receivers, version, ...).</summary>
    public SkillPackageSkillMeta Skill { get; set; } = new();

    /// <summary>Декларируемые инструменты (опционально).</summary>
    public List<ToolDeclaration>? Tools { get; set; }

    /// <summary>Тесты навыка (опционально).</summary>
    public SkillTestSuite? Tests { get; set; }

    /// <summary>Примеры использования навыка (skill.examples.json, опционально).</summary>
    public SkillExamples? Examples { get; set; }

    /// <summary>Источник пакета (author, license, repository URL).</summary>
    public SkillPackageSource? Source { get; set; }

    /// <summary>Дата создания пакета (ISO 8601).</summary>
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");
}

/// <summary>
///     Метаданные навыка внутри пакета. Включает те же поля, что и SkillMeta,
///     но без runtime-метрик (SuccessRate, TotalUses) — они не переносятся при экспорте.
///     Расширен в task_020 полями совместимости: owner, Hercules-version range,
///     schema versions, permissions, model requirements, risk level, budgets.
/// </summary>
public sealed class SkillPackageSkillMeta
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    [JsonPropertyName("phrase_receivers")] public List<string> PhraseReceivers { get; set; } = new();

    public int Version { get; set; } = 1;
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");

    // Task 020: Skill Manifest & Compatibility
    public string? Owner { get; set; }

    [JsonPropertyName("min_hercules_version")]
    public string? MinHerculesVersion { get; set; }

    [JsonPropertyName("max_hercules_version")]
    public string? MaxHerculesVersion { get; set; }

    [JsonPropertyName("input_schema_version")]
    public string? InputSchemaVersion { get; set; }

    [JsonPropertyName("output_schema_version")]
    public string? OutputSchemaVersion { get; set; }

    /// <summary>Запрошенные permissions навыка.</summary>
    public List<string> Permissions { get; set; } = new();

    [JsonPropertyName("model_requirements")]
    public string? ModelRequirements { get; set; }

    /// <summary>Уровень риска: 0=Low, 1=Medium, 2=High, 3=Critical.</summary>
    [JsonPropertyName("risk_level")]
    public int RiskLevel { get; set; } = 0;

    /// <summary>Декларативные бюджетные лимиты навыка.</summary>
    public SkillPackageBudget? Budget { get; set; }
}

/// <summary>
///     Бюджетные лимиты внутри пакета (сериализуются в skill.package.json).
/// </summary>
public sealed class SkillPackageBudget
{
    [JsonPropertyName("max_tokens_per_call")]
    public int MaxTokensPerCall { get; set; } = 0;

    [JsonPropertyName("max_calls_per_minute")]
    public int MaxCallsPerMinute { get; set; } = 0;

    [JsonPropertyName("max_cost_per_call_usd")]
    public decimal MaxCostPerCallUsd { get; set; } = 0;
}

/// <summary>
///     Источник пакета: автор, лицензия, репозиторий.
/// </summary>
public sealed class SkillPackageSource
{
    public string? Author { get; set; }
    public string? License { get; set; }

    /// <summary>URL репозитория или маркетплейса, откуда импортирован пакет.</summary>
    public string? Repository { get; set; }
}

/// <summary>
///     Примеры использования навыка (skill.examples.json).
/// </summary>
public sealed class SkillExamples
{
    public List<SkillExample> Examples { get; set; } = new();
}

/// <summary>
///     Один пример использования навыка.
/// </summary>
public sealed class SkillExample
{
    /// <summary>Пример входного запроса пользователя.</summary>
    public string Input { get; set; } = "";

    /// <summary>Ожидаемое поведение навыка.</summary>
    public string ExpectedBehavior { get; set; } = "";
}
