using System.Text.Json.Serialization;
using Hercules.Skills;

namespace Hercules.Storage;

/// <summary>
///     Метаданные навыка (skill.{id}.meta.json).
/// </summary>
public sealed class SkillMeta
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>
    ///     Фразы-приёмники: слова/паттерны, по которым SkillRouter матчит запрос пользователя с навыком.
    ///     Ранее называлось Triggers — переименовано в PhraseReceivers (human-friendly, точнее отражает смысл).
    ///     Поддерживается обратная совместимость при чтении legacy meta.json (ключ "triggers").
    /// </summary>
    [JsonPropertyName("phrase_receivers")]
    public List<string> PhraseReceivers { get; set; } = new();

    /// <summary>
    ///     Legacy-поле для обратной совместимости при чтении старых skill.{id}.meta.json.
    ///     На запись НЕ используется: сериализуется только PhraseReceivers как "phrase_receivers".
    ///     При десериализации, если phrase_receivers пуст — populate from Triggers.
    /// </summary>
    [JsonPropertyName("triggers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Triggers
    {
        set
        {
            if (value is null || value.Count == 0)
            {
                return;
            }

            if (PhraseReceivers.Count == 0)
            {
                PhraseReceivers = value
                    .Select(t => t.Trim().ToLowerInvariant())
                    .Where(t => t.Length > 0)
                    .Distinct()
                    .ToList();
            }
        }
    }

    /// <summary>
    ///     Phase 2: Декларации инструментов, используемых навыком.
    ///     Если null или пусто — навык не требует инструментов.
    ///     Реестр инструментов (ToolRegistry) проверяет, что все Required-инструменты зарегистрированы.
    /// </summary>
    [JsonPropertyName("tools")]
    public List<ToolDeclaration>? Tools { get; set; }

    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");
    public int Version { get; set; } = 1;
    public double SuccessRate { get; set; } = 1.0;
    public int TotalUses { get; set; } = 0;

    /// <summary>
    ///     Дата депрекации навыка. Null = навык активен.
    ///     Заполняется через SkillDeprecationManager.DeprecateAsync.
    /// </summary>
    public string? DeprecatedAt { get; set; }

    /// <summary>
    ///     Причина депрекации (например, "заменён навыком X", "низкое качество").
    /// </summary>
    public string? DeprecationReason { get; set; }

    /// <summary>
    ///     Последняя оценка качества (0..1), записанная через SkillEvaluationEngine.
    ///     Null = оценка ещё не проводилась.
    /// </summary>
    public double? LastEvaluationScore { get; set; }

    /// <summary>
    ///     Task 023: Декларативные типы ввода навыка.
    ///     Используется DeterministicRouter для маршрутизации без embedding в edge/offline-деплоях.
    ///     Примеры: "code", "writing", "qa", "analysis", "translation", "data", "math".
    ///     Сериализуется как "input_types" в skill.meta.json.
    /// </summary>
    [JsonPropertyName("input_types")]
    public List<string> InputTypes { get; set; } = new();

    /// <summary>
    ///     Task 023: Теги навыка для детерминированной маршрутизации.
    ///     Используется DeterministicRouter (без embedding) для offline/edge-деплоев.
    ///     Примеры: "python", "api", "debug", "web", "cli".
    ///     Сериализуется как "tags" в skill.meta.json.
    /// </summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    // ─── Task 20: Skill Manifest & Compatibility ────────────────────────────────────

    /// <summary>
    ///     Автор или maintainer навыка.
    ///     Сериализуется как "owner" в skill.meta.json.
    /// </summary>
    public string? Owner { get; set; }

    /// <summary>
    ///     Минимальная совместимая версия Hercules (semver, например "1.0.0").
    ///     Если Hercules старше этой версии — навык несовместим.
    ///     Сериализуется как "min_hercules_version".
    /// </summary>
    [JsonPropertyName("min_hercules_version")]
    public string? MinHerculesVersion { get; set; }

    /// <summary>
    ///     Максимальная совместимая версия Hercules (semver).
    ///     Null = без ограничения.
    ///     Сериализуется как "max_hercules_version".
    /// </summary>
    [JsonPropertyName("max_hercules_version")]
    public string? MaxHerculesVersion { get; set; }

    /// <summary>
    ///     Версия схемы входных данных навыка (semver).
    ///     Сериализуется как "input_schema_version".
    /// </summary>
    [JsonPropertyName("input_schema_version")]
    public string? InputSchemaVersion { get; set; }

    /// <summary>
    ///     Версия схемы выходных данных навыка (semver).
    ///     Сериализуется как "output_schema_version".
    /// </summary>
    [JsonPropertyName("output_schema_version")]
    public string? OutputSchemaVersion { get; set; }

    /// <summary>
    ///     Запрошенные permissions навыка: Read, Write, Network, Memory, Shell, CodeExecution, etc.
    ///     Сериализуется как "permissions".
    /// </summary>
    public List<string> Permissions { get; set; } = new();

    /// <summary>
    ///     Минимальные требования к LLM-модели.
    ///     Сериализуется как "model_requirements".
    /// </summary>
    [JsonPropertyName("model_requirements")]
    public string? ModelRequirements { get; set; }

    /// <summary>
    ///     Уровень риска навыка: Low=0, Medium=1, High=2, Critical=3.
    ///     Сериализуется как "risk_level" (int).
    /// </summary>
    [JsonPropertyName("risk_level")]
    public int RiskLevel { get; set; } = 0;

    /// <summary>
    ///     Декларативные бюджетные лимиты навыка.
    ///     Сериализуется как "budget" (inline object).
    /// </summary>
    public SkillMetaBudget? Budget { get; set; }
}

/// <summary>
///     Полная модель навыка: метаданные + описание + system prompt.
/// </summary>
public sealed class Skill
{
    public SkillMeta Meta { get; set; } = new();

    /// <summary>Markdown-описание (что делает навык, когда вызывать).</summary>
    public string Description { get; set; } = "";

    /// <summary>System prompt навыка.</summary>
    public string Prompt { get; set; } = "";
}

/// <summary>Запись об одном использовании навыка (usage.json).</summary>
public sealed class SkillUsage
{
    public string Timestamp { get; set; } = DateTime.UtcNow.ToString("o");

    /// <summary>Признак успеха использования (true/false).</summary>
    public bool Success { get; set; } = true;

    /// <summary>Уверенность ответа: high/medium/low.</summary>
    public string Confidence { get; set; } = "medium";

    /// <summary>
    ///     Продолжительность выполнения навыка в миллисекундах.
    ///     Используется LatencyScorer для приоритизации быстрых навыков (task_022).
    /// </summary>
    public int LatencyMs { get; set; } = 0;
}

/// <summary>Запись взаимодействия для лога SQLite.</summary>
public sealed record InteractionLog(
    string SessionId,
    string Input,
    string Output,
    string Confidence,
    string Mode, // skill | direct
    string? SkillId,
    string Provider,
    DateTime CreatedAt);

/// <summary>
///     Запись одного бюджетного списания (один LLM-вызов).
///     Хранится в SQLite. Ключ — session_id + created_at.
/// </summary>
public sealed record BudgetEntry(
    long Id,
    string SessionId,
    string Provider,     // "yandexgpt", "ollama-cloud", etc.
    string Model,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd,
    DateTime CreatedAt);

/// <summary>
///     Сводка по бюджету за период.
/// </summary>
public sealed record BudgetSummary(
    int TotalCalls,
    int TotalInputTokens,
    int TotalOutputTokens,
    decimal TotalCostUsd);

/// <summary>
///     Один аудит-лог: фиксация действия агента или пользователя.
///     Расширен в task_014 для поддержки enriched fields: requestId, tool name, policy decision, payload hash.
/// </summary>
public sealed record AuditLogEntry(
    long Id,
    string Actor,        // "agent", "user", "system"
    string Action,       // "skill_created", "skill_deleted", "config_changed", etc.
    string? Target,      // skill_id, session_id, etc.
    string? Details,     // JSON payload
    string? SessionId,
    DateTime CreatedAt)
{
    /// <summary>Уникальный request ID (для traceability конкретного вызова).</summary>
    public string? RequestId { get; init; }

    /// <summary>Имя tool'а, если событие связано с tool execution.</summary>
    public string? ToolName { get; init; }

    /// <summary>Результат policy-проверки: Allowed, Denied, NeedsApproval.</summary>
    public string? PolicyDecision { get; init; }

    /// <summary>Использованные permissions (comma-separated).</summary>
    public string? PermissionUsed { get; init; }

    /// <summary>Результат действия: success, failure, timeout, denied.</summary>
    public string? Result { get; init; }

    /// <summary>SHA256-хеш payload (first 16 hex chars) для integrity verification.</summary>
    public string? PayloadHash { get; init; }
}

/// <summary>
///     Результат оценки навыка (SkillEvaluationEngine).
///     Хранится в SQLite для истории и анализа трендов.
/// </summary>
public sealed record SkillEvaluationRecord(
    long Id,
    string SkillId,
    double Score,        // 0..1
    bool Passed,
    string? TestResults,  // JSON array: [{test, passed, duration_ms, error?}]
    string EvaluatorProvider,
    DateTime CreatedAt);

/// <summary>
///     Устойчивое состояние задачи (для durable task lifecycle).
///     Таблица: task_states.
/// </summary>
public sealed record TaskState(
    long Id,
    string TaskId,
    string Status,       // pending | in_progress | done | blocked | failed
    string? Result,
    string? Error,
    string? Metadata,    // JSON: tags, priority, assignee, etc.
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>
///     Metadata for layered memory entries (sidecar JSON for Markdown memory files).
///     Stored as {filename}.meta.json alongside each Markdown memory file.
/// </summary>
/// <remarks>
///     Used by DurableFactsService and MemoryStore.WriteWithMetadata.
/// </remarks>
public sealed record MemoryEntryMetadata
{
    public string Key { get; set; } = "";
    public string Source { get; set; } = "unknown";
    public string Confidence { get; set; } = "Medium";
    public int TtlMinutes { get; set; } = 0;
    public string Sensitivity { get; set; } = "Internal";
    public List<string>? Tags { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
///     Запрос на подтверждение выполнения tool (human-in-the-loop approval).
///     Таблица: approval_requests.
/// </summary>
public sealed record ApprovalRequest(
    string Id,
    string SessionId,
    string ToolName,
    string ArgumentsJson,
    string PolicyDecision,   // "RequiresApproval"
    string Reason,
    DateTime RequestedAt,
    string? RequestedBy,     // "user" or "agent:sessionId"
    string Status,           // Pending | Approved | Denied | Expired
    DateTime? ApprovedAt,
    DateTime? DeniedAt);

/// <summary>
///     Декларативные бюджетные лимиты навыка (сериализуемые в skill.meta.json).
///     Дублирует Hercules.Skills.SkillManifestBudget для Storage-модели.
/// </summary>
public sealed class SkillMetaBudget
{
    /// <summary>Максимум токенов на один вызов навыка.</summary>
    [JsonPropertyName("max_tokens_per_call")]
    public int MaxTokensPerCall { get; set; } = 0;

    /// <summary>Максимум вызовов в минуту.</summary>
    [JsonPropertyName("max_calls_per_minute")]
    public int MaxCallsPerMinute { get; set; } = 0;

    /// <summary>Максимум стоимости на один вызов (USD).</summary>
    [JsonPropertyName("max_cost_per_call_usd")]
    public decimal MaxCostPerCallUsd { get; set; } = 0;
}
