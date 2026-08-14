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
///     Конфигурация security operations (task_055).
///     Fleet identity, certificates, package signing, vulnerability reporting, audit export.
/// </summary>
public sealed class SecurityOpsConfig
{
    /// <summary>Включить security operations. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    // Identity rotation
    /// <summary>Директория для хранения fleet identity. Default: "data/security/identity".</summary>
    public string IdentityStoragePath { get; set; } = "data/security/identity";

    /// <summary>Default agent ID для fleet identity. Default: "hercules".</summary>
    public string DefaultAgentId { get; set; } = "hercules";

    /// <summary>Default roles для нового агента. Default: ["agent"].</summary>
    public List<string> DefaultRoles { get; set; } = new() { "agent" };

    /// <summary>Срок действия identity в днях. Default: 90.</summary>
    public int IdentityRotationDays { get; set; } = 90;

    /// <summary>Включить автоматическую ротацию identity. Default: true.</summary>
    public bool AutoRotateIdentity { get; set; } = true;

    // Certificate management
    /// <summary>Директория для хранения сертификатов. Default: "data/security/certs".</summary>
    public string CertificateStoragePath { get; set; } = "data/security/certs";

    /// <summary>Срок действия сертификата в днях. Default: 365.</summary>
    public int CertificateValidityDays { get; set; } = 365;

    /// <summary>Порог дней до истечения для автоматического продления. Default: 30.</summary>
    public int CertificateRenewalThresholdDays { get; set; } = 30;

    /// <summary>Включить автоматическое продление сертификатов. Default: true.</summary>
    public bool AutoRenewCertificates { get; set; } = true;

    // Package signing
    /// <summary>Директория для хранения подписей пакетов. Default: "data/security/signing".</summary>
    public string PackageSigningPath { get; set; } = "data/security/signing";

    /// <summary>Требовать подпись пакетов. Default: false.</summary>
    public bool RequirePackageSignature { get; set; } = false;

    /// <summary>Требовать доверенного подписанта. Default: false.</summary>
    public bool RequireTrustedSigner { get; set; } = false;

    // Vulnerability reporting
    /// <summary>Директория для хранения vulnerability reports. Default: "data/security/vulns".</summary>
    public string VulnerabilityReportPath { get; set; } = "data/security/vulns";

    /// <summary>Включить автоматическое сканирование уязвимостей. Default: false.</summary>
    public bool AutoScanVulnerabilities { get; set; } = false;

    /// <summary>Интервал сканирования уязвимостей в часах. Default: 24.</summary>
    public int VulnerabilityScanIntervalHours { get; set; } = 24;

    // Security monitoring
    /// <summary>Включить security monitoring. Default: true.</summary>
    public bool EnableSecurityMonitoring { get; set; } = true;

    /// <summary>Включить шифрование данных at rest. Default: false.</summary>
    public bool EnableDataEncryptionAtRest { get; set; } = false;
}

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

    // Extended config properties (task_044-055)
    /// <summary>Fan-out / fan-in orchestrator config (task_045): concurrency, budget, schema validation, selection strategy.</summary>
    public FanOutOptions FanOut { get; set; } = new();

    /// <summary>Verification pipeline config (task_046): enabled verifiers, severity thresholds, block list.</summary>
    public VerificationConfig Verification { get; set; } = new();

    /// <summary>Resilience config (task_047): retry, circuit breaker, bulkhead, idempotency.</summary>
    public ResilienceConfig Resilience { get; set; } = new();

    /// <summary>Delegation boundaries config (task_048): hop count, fan-out width, cumulative tool calls, cost, time limits.</summary>
    public DelegationBoundaryConfig DelegationBoundaries { get; set; } = new();

    /// <summary>Human-in-the-loop escalation config (task_049): severity thresholds, TTL, escalation types.</summary>
    public EscalationConfig Escalation { get; set; } = new();

    /// <summary>Distributed reflection proposal config (task_050): proposal generation, LLM analysis, thresholds.</summary>
    public Mesh.ReflectionProposalConfig ReflectionProposals { get; set; } = new();

    /// <summary>Shared memory sync config (task_051): TTL, sensitivity classification, encryption, namespace limits.</summary>
    public SharedMemorySyncConfig SharedMemorySync { get; set; } = new();

    /// <summary>Mesh evaluation suite config (task_052): scenario definitions, metrics, thresholds.</summary>
    public MeshEvalConfig MeshEval { get; set; } = new();

    /// <summary>Centralized mesh observability config (task_054): trace context propagation, mesh-specific span enrichment, OTLP sink.</summary>
    public MeshCentralizedObservabilityConfig CentralizedObservability { get; set; } = new();

    // task_070: Backend profiles and degradation — mesh deployment profiles, backend health monitoring, degradation policies
    /// <summary>Mesh backend profiles config (task_070): active profile, profiles directory, health check settings, degradation alerts.</summary>
    public MeshProfilesConfig MeshProfiles { get; set; } = new();

    /// <summary>Security operations config (task_055): fleet identity, certificates, package signing, vulnerability reporting, audit export.</summary>
    public SecurityOpsConfig SecurityOps { get; set; } = new();

    /// <summary>Rate limits and quotas config (task_056): per-agent, per-skill, per-user, per-tenant limits on calls, tokens, cost, storage, message volume.</summary>
    public QuotasConfig Quotas { get; set; } = new();

    /// <summary>Config and policy rollout config (task_058): signed bundles, staged rollout, expiry, LKG fallback.</summary>
    public ConfigRolloutConfig ConfigRollout { get; set; } = new();

    /// <summary>Edge provisioning config (task_059): enrollment URL, device ID, Wi-Fi, secure defaults.</summary>
    public EdgeConfig Edge { get; set; } = new();

    // task_060: Offline resilience — bounded outbox queue for sensor logs, task results, alerts
    /// <summary>Offline resilience config (task_060): bounded queue, TTL, flush interval, network polling.</summary>
    public Offline.OfflineSyncConfig OfflineSync { get; set; } = new();

    // task_061: Local-first degradation — fallback strategies, operator notifications, observability
    /// <summary>Local-first degradation config (task_061): health checks, fallback strategies, notifications.</summary>
    public Degradation.DegradationConfig Degradation { get; set; } = new();

    // task_063: Backup & Recovery
    /// <summary>Backup config (task_063): encryption, retention, scheduling.</summary>
    public Backup.BackupConfig Backup { get; set; } = new();

    // task_064: Operational SLOs
    /// <summary>SLO config (task_064): SLO definitions directory, alert thresholds.</summary>
    public SlosConfig Slos { get; set; } = new();
}

/// <summary>
///     Конфигурация staged rollout конфигурационных и policy бандлов (task_058).
/// </summary>
public sealed class ConfigRolloutConfig
{
    /// <summary>Включить staged rollout. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Директория для хранения бандлов и state. Default: "data/rollout".</summary>
    public string BundlesPath { get; set; } = "data/rollout";

    /// <summary>Требовать подпись бандлов. Default: false.</summary>
    public bool RequireBundleSignature { get; set; } = false;

    /// <summary>Требовать, чтобы подписант был в trusted signers list. Default: false.</summary>
    public bool RequireTrustedSigner { get; set; } = false;

    /// <summary>Автоматически продвигать в staging после apply. Default: true.</summary>
    public bool AutoPromoteToStaging { get; set; } = true;

    /// <summary>Default signer ID для подписи бандлов. Default: "hercules-agent".</summary>
    public string DefaultSignerId { get; set; } = "hercules-agent";

    /// <summary>Максимальный размер payload бандла в байтах. Default: 5 MB.</summary>
    public int MaxPayloadBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Список staging groups (если bundle.StagingGroup не в списке — apply отклоняется). Пустой = все разрешены.</summary>
    public List<string> AllowedStagingGroups { get; set; } = new();

    /// <summary>Включить автопроверку expiry по таймеру (через BackgroundService). Default: true.</summary>
    public bool EnableExpiryChecker { get; set; } = true;

    /// <summary>Интервал проверки expiry в минутах. Default: 5.</summary>
    public int ExpiryCheckIntervalMinutes { get; set; } = 5;

    /// <summary>Default время на staging в минутах. Default: 60.</summary>
    public int DefaultStagingDurationMinutes { get; set; } = 60;
}

/// <summary>
///     Конфигурация rate limits и quotas (task_056).
///     Per-agent, per-skill, per-user, per-tenant limits.
/// </summary>
public sealed class QuotasConfig
{
    /// <summary>Включить quota enforcement. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Enforcement mode: "soft_warn" или "hard_cap". Default: soft_warn.</summary>
    public string EnforcementMode { get; set; } = "soft_warn";

    // Per-agent limits
    /// <summary>Максимум concurrent requests на агента. Default: 10.</summary>
    public int MaxConcurrentRequestsPerAgent { get; set; } = 10;

    /// <summary>Максимум calls в минуту на агента. Default: 60.</summary>
    public int MaxCallsPerMinutePerAgent { get; set; } = 60;

    /// <summary>Максимум tokens в день на агента. Default: 1000000.</summary>
    public long MaxTokensPerDayPerAgent { get; set; } = 1_000_000;

    /// <summary>Максимум storage в MB на агента. Default: 500.</summary>
    public long MaxStorageMbPerAgent { get; set; } = 500;

    /// <summary>Максимум message volume в день на агента. Default: 10000.</summary>
    public int MaxMessagesPerDayPerAgent { get; set; } = 10_000;

    // Per-skill limits
    /// <summary>Максимум calls в минуту на навык. Default: 30.</summary>
    public int MaxCallsPerMinutePerSkill { get; set; } = 30;

    /// <summary>Максимум concurrent executions на навык. Default: 5.</summary>
    public int MaxConcurrentPerSkill { get; set; } = 5;

    // Per-user limits
    /// <summary>Максимум requests в минуту на user. Default: 20.</summary>
    public int MaxRequestsPerMinutePerUser { get; set; } = 20;

    /// <summary>Максимум daily requests на user. Default: 500.</summary>
    public int MaxRequestsPerDayPerUser { get; set; } = 500;

    // Per-tenant limits
    /// <summary>Максимум total agents в tenant. Default: 50.</summary>
    public int MaxAgentsPerTenant { get; set; } = 50;

    /// <summary>Максимум total calls в минуту на tenant. Default: 1000.</summary>
    public int MaxCallsPerMinutePerTenant { get; set; } = 1000;

    /// <summary>Максимум total cost в день на tenant (USD). Default: 100.</summary>
    public decimal MaxCostPerDayPerTenantUsd { get; set; } = 100m;

    // Sliding window for rate limiting (in seconds)
    /// <summary>Размер sliding window для rate limiting в секундах. Default: 60.</summary>
    public int RateLimitWindowSeconds { get; set; } = 60;
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

    /// <summary>Fleet templates directory (data/FleetTemplates/). Default: "FleetTemplates".</summary>
    public string FleetTemplatesDir { get; set; } = "FleetTemplates";

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
///     Enforcement mode for delegation boundary violations.
/// </summary>
public enum DelegationBoundaryEnforcementMode
{
    /// <summary>Log violations but allow the operation to proceed.</summary>
    SoftWarn,

    /// <summary>Hard block: reject delegations that exceed any boundary.</summary>
    HardCap
}

/// <summary>
///     Конфигурация delegation boundaries (task_048).
///     Управляет hop count, fan-out width, cumulative tool calls, cost и time limits
///     для inter-agent delegation chains.
/// </summary>
public sealed class DelegationBoundaryConfig
{
    /// <summary>Включить enforcement delegation boundaries. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Enforcement mode: SoftWarn (log only) or HardCap (reject). Default: SoftWarn.
    /// </summary>
    public string EnforcementMode { get; set; } = "SoftWarn";

    /// <summary>
    ///     Максимальная глубина делегации (hop count). 0 = без ограничений. Default: 3.
    /// </summary>
    public int MaxHopCount { get; set; } = 3;

    /// <summary>
    ///     Максимальное количество параллельных delegations с одного hop (fan-out width). 0 = без ограничений. Default: 5.
    /// </summary>
    public int MaxFanOutWidth { get; set; } = 5;

    /// <summary>
    ///     Максимум cumulative tool calls за весь delegation chain. 0 = без ограничений. Default: 50.
    /// </summary>
    public int MaxCumulativeToolCalls { get; set; } = 50;

    /// <summary>
    ///     Максимум cumulative cost в USD за весь delegation chain. 0 = без ограничений. Default: 5.00.
    /// </summary>
    public decimal MaxCumulativeCostUsd { get; set; } = 5.00m;

    /// <summary>
    ///     Максимум cumulative wall-clock milliseconds за весь delegation chain. 0 = без ограничений. Default: 300000 (5 min).
    /// </summary>
    public long MaxCumulativeWallClockMs { get; set; } = 300_000;
}

/// <summary>
///     Конфигурация resilience для peer-вызовов: retry, circuit breaker, bulkhead, idempotency.
///     task_047: Retry, timeout, circuit breaker.
/// </summary>
public sealed class ResilienceConfig
{
    /// <summary>Включить resilience-логику. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Максимум попыток (включая первую). Default: 3.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Базовая задержка перед retry (мс). Default: 500.</summary>
    public int BaseDelayMs { get; set; } = 500;

    /// <summary>Множитель экспоненциальной задержки. Default: 2.0.</summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>Максимальная задержка между попытками (мс). Default: 5000.</summary>
    public int MaxDelayMs { get; set; } = 5_000;

    /// <summary>
    ///     Амплитуда jitter (±% от задержки). 0 = без jitter. Default: 0.3 (30%).
    ///     Jitter снимает "thundering herd" — все клиенты ретраятся с разным смещением.
    /// </summary>
    public double JitterFactor { get; set; } = 0.3;

    /// <summary>Порог неудач для размыкания circuit breaker. Default: 5.</summary>
    public int CircuitBreakerFailureThreshold { get; set; } = 5;

    /// <summary>Cooldown circuit breaker перед пробной попыткой (секунды). Default: 60.</summary>
    public int CircuitBreakerCooldownSeconds { get; set; } = 60;

    /// <summary>Максимум одновременных вызовов к одному peer'у (bulkhead). Default: 4.</summary>
    public int MaxConcurrentPerPeer { get; set; } = 4;

    /// <summary>Общий максимум одновременных outbound вызовов. Default: 20.</summary>
    public int MaxConcurrentTotal { get; set; } = 20;

    /// <summary>Idempotency key policy для non-idempotent операций.</summary>
    public IdempotencyKeyConfig IdempotencyKey { get; set; } = new();
}

/// <summary>
///     Политика генерации idempotency keys для retry-safe операций.
/// </summary>
public sealed class IdempotencyKeyConfig
{
    /// <summary>Автогенерировать idempotency key если отсутствует. Default: true.</summary>
    public bool AutoGenerate { get; set; } = true;

    /// <summary>
    ///     Prefixes intent names, при которых idempotency key не нужен (idempotent-safe).
    ///     Операции с этими prefix'ами ретраятся без ограничений.
    /// </summary>
    public List<string> SafeIntents { get; set; } = new() { "read:", "query:", "search:", "get:", "list:" };

    /// <summary>TTL idempotency key на receiver'е (секунды). Default: 300.</summary>
    public int TtlSeconds { get; set; } = 300;
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

/// <summary>
///     Конфигурация verification pipeline (task_046).
///     Verifiers: SafetyVerifier, PolicyVerifier, SchemaVerifier, NumericValidator.
/// </summary>
public sealed class VerificationConfig
{
    /// <summary>Включить verification pipeline. Default: false (backward-compatible).</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Режим: "off" | "dryrun" | "enforce". Default: "dryrun".</summary>
    public string Mode { get; set; } = "dryrun";

    /// <summary>Минимальная severity для блокировки: "info" | "low" | "medium" | "high" | "critical".</summary>
    public string BlockSeverityThreshold { get; set; } = "medium";

    /// <summary>Включить SafetyVerifier (PII, code injection, dangerous patterns). Default: true.</summary>
    public bool EnableSafetyVerifier { get; set; } = true;

    /// <summary>Включить PolicyVerifier (trust admission, tool constraints). Default: true.</summary>
    public bool EnablePolicyVerifier { get; set; } = true;

    /// <summary>Включить SchemaVerifier (JSON structure, error fields). Default: true.</summary>
    public bool EnableSchemaVerifier { get; set; } = true;

    /// <summary>Включить NumericValidator (numeric plausibility). Default: false (may produce false positives).</summary>
    public bool EnableNumericValidator { get; set; } = false;

    /// <summary>Режимы ответов, верифицируемые всегда (помимо tool/low-confidence).</summary>
    public List<string> AlwaysVerifyModes { get; set; } = new();

    /// <summary>Tool'ы, которые всегда блокируются PolicyVerifier'ом.</summary>
    public List<string> BlockedTools { get; set; } = new();

    /// <summary>Требовать approval при low-confidence ответах. Default: false.</summary>
    public bool RequireApprovalOnLowConfidence { get; set; } = false;

    /// <summary>Максимальное время верификации в миллисекундах. Default: 5000.</summary>
    public int MaxVerificationTimeMs { get; set; } = 5000;

    /// <summary>Верифицировать только ответы с confidence ниже этого порога. Default: "high".</summary>
    public string MinConfidenceToSkipVerification { get; set; } = "high";
}

/// <summary>
///     Конфигурация human-in-the-loop escalation (task_049).
///     Severity thresholds, TTL, escalation types, and operator notification.
/// </summary>
public sealed class EscalationConfig
{
    /// <summary>Включить escalation service. Default: false (backward-compatible).</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    ///     Режим: "off" | "notify" | "block". Default: "notify".
    ///     block: ответ блокируется до подтверждения оператором.
    ///     notify: эскалация создаётся, но операция выполняется (с нотификацией).
    /// </summary>
    public string Mode { get; set; } = "notify";

    /// <summary>TTL pending-эскалаций в минутах. Default: 30.</summary>
    public int DefaultTtlMinutes { get; set; } = 30;

    /// <summary>
    ///     Minimal severity that triggers escalation in block mode.
    ///     Values: Low, Medium, High, Critical. Default: Medium.
    /// </summary>
    public string MinSeverityForBlock { get; set; } = "Medium";

    /// <summary>Escalate low-confidence responses (below this threshold). Default: "low".</summary>
    public string LowConfidenceThreshold { get; set; } = "low";

    /// <summary>Escalate destructive operations (SideEffectLevel.Destructive or above). Default: true.</summary>
    public bool EscalateDestructive { get; set; } = true;

    /// <summary>Escalate trust admission denials. Default: true.</summary>
    public bool EscalateTrustDenials { get; set; } = true;

    /// <summary>Escalate budget guardrail violations. Default: true.</summary>
    public bool EscalateBudgetViolations { get; set; } = true;

    /// <summary>Escalate ambiguous intent routing. Default: false.</summary>
    public bool EscalateAmbiguousIntent { get; set; } = false;

    /// <summary>
    ///     Operationally escalate (notify operator, page, etc.) for Critical escalations.
    ///     Currently logs at Warning level; pluggable in future.
    /// </summary>
    public bool PageOperatorOnCritical { get; set; } = false;
}

/// <summary>
///     Конфигурация shared memory sync (task_051).
///     TTL, sensitivity classification, encryption requirement, namespace limits.
/// </summary>
public sealed class SharedMemorySyncConfig
{
    /// <summary>Включить shared memory sync. Default: false (backward-compatible).</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Default TTL в минутах для публикуемых фактов. 0 = permanent. Default: 1440 (1 день).</summary>
    public int DefaultTtlMinutes { get; set; } = 1440;

    /// <summary>Максимальное число shared facts на агента. Default: 500.</summary>
    public int MaxFactsPerAgent { get; set; } = 500;

    /// <summary>Требовать шифрование при передаче фактов между агентами. Default: true (HTTPS/mTLS).</summary>
    public bool EncryptionRequired { get; set; } = true;

    /// <summary>
    ///     Максимальный уровень sensitivity для shared facts: Public, Internal, Sensitive, Restricted.
    ///     Факты с более высоким уровнем sensitivity не синхронизируются.
    ///     Default: "Sensitive".
    /// </summary>
    public string MaxAllowedSensitivity { get; set; } = "Sensitive";

    /// <summary>Интервал автоматической синхронизации в минутах. 0 = выключена. Default: 30.</summary>
    public int SyncIntervalMinutes { get; set; } = 30;
}

/// <summary>
///     Конфигурация mesh evaluation suite (task_052).
///     Scenario definitions, metrics collection, regression thresholds.
/// </summary>
public sealed class MeshEvalConfig
{
    /// <summary>Включить evaluation suite. Default: false.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Директория с файлами сценариев (.json). Default: "data/mesh-eval/scenarios".</summary>
    public string ScenariosDir { get; set; } = "data/mesh-eval/scenarios";

    /// <summary>Директория для сохранения результатов. Default: "data/mesh-eval/results".</summary>
    public string ResultsDir { get; set; } = "data/mesh-eval/results";

    /// <summary>Максимальное время выполнения одного сценария в секундах. Default: 120.</summary>
    public int MaxScenarioDurationSeconds { get; set; } = 120;

    /// <summary>Включить оценку task success rate. Default: true.</summary>
    public bool EnableTaskSuccessEval { get; set; } = true;

    /// <summary>Включить оценку safety denials. Default: true.</summary>
    public bool EnableSafetyDenialEval { get; set; } = true;

    /// <summary>Включить оценку routing quality. Default: true.</summary>
    public bool EnableRoutingQualityEval { get; set; } = true;

    /// <summary>Включить оценку latency. Default: true.</summary>
    public bool EnableLatencyEval { get; set; } = true;

    /// <summary>Включить оценку cost. Default: true.</summary>
    public bool EnableCostEval { get; set; } = true;

    /// <summary>Включить оценку resilience (retry, circuit breaker). Default: true.</summary>
    public bool EnableResilienceEval { get; set; } = true;

    /// <summary>Включить оценку degradation при отказах. Default: true.</summary>
    public bool EnableDegradationEval { get; set; } = true;

    /// <summary>Порог success rate для pass/fail (0.0-1.0). Default: 0.8.</summary>
    public double MinSuccessRateThreshold { get; set; } = 0.8;

    /// <summary>Порог max latency в миллисекундах. Default: 5000.</summary>
    public int MaxLatencyThresholdMs { get; set; } = 5000;

    /// <summary>Порог max cost per request в USD. Default: 0.10.</summary>
    public decimal MaxCostPerRequestUsd { get; set; } = 0.10m;

    /// <summary>Блокировать deployment при regression (success rate ниже baseline). Default: true.</summary>
    public bool BlockOnRegression { get; set; } = true;

    /// <summary>Порог regression: если success rate падает более чем на это значение vs baseline — блокировка. Default: 0.05.</summary>
    public double RegressionThreshold { get; set; } = 0.05;

    /// <summary>Включить chaos-тестирование (случайные отказы). Default: false.</summary>
    public bool EnableChaosTesting { get; set; } = false;

    /// <summary>Вероятность chaos-инъекции (0.0-1.0). Default: 0.1.</summary>
    public double ChaosInjectionRate { get; set; } = 0.1;
}

/// <summary>
///     Конфигурация centralized mesh observability (task_054).
///     Trace context propagation (W3C TraceContext + B3), mesh-specific span enrichment,
///     OTLP metrics and structured log sink.
/// </summary>
public sealed class MeshCentralizedObservabilityConfig
{
    /// <summary>Включить centralized mesh observability. Default: false (backward-compatible).</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    ///     Propagation formats для trace context: "w3c" (default), "b3", "both".
    ///     W3C TraceContext: traceparent + tracestate headers.
    ///     B3: X-B3-TraceId + X-B3-SpanId + X-B3-Sampled.
    /// </summary>
    public string PropagationFormat { get; set; } = "w3c";

    /// <summary>
    ///     Список OTLP endpoint'ов для экспорта mesh-трейсов и метрик.
    ///     Если пусто — используется OtelConfig.OtlpEndpoint.
    /// </summary>
    public List<string> OtlpEndpoints { get; set; } = new();

    /// <summary>Включить per-hop mesh-специфичные span-теги. Default: true.</summary>
    public bool EnableSpanEnrichment { get; set; } = true;

    /// <summary>Включить mesh-метрики (hop_count, delegation_depth, routing_decision). Default: true.</summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>Включить redacted mesh-structured логи. Default: true.</summary>
    public bool EnableStructuredLogs { get; set; } = true;

    /// <summary>Включить trace context propagation в outbound HTTP-заголовках. Default: true.</summary>
    public bool EnableTraceContextPropagation { get; set; } = true;

    /// <summary>Включить trace context extraction из inbound запросов. Default: true.</summary>
    public bool EnableTraceContextExtraction { get; set; } = true;

    /// <summary>
    ///     Список header names для извлечения trace context из входящих запросов.
    ///     Default: traceparent, x-b3-traceid, x-b3-spanid.
    /// </summary>
    public List<string> InboundTraceHeaders { get; set; } = new() { "traceparent", "x-b3-traceid", "x-b3-spanid" };

    /// <summary>
    ///     Список атрибутов для redacted в mesh-тегах: payload, intent, sender, recipient.
    ///     Default: payload (никогда не включается в span-теги).
    /// </summary>
    public List<string> RedactedAttributes { get; set; } = new() { "payload" };

    /// <summary>Максимальная длина строки в tag value (больше обрезается). Default: 512.</summary>
    public int MaxTagValueLength { get; set; } = 512;
}
/// <summary>
///     Конфигурация operational SLOs (task_064).
///     Per-vertical availability, response-time, data-loss, recovery-time, cost targets,
///     alert thresholds and severity levels.
/// </summary>
public sealed class SlosConfig
{
    /// <summary>Включить SLO tracking. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
///     Директория с SLO definition JSON-файлами.
///     Файлы: {vertical}.slo.json, например: greenhouse.slo.json, cold-chain.slo.json.
///     Default: "docs/slos".
/// </summary>
    public string SlosDir { get; set; } = "docs/slos";

    /// <summary>Интервал evaluation SLO в секундах. Default: 60.</summary>
    public int EvaluationIntervalSeconds { get; set; } = 60;

    /// <summary>
///     Дефолтные alert thresholds (переопределяются per-vertical в .slo.json).
///     Пороги для warning и critical уровней.
/// </summary>
    public SlosDefaultThresholds DefaultThresholds { get; set; } = new();

    /// <summary>Включить alerting при SLO breach. Default: true.</summary>
    public bool EnableAlerting { get; set; } = true;

    /// <summary>Включить SLO status endpoint. Default: true.</summary>
    public bool EnableStatusEndpoint { get; set; } = true;
}

/// <summary>
///     Конфигурация backend profiles и degradation (task_070).
///     Активный профиль, директория профилей, настройки health check, policy degradation.
/// </summary>
public sealed class MeshProfilesConfig
{
    /// <summary>Включить backend health monitoring. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Имя активного профиля (например "local", "redis-ha", "nats-cluster"). Default: "local".</summary>
    public string? ActiveProfile { get; set; } = "local";

    /// <summary>Директория с .meshprofile.json файлами профилей. Default: "data/mesh-profiles".</summary>
    public string ProfilesDir { get; set; } = "data/mesh-profiles";

    /// <summary>Интервал health check всех backends в секундах. Default: 30.</summary>
    public int HealthCheckIntervalSec { get; set; } = 30;

    /// <summary>Timeout одного health check в секундах. Default: 5.</summary>
    public int HealthCheckTimeoutSec { get; set; } = 5;

    /// <summary>Число последовательных неудачных check'ов для перехода в Unavailable. Default: 3.</summary>
    public int MaxConsecutiveFailures { get; set; } = 3;

    /// <summary>Включить alerting через webhook при изменении состояния backend. Default: false.</summary>
    public bool EnableDegradationAlerts { get; set; } = false;

    /// <summary>URL webhook для degradation alerts.</summary>
    public string? AlertWebhook { get; set; }

    /// <summary>
    ///     Inline профили (dict key → profile). Загружаются при старте из конфигурации.
    ///     Используйте для overrides в appsettings.json.
    /// </summary>
    public Dictionary<string, Mesh.Profiles.MeshProfileDefinition>? Profiles { get; set; }
}

/// <summary>
///     Дефолтные alert thresholds для всех verticals (переопределяемые per-vertical).
/// </summary>
public sealed class SlosDefaultThresholds
{
    /// <summary>Дефолтный warning threshold для availability (% от целевого). Default: 95.</summary>
    public double AvailabilityWarningPct { get; set; } = 95.0;

    /// <summary>Дефолтный critical threshold для availability (% от целевого). Default: 90.</summary>
    public double AvailabilityCriticalPct { get; set; } = 90.0;

    /// <summary>Дефолтный warning threshold для response time (ms, 95th percentile). Default: 2000.</summary>
    public double ResponseTimeWarningMs { get; set; } = 2000.0;

    /// <summary>Дефолтный critical threshold для response time (ms, 95th percentile). Default: 5000.</summary>
    public double ResponseTimeCriticalMs { get; set; } = 5000.0;

    /// <summary>Дефолтный warning threshold для data loss (events/day). Default: 10.</summary>
    public int DataLossWarningPerDay { get; set; } = 10;

    /// <summary>Дефолтный critical threshold для data loss (events/day). Default: 100.</summary>
    public int DataLossCriticalPerDay { get; set; } = 100;

    /// <summary>Дефолтный warning threshold для recovery time (minutes). Default: 15.</summary>
    public int RecoveryTimeWarningMinutes { get; set; } = 15;

    /// <summary>Дефолтный critical threshold для recovery time (minutes). Default: 30.</summary>
    public int RecoveryTimeCriticalMinutes { get; set; } = 30;

    /// <summary>Дефолтный warning threshold для cost (% от дневного budget). Default: 80.</summary>
    public double CostWarningPct { get; set; } = 80.0;

    /// <summary>Дефолтный critical threshold для cost (% от дневного budget). Default: 95.</summary>
    public double CostCriticalPct { get; set; } = 95.0;
}