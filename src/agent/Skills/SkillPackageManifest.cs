using System.Text.Json;
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
}

/// <summary>
///     Набор тестов для навыка (skill.tests.json внутри пакета).
/// </summary>
public sealed class SkillTestSuite
{
    /// <summary>Версия формата тестов.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Список тест-кейсов.</summary>
    public List<SkillTestCase> Tests { get; set; } = new();
}

/// <summary>
///     Манифест пакета навыка (skill.package.json).
///     Описывает содержимое пакета: метаданные навыка, инструменты, тесты, версия формата.
///     Один пакет = одна папка с файлами: skill.meta.json, skill.prompt.md, skill.description.md,
///     skill.tests.json, tool.schema.json (опционально), skill.usage.json (опционально).
/// </summary>
public sealed class SkillPackageManifest
{
    /// <summary>Версия формата пакета.</summary>
    public int PackageVersion { get; set; } = 1;

    /// <summary>Метаданные навыка (id, name, description, phrase_receivers, version, ...).</summary>
    public SkillPackageSkillMeta Skill { get; set; } = new();

    /// <summary>Декларируемые инструменты (опционально).</summary>
    public List<ToolDeclaration>? Tools { get; set; }

    /// <summary>Тесты навыка (опционально).</summary>
    public SkillTestSuite? Tests { get; set; }

    /// <summary>Источник пакета (author, license, repository URL).</summary>
    public SkillPackageSource? Source { get; set; }

    /// <summary>Дата создания пакета (ISO 8601).</summary>
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");
}

/// <summary>
///     Метаданные навыка внутри пакета. Включает те же поля, что и SkillMeta,
///     но без runtime-метрик (SuccessRate, TotalUses) — они не переносятся при экспорте.
/// </summary>
public sealed class SkillPackageSkillMeta
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    [JsonPropertyName("phrase_receivers")]
    public List<string> PhraseReceivers { get; set; } = new();

    public int Version { get; set; } = 1;
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");
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