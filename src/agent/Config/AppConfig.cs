using System.Text;
using System.Text.Json.Serialization;
using Hercules.Cache;
using Hercules.Mesh.Auth;
using Hercules.Mesh.Router;
using Hercules.Mesh.Transport;
using Hercules.Mesh.Audit;
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
    public MarketplaceConfig Marketplace { get; set; } = new();
    public MeshConfig Mesh { get; set; } = new();
    public ToolPolicyConfig ToolPolicy { get; set; } = new();
    public ToolRegistryConfig ToolRegistry { get; set; } = new();
    public ApprovalConfig Approval { get; set; } = new();
    public MemoryConfig Memory { get; set; } = new();
    public BudgetConfig Budget { get; set; } = new();
    public OtelConfig Otel { get; set; } = new();
    public AuditConfig Audit { get; set; } = new();
    public SecretsConfig Secrets { get; set; } = new();
    public EvalConfig Eval { get; set; } = new();
    public SelfImprovementConfig SelfImprovement { get; set; } = new();
    public TaskConfig Tasks { get; set; } = new();
    public LeastPrivilegeConfig LeastPrivilege { get; set; } = new();
    public ContextConfig Context { get; set; } = new();
    public CacheConfig Cache { get; set; } = new();
    public SkillQualityConfig SkillQuality { get; set; } = new();
}

/// <summary>
///     Конфигурация least-privilege grants (task_026).
///     Контролирует, проверяются ли permissions навыков при импорте и runtime.
/// </summary>
public sealed class LeastPrivilegeConfig
{
    /// <summary>Включить проверку grants. Если false — все навыки работают без ограничений.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Режим миграции: если true, навыки без declared permissions считаются разрешёнными
    ///     (обратная совместимость). Если false — такие навыки получают только Read grant.
    /// </summary>
    public bool MigrationMode { get; set; } = true;

    /// <summary>
    ///     Проверять permissions при импорте навыка. Если навык запрашивает permission,
    ///     не входящий в AllowedPermissions — импорт блокируется.
    /// </summary>
    public bool EnforceOnImport { get; set; } = true;

    /// <summary>
    ///     Проверять permissions при runtime (перед tool execution).
    ///     Если навык не имеет нужного grant — tool denied.
    /// </summary>
    public bool EnforceOnRuntime { get; set; } = true;

    /// <summary>
    ///     Список разрешённых permission-строк для импорта навыков.
    ///     Навык импортируется только если его Permissions ⊆ AllowedPermissions.
    ///     Пустой список = все permissions разрешены.
    /// </summary>
    public List<string> AllowedPermissions { get; set; } = new();
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
///     Конфигурация реестра инструментов (task_024).
///     Allow/deny patterns, health check interval, tool discovery directory.
/// </summary>
public sealed class ToolRegistryConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>Директория с tool declarations (data/Tools/*.tool.json).</summary>
    public string ToolsDir { get; set; } = "data/Tools";

    /// <summary>Allow patterns (glob). Пусто = все разрешены.</summary>
    public List<string> AllowedPatterns { get; set; } = new() { "*" };

    /// <summary>Deny patterns (glob, evaluated after allow).</summary>
    public List<string> DeniedPatterns { get; set; } = new();

    /// <summary>Интервал health check в секундах (0 = выключен).</summary>
    public int HealthCheckIntervalSeconds { get; set; } = 60;

    /// <summary>Дефолтный timeout для tools без override.</summary>
    public int DefaultTimeoutSeconds { get; set; } = 30;

    /// <summary>Число последовательных ошибок до Unhealthy статуса.</summary>
    public int ConsecutiveFailureThreshold { get; set; } = 3;
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
    ///     Веса scoring-компонентов для семантической маршрутизации (task_022).
    ///     Key = имя компонента, Value = вес [0..1]. Сумма не обязана равняться 1 (нормализуется в engine).
    ///     Компоненты с весом 0 пропускаются.
    /// </summary>
    public Dictionary<string, double> SkillScoringWeights { get; set; } = new()
    {
        ["embedding"] = 0.40,
        ["lexical"]   = 0.20,
        ["schema"]    = 0.15,
        ["quality"]    = 0.15,
        ["latency"]   = 0.05,
        ["policy"]     = 0.05,
    };

    /// <summary>
    ///     Настройки манифеста навыка (task_020): версия Hercules, разрешённые уровни риска.
    /// </summary>
    public SkillManifestConfig SkillManifest { get; set; } = new();

    /// <summary>
    ///     Task 023: Настройки детерминированного fallback-маршрутизатора (без embedding).
    ///     Используется для offline/edge-деплоев, где embedding-провайдер недоступен.
    /// </summary>
    public DeterministicRoutingConfig DeterministicRouting { get; set; } = new();
}

/// <summary>
///     Task 023: Режим fallback для DeterministicRouter.
/// </summary>
public enum DeterministicFallbackMode
{
    /// <summary>Не использовать DeterministicRouter никогда.</summary>
    Never,

    /// <summary>Использовать DeterministicRouter когда embedding недоступен или semantic routing выключен.</summary>
    OnNoEmbedding,

    /// <summary>Всегда использовать DeterministicRouter (полный offline-режим).</summary>
    Always
}

/// <summary>
///     Task 023: Конфигурация детерминированного маршрутизатора (без embedding).
///     Поддерживает keyword triggers, tags и declared input types.
/// </summary>
public sealed class DeterministicRoutingConfig
{
    /// <summary>
    ///     Режим использования DeterministicRouter: Never / OnNoEmbedding / Always.
    ///     Default: OnNoEmbedding (embeds legacy KeywordFallback behaviour).
    /// </summary>
    public DeterministicFallbackMode FallbackMode { get; set; } = DeterministicFallbackMode.OnNoEmbedding;

    /// <summary>
    ///     Включить маршрутизацию по tags (tag intersection).
    ///     Default: true.
    /// </summary>
    public bool EnableTagMatching { get; set; } = true;

    /// <summary>
    ///     Включить маршрутизацию по declared input types.
    ///     Default: true.
    /// </summary>
    public bool EnableInputTypeMatching { get; set; } = true;

    /// <summary>
    ///     Веса для компонентов детерминированного scoring.
    ///     keyword: вес keyword matching (по phrase_receivers).
    ///     tag: вес tag intersection.
    ///     type: вес input type matching.
    /// </summary>
    public Dictionary<string, double> ScoringWeights { get; set; } = new()
    {
        ["keyword"] = 0.60,
        ["tag"]     = 0.25,
        ["type"]    = 0.15,
    };
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

    /// <summary>Enable this MCP server (client connections and/or in-process hosting). Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Enable health-check polling for this server. Default: true.</summary>
    public bool HealthCheckEnabled { get; set; } = true;

    /// <summary>Timeout for tool calls in seconds. Default: 30.</summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
///     A2A-клиент: Agent-to-Agent протокол (JSON-RPC 2.0).
/// </summary>
public sealed class A2AConfig
{
    /// <summary>Known peer agents: name → base URL для JSON-RPC вызовов.</summary>
    public Dictionary<string, string> Endpoints { get; set; } = new();

    /// <summary>Timeout для A2A HTTP-вызовов в секундах.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Agent Card publishing и discovery настройки.</summary>
    public A2AAgentCardConfig AgentCard { get; set; } = new();

    /// <summary>Remote discovery: список URLs для автодискавери Agent Cards.</summary>
    public A2ADiscoveryConfig Discovery { get; set; } = new();
}

/// <summary>
///     Agent Card publishing/discovery настройки.
/// </summary>
public sealed class A2AAgentCardConfig
{
    /// <summary>Публиковать локальный Agent Card в файл. Default: true.</summary>
    public bool Publish { get; set; } = true;

    /// <summary>
    ///     Путь/endpoint для публикации Agent Card (по умолчанию "/agent-card.json").
    ///     Может быть абсолютным путём или относительным (от dataRoot).
    /// </summary>
    public string Endpoint { get; set; } = "agent-card.json";

    /// <summary>TTL кэша Agent Card в минутах. Default: 60.</summary>
    public int CacheTtlMinutes { get; set; } = 60;
}

/// <summary>
///     Remote discovery настройки.
/// </summary>
public sealed class A2ADiscoveryConfig
{
    /// <summary>Список URLs для автоматического дискавери remote Agent Cards.</summary>
    public List<string> Endpoints { get; set; } = new();

    /// <summary>Включить auto-discovery при старте. Default: false.</summary>
    public bool AutoDiscover { get; set; } = false;

    /// <summary>Интервал auto-refresh в минутах (0 = выключен). Default: 0.</summary>
    public int RefreshIntervalMinutes { get; set; } = 0;
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

    /// <summary>Версии протокола, поддерживаемые агентом (для публикации в манифесте).</summary>
    public List<string> SupportedProtocolVersions { get; set; } = new() { "1.0" };

    /// <summary>Resource limits агента (для публикации в манифесте).</summary>
    public ManifestResourceLimitsConfig? ResourceLimits { get; set; }

    /// <summary>Trust metadata агента (для публикации в манифесте).</summary>
    public ManifestTrustMetadataConfig? TrustMetadata { get; set; }

    /// <summary>Интервал health-check агентов в capability registry (секунды, 0 = выключен).</summary>
    public int CapabilityHealthCheckIntervalSeconds { get; set; } = 60;

    /// <summary>Порог последовательных ошибок health-check для статуса Unhealthy.</summary>
    public int CapabilityHealthFailureThreshold { get; set; } = 3;

    /// <summary>Default TTL для агентов в capability registry (секунды, default = 86400).</summary>
    public int CapabilityDefaultTtlSeconds { get; set; } = 86_400;

    /// <summary>Transport layer config (HTTP, gRPC, Bus adapters). task_037.</summary>
    public TransportConfig Transport { get; set; } = new();

    /// <summary>Discovery mechanisms config (static peers, registry, mDNS). task_038.</summary>
    public DiscoveryConfig Discovery { get; set; } = new();

    /// <summary>Inter-agent auth config (bearer tokens, API keys, mTLS). task_039.</summary>
    public PeerAuthConfig PeerAuth { get; set; } = new();

    /// <summary>Trust admission policy config (intent allow-lists, classification, schema, budget). task_040.</summary>
    public TrustAdmissionConfig TrustAdmission { get; set; } = new();

    /// <summary>Inter-agent audit trail config (task_041).</summary>
    public MeshAuditConfig InterAgentAudit { get; set; } = new();

    /// <summary>Mesh router config (capability routing, peer scoring, health tracking). task_043.</summary>
    public MeshRouterOptions MeshRouter { get; set; } = new();

    /// <summary>Complexity router config (task_044): complexity classification, execution path selection, cost budgets.</summary>
    public ComplexityRouterOptions ComplexityRouter { get; set; } = new();
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
///     Resource limits агента для публикации в манифесте (task_032).
/// </summary>
public sealed class ManifestResourceLimitsConfig
{
    public int MaxTokensPerRequest { get; set; }
    public int MaxConcurrentRequests { get; set; }
    public int MaxToolCallsPerRequest { get; set; }
    public int MaxWallClockSecondsPerRequest { get; set; }
    public decimal MaxCostPerDayUsd { get; set; }
    public int MaxTokensPerDay { get; set; }
}

/// <summary>
///     Trust metadata агента для публикации в манифесте (task_032).
/// </summary>
public sealed class ManifestTrustMetadataConfig
{
    public string Level { get; set; } = "unverified";
    public string IdentityProvider { get; set; } = "self-signed";
    public string? VerifiedBy { get; set; }
    public Dictionary<string, string> IdentityClaims { get; set; } = new();
}

/// <summary>
///     Конфигурация discovery mechanisms (task_038).
///     Static peers, capability registry, and mDNS/Bonjour discovery.
/// </summary>
public sealed class DiscoveryConfig
{
    /// <summary>Включить автоматический discovery при старте агента. Default: true.</summary>
    public bool AutoDiscoverOnStart { get; set; } = true;

    /// <summary>Включить mDNS/Bonjour discovery (requires mDNSResponder / Bonjour). Default: false.</summary>
    public bool EnableMdns { get; set; } = false;

    /// <summary>mDNS service type для mesh-агентов. Default: "_hercules._tcp".</summary>
    public string MdnsServiceType { get; set; } = "_hercules._tcp";

    /// <summary>TTL кэша discovery-результатов в секундах. Default: 300 (5 минут).</summary>
    public int CacheTtlSeconds { get; set; } = 300;

    /// <summary>Интервал auto-refresh discovery в секундах (0 = выключен). Default: 0.</summary>
    public int AutoRefreshIntervalSeconds { get; set; } = 0;
}

/// <summary>
///     Конфигурация trust admission policy (task_040).
///     Управляет allow-listing агентов, intent-фильтрацией, data classification,
///     schema version compatibility и resource budget limits для inter-agent вызовов.
/// </summary>
public sealed class TrustAdmissionConfig
{
    /// <summary>Включить trust admission policy. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Enforcement mode: Enforce / DryRun / Disabled.
    ///     Default: DryRun (логирует, но не блокирует — чтобы не сломать существующие deployments).
    /// </summary>
    public string PolicyMode { get; set; } = "DryRun";

    /// <summary>
    ///     Minimal trust level required for incoming requests.
    ///     Values: "Unverified", "ProvisionallyTrusted", "Trusted", "Verified".
    ///     Empty list = no trust level check.
    /// </summary>
    public List<string> AllowedTrustLevels { get; set; } = new();

    /// <summary>
    ///     Allowed intents for inter-agent calls.
    ///     Empty list = all intents allowed.
    /// </summary>
    public List<string> AllowedIntents { get; set; } = new();

    /// <summary>
    ///     Allowed data classifications: "Public", "Internal", "Confidential", "Restricted".
    ///     Empty list = all classifications allowed.
    /// </summary>
    public List<string> AllowedClassifications { get; set; } = new();

    /// <summary>
    ///     Allow requests when caller schema version differs from target minimum.
    ///     Default: true (permissive, to ease migration).
    /// </summary>
    public bool AllowSchemaMismatch { get; set; } = true;

    /// <summary>
    ///     Allow requests that exceed target declared resource limits.
    ///     Default: true (permissive — target enforces its own limits).
    /// </summary>
    public bool AllowBudgetExceeded { get; set; } = true;

    /// <summary>
    ///     Allowed risk levels for incoming requests (from skill metadata).
    ///     Values: "low", "medium", "high", "critical".
    ///     Empty list = no risk level check.
    /// </summary>
    public List<string> AllowedRiskLevels { get; set; } = new();
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
///     Конфигурация quality score для навыков (task_029).
///     Управляет весами метрик, минимальным размером выборки и decay.
/// </summary>
public sealed class SkillQualityConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Веса компонентов composite quality score.
    ///     Сумма не обязана равняться 1 (нормализуется в SkillQualityService).
    /// </summary>
    public Dictionary<string, double> ScoreWeights { get; set; } = new()
    {
        ["acceptanceRate"]     = 0.25,
        ["testScore"]         = 0.30,
        ["userCorrectionRate"] = 0.20,
        ["fallbackRate"]      = 0.15,
        ["latency"]           = 0.05,
        ["cost"]              = 0.05,
    };

    /// <summary>
    ///     Минимальное число вызовов навыка для достоверного quality score.
    ///     При меньшем числе вызовов score = 1.0 (neutral).
    /// </summary>
    public int MinSampleSize { get; set; } = 10;

    /// <summary>
    ///     Коэффициент decay для версий старше текущей: score *= ScoreDecayPerVersion^(versionGap).
    /// </summary>
    public double ScoreDecayPerVersion { get; set; } = 0.95;

    /// <summary>
    ///     Минимальный quality score для promotion. Если computed score &lt; этого значения — promotion блокируется.
    /// </summary>
    public double MinScoreForPromotion { get; set; } = 0.40;
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

/// <summary>
///     Конфигурация маркетплейса навыков (task_021).
///     Подпись пакетов, импорт по HTTP, лимиты размера.
/// </summary>
public sealed class MarketplaceConfig
{
    /// <summary>
    ///     Секретный ключ для HMAC-SHA256 подписи пакетов (base64-encoded).
    ///     Если null или пусто — подпись не выполняется.
    /// </summary>
    public string? SigningKey { get; set; }

    /// <summary>
    ///     Требовать валидную подпись при импорте.
    ///     Если true и пакет не подписан — импорт отклоняется.
    /// </summary>
    public bool RequireSignature { get; set; } = false;

    /// <summary>
    ///     Разрешить импорт пакетов по HTTP URL.
    /// </summary>
    public bool AllowHttpImport { get; set; } = false;

    /// <summary>
    ///     Максимальный размер пакета в мегабайтах.
    /// </summary>
    public int MaxPackageSizeMb { get; set; } = 50;

    /// <summary>
    ///     Base URL публичного репозитория шаблонов (опционально).
    ///     Если задан — /marketplace import-url загружает отсюда.
    /// </summary>
    public string? TemplateRepositoryUrl { get; set; }
}
