using System.Text;
using System.Text.Json.Serialization;
using Hercules.Tools.Policy;

namespace Hercules.Config;

/// <summary>
///     Корневая конфигурация приложения (маппится из appsettings.json).
/// </summary>
public sealed class AppConfig
{
    public LlmConfig Llm { get; set; } = new();
    public StorageConfig Storage { get; set; } = new();
    public AgentConfig Agent { get; set; } = new();
    public TelegramConfig Telegram { get; set; } = new();
    public CodeExecutionConfig CodeExecution { get; set; } = new();
    public HttpConfig Http { get; set; } = new();
    public McpConfig Mcp { get; set; } = new();
    public A2AConfig A2A { get; set; } = new();
    public Dictionary<string, RoleConfig> Roles { get; set; } = new();
    public Phase2Config Phase2 { get; set; } = new();
    public MeshConfig Mesh { get; set; } = new();
    public ToolPolicyConfig ToolPolicy { get; set; } = new();
    public ApprovalConfig Approval { get; set; } = new();
    public MemoryConfig Memory { get; set; } = new();
    public BudgetConfig Budget { get; set; } = new();
    public OtelConfig Otel { get; set; } = new();
    public AuditConfig Audit { get; set; } = new();
    public SecretsConfig Secrets { get; set; } = new();
    public EvalConfig Eval { get; set; } = new();
    public SelfImprovementConfig SelfImprovement { get; set; } = new();
    public TaskConfig Tasks { get; set; } = new();
}

/// <summary>
///     Конфигурация tool policy engine (task_009).
///     Side-effect level, permissions, deny rules, approval thresholds.
/// </summary>
public sealed class ToolPolicyConfig
{
    public bool DryRun { get; set; } = false;
    public bool AllowUnknownTools { get; set; } = false;
    public SideEffectLevel MinSideEffectLevelForApproval { get; set; } = SideEffectLevel.External;
    public List<string> DeniedTools { get; set; } = new();
    public string AgentPermissions { get; set; } = "Read|Write|Network|Memory";
    public int DefaultTimeoutSeconds { get; set; } = 30;
}

/// <summary>
///     Настройки Phase 2: семантическая маршрутизация, маркетплейс навыков, реестр инструментов.
/// </summary>
public sealed class Phase2Config
{
    public bool SemanticRoutingEnabled { get; set; } = false;
    public string EmbeddingProvider { get; set; } = "stub-hash";
    public double SimilarityThreshold { get; set; } = 0.35;
    public bool KeywordFallback { get; set; } = true;
    public string MarketplaceDir { get; set; } = "marketplace";
    public string ToolsDir { get; set; } = "Tools";
    public string TemplatesDir { get; set; } = "Templates";

    /// <summary>
    ///     Настройки манифеста навыка (task_020): версия Hercules, разрешённые уровни риска.
    /// </summary>
    public SkillManifestConfig SkillManifest { get; set; } = new();
}

/// <summary>
///     Параметры одной LLM-роли.
/// </summary>
public sealed class RoleConfig
{
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public float Temperature { get; set; } = 0.6f;
    public int MaxTokens { get; set; } = 2000;
}

/// <summary>
///     Параметры sandbox для исполнения LLM-кода (Stage 2).
/// </summary>
public sealed class CodeExecutionConfig
{
    public int CpuTimeoutSeconds { get; set; } = 30;
    public int MaxFileSizeMb { get; set; } = 10;
    public int MaxProcesses { get; set; } = 0;
    public int MaxOpenFiles { get; set; } = 1024;
    public long MaxVirtualMemoryMb { get; set; } = 0;
    public bool AllowNetwork { get; set; } = false;
    public int MaxCodeSizeKb { get; set; } = 100;
    public int SessionTtlSeconds { get; set; } = 3600;
    public string TempRoot { get; set; } = "";
}

/// <summary>
///     HTTP-инструмент: безопасные исходящие запросы с allow-list.
/// </summary>
public sealed class HttpConfig
{
    public List<string> AllowedDomains { get; set; } = ["*"];
    public int RateLimitPerMinute { get; set; } = 60;
    public int TimeoutSeconds { get; set; } = 10;
    public int MaxResponseSizeKb { get; set; } = 256;
}

/// <summary>
///     MCP-клиент: подключение к Model Context Protocol серверам.
/// </summary>
public sealed class McpConfig
{
    public List<McpServerConfig> Servers { get; set; } = new();
}

/// <summary>
///     Конфигурация одного MCP-сервера.
/// </summary>
public sealed class McpServerConfig
{
    public string Name { get; set; } = "";
    public string Transport { get; set; } = "stdio";
    public string? Command { get; set; }
    public List<string> Args { get; set; } = new();
    public string? Endpoint { get; set; }
}

/// <summary>
///     A2A-клиент: Agent-to-Agent протокол (JSON-RPC 2.0).
/// </summary>
public sealed class A2AConfig
{
    public Dictionary<string, string> Endpoints { get; set; } = new();
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
///     Конфигурация LLM-провайдеров.
/// </summary>
public sealed class LlmConfig
{
    public string Provider { get; set; } = "yandexgpt";
    public List<string> Fallback { get; set; } = ["ollama-cloud", "ollama-local"];
    public YandexGptConfig YandexGpt { get; set; } = new();
    public OllamaConfig OllamaCloud { get; set; } = new();
    public OllamaConfig OllamaLocal { get; set; } = new();
    public OpenAICompatibleConfig OpenAICompatible { get; set; } = new();
}

/// <summary>
///     Конфигурация OpenAI-совместимого провайдера.
/// </summary>
public sealed class OpenAICompatibleConfig
{
    public string Endpoint { get; set; } = "http://localhost:1234/v1";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "llama3.1";
    public float Temperature { get; set; } = 0.6f;
    public int MaxTokens { get; set; } = 2000;
    public string DisplayName { get; set; } = "OpenAI-Compatible";
    public string Description { get; set; } = "OpenAI-compatible endpoint (LM Studio, etc.)";
}

/// <summary>
///     Параметры YandexGPT (OpenAI-совместимый endpoint).
/// </summary>
public sealed class YandexGptConfig
{
    public string Endpoint { get; set; } = "https://llm.api.cloud.yandex.net/v1";
    public string ApiKey { get; set; } = "";
    public string FolderId { get; set; } = "";
    public string Model { get; set; } = "yandexgpt";
    public float Temperature { get; set; } = 0.6f;
    public int MaxTokens { get; set; } = 2000;
}

/// <summary>
///     Параметры Ollama (Cloud или Local).
/// </summary>
public sealed class OllamaConfig
{
    public string Endpoint { get; set; } = "http://localhost:11434/v1";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "llama3.1";
    public float Temperature { get; set; } = 0.6f;
    public int MaxTokens { get; set; } = 2000;
}

/// <summary>
///     Пути к хранилищам данных.
/// </summary>
public sealed class StorageConfig
{
    public string DataRoot { get; set; } = "data";
    public string SkillsDir { get; set; } = "Skills";
    public string MemoryDir { get; set; } = "Memory";
    public string SqliteFile { get; set; } = "sessions.db";
    public Phase2Config? Phase2 { get; set; }
}

/// <summary>
///     Пороговые значения поведения агента.
/// </summary>
public sealed class AgentConfig
{
    public string SystemPrompt { get; set; } =
        "Ты — Hercules, самообучающийся ассистент. Отвечай кратко, по делу и на русском языке.";
    public int SkillCreationThreshold { get; set; } = 3;
    public double SkillImprovementThreshold { get; set; } = 0.6;
    public int SkillEvaluationWindow { get; set; } = 5;
    public int ReflectionEveryNCommands { get; set; } = 10;
    public int MaxToolIterations { get; set; } = 3;
    public int MaxWallClockTimeoutSeconds { get; set; } = 120;
    public int MaxRecursionDepth { get; set; } = 2;
}

/// <summary>
///     Параметры Telegram-бота.
/// </summary>
public sealed class TelegramConfig
{
    public bool Enabled { get; set; } = false;
    public string BotToken { get; set; } = "";
}

/// <summary>
///     Конфигурация Phase 3: inter-agent mesh.
/// </summary>
public sealed class MeshConfig
{
    public bool Enabled { get; set; } = false;
    public string AgentId { get; set; } = "hercules-main";
    public string DisplayName { get; set; } = "Hercules";
    public string Description { get; set; } = "Self-improving micro-agent";
    public string Endpoint { get; set; } = "http://localhost:5000";
    public string RegistryDb { get; set; } = "mesh_registry.db";
    public int IntentTimeoutMs { get; set; } = 30_000;
    public double LocalConfidenceThreshold { get; set; } = 0.5;
    public List<MeshPeerConfig> Peers { get; set; } = new();
    public List<ManifestCapabilityConfig>? Capabilities { get; set; }
}

/// <summary>
///     Конфигурация одного peer-агента для статического discovery.
/// </summary>
public sealed class MeshPeerConfig
{
    public string AgentId { get; set; } = "";
    public string Endpoint { get; set; } = "";
}

/// <summary>
///     Capability в конфигурации peer-агента.
/// </summary>
public sealed class ManifestCapabilityConfig
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    [JsonPropertyName("phrase_receivers")] public List<string> PhraseReceivers { get; set; } = new();
}

/// <summary>
///     Конфигурация approval gates (task_010).
/// </summary>
public sealed class ApprovalConfig
{
    public bool Enabled { get; set; } = true;
    public int DefaultTtlMinutes { get; set; } = 30;
    public int MaxPending { get; set; } = 50;
}

/// <summary>
///     Конфигурация layered memory (task_011).
/// </summary>
public sealed class MemoryConfig
{
    public int MaxWorkingMemoryEntries { get; set; } = 100;
    public int MaxEpisodesInContext { get; set; } = 5;
    public int DefaultFactTtlMinutes { get; set; } = 0;
    public bool SensitivityRedactionEnabled { get; set; } = true;
    public int MaxFactAgeDays { get; set; } = 0;
}

/// <summary>
///     Конфигурация бюджетов и guardrails (task_012).
/// </summary>
public sealed class BudgetConfig
{
    public int MaxTokensPerRequest { get; set; } = 0;
    public int MaxToolCallsPerRequest { get; set; } = 0;
    public int MaxRetriesPerTool { get; set; } = 0;
    public int MaxWallClockSecondsPerRequest { get; set; } = 0;
    public decimal MaxCostPerDayUsd { get; set; } = 0;
    public int MaxTokensPerDay { get; set; } = 0;
    public int MaxCallsPerDay { get; set; } = 0;
    public string EnforcementMode { get; set; } = "soft_warn";
    public bool Enabled { get; set; } = true;
}

/// <summary>
///     Конфигурация OpenTelemetry (task_013).
/// </summary>
public sealed class OtelConfig
{
    public bool Enabled { get; set; } = true;
    public string ServiceName { get; set; } = "hercules";
    public string? OtlpEndpoint { get; set; }
    public double SamplingRatio { get; set; } = 1.0;
}

/// <summary>
///     Конфигурация аудита и приватности (task_014).
/// </summary>
public sealed class AuditConfig
{
    public bool Enabled { get; set; } = true;
    public List<string> RedactSensitivityLevels { get; set; } = new() { "high", "medium" };
    public bool PayloadHashEnabled { get; set; } = true;
    public List<string> LogActorActions { get; set; } = new() { "agent", "system", "user" };
    public int RetentionDays { get; set; } = 90;
    public List<AuditRedactionPattern> RedactionPatterns { get; set; } = new();
}

/// <summary>
///     Custom redaction pattern.
/// </summary>
public sealed class AuditRedactionPattern
{
    public string Pattern { get; set; } = "";
    public string Replacement { get; set; } = "***";
}

/// <summary>
///     Конфигурация секретов (task_015).
/// </summary>
public sealed class SecretsConfig
{
    public string EnvironmentVariablePrefix { get; set; } = "HERCULES_SECRET_";
    public string? SecretsFile { get; set; }
    public string SecretReferencePrefix { get; set; } = "env:";
    public bool RedactInExports { get; set; } = true;
    public bool RedactInMemory { get; set; } = true;
    public bool RedactInTelemetry { get; set; } = true;
}

/// <summary>
///     Конфигурация eval harness (task_016).
/// </summary>
public sealed class EvalConfig
{
    public bool BlockOnRegression { get; set; } = true;
    public double BaselineComparisonThreshold { get; set; } = 0.05;
    public int DefaultFixtureCount { get; set; } = 5;
    public bool EnableLlmJudgeCases { get; set; } = false;
}

/// <summary>
///     Конфигурация durable task lifecycle (task_018).
///     Retry policy, concurrent limits, checkpoint retention.
/// </summary>
public sealed class TaskConfig
{
    public int DefaultMaxRetries { get; set; } = 3;
    public int DefaultRetryDelayMs { get; set; } = 1000;
    public double DefaultBackoffMultiplier { get; set; } = 2.0;
    public int MaxConcurrentDurableTasks { get; set; } = 10;
    public int CheckpointRetentionDays { get; set; } = 7;
}

/// <summary>
///     Конфигурация safe self-improvement (task_017).
///     Maintenance workflow, proposal generation, versioned diffs.
/// </summary>
public sealed class SelfImprovementConfig
{
    /// <summary>
    ///     Включить maintenance workflow. Если false — self-improvement отключён.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Порог success_rate, ниже которого навык — кандидат на улучшение.
    /// </summary>
    public double MinSuccessRateThreshold { get; set; } = 0.5;

    /// <summary>
    ///     Требовать approval для применения proposal к production-навыкам.
    ///     Если true — даже LOW_RISK улучшения проходят через human gate.
    /// </summary>
    public bool RequireApprovalForProd { get; set; } = true;

    /// <summary>
    ///     Максимальное число proposals в день. 0 = без ограничения.
    /// </summary>
    public int MaxProposalsPerDay { get; set; } = 5;

    /// <summary>
    ///     Анонимизировать данные перед отправкой в LLM-анализ (удалять user-specific info).
    /// </summary>
    public bool AnonymizeData { get; set; } = true;
}

/// <summary>
///     Конфигурация манифеста навыка (task_020).
///     Текущая версия Hercules, разрешённые уровни риска.
/// </summary>
public sealed class SkillManifestConfig
{
    /// <summary>
    ///     Текущая версия Hercules в semver (например "1.0.0").
    ///     Используется для проверки совместимости навыков при загрузке/импорте.
    /// </summary>
    public string CurrentHerculesVersion { get; set; } = "1.0.0";

    /// <summary>
    ///     Разрешённые уровни риска навыков (0=Low, 1=Medium, 2=High, 3=Critical).
    ///     Если пусто — без ограничений.
    /// </summary>
    public List<int> AllowedRiskLevels { get; set; } = new() { 0, 1, 2 };
}
