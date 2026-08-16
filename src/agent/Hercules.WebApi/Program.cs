using System.Text;
using System.Text.Encodings.Web;
using Hercules.Agent;
using Hercules.Audit;
using Hercules.Budget;
using Hercules.Cache;
using Hercules.CodeExecution;
using Hercules.Config;
using Hercules.Config.Rollout;
using Hercules.Context;
using Hercules.Context.Summarizer;
using Hercules.Degradation;
using Hercules.Lifecycle;
using Hercules.LLM;
using Hercules.LLM.JsonRepair;
using Hercules.LLM.Providers;
using Hercules.Memory.Layers;
using Hercules.Mesh;
using Hercules.Mesh.A2A;
using Hercules.Mesh.Auth;
using Hercules.Observability;
using Hercules.Offline;
using Hercules.Quotas;
using Hercules.Redaction;
using Hercules.Simulation;
using Hercules.Skills;
using Hercules.Skills.Eval;
using Hercules.Skills.Marketplace;
using Hercules.Skills.Quality;
using Hercules.Skills.Routing;
using Hercules.Skills.Routing.ScoringComponents;
using Hercules.Skills.Routing.Deterministic;
using Hercules.Storage;
using Hercules.Mesh.Transport;
using Hercules.Tasks;
using Hercules.Telegram;
using Hercules.Tools;
using Hercules.Tools.Approval;
using Hercules.Tools.Policy;
using Hercules.Tools.Registry;
using Hercules.WasmSandbox;
using Hercules.WasmSandbox.Compilation;
using Hercules.WebApi.Auth;
using Hercules.WebApi.Config;
using Hercules.WebApi.Controllers;
using Hercules.WebApi.Middleware;
using Hercules.Health;
using HerculesBus;
using HerculesBus.Core;
using HerculesBus.InMemory;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;

// ============================================================================
//  Hercules Web API — ASP.NET Core Minimal API поверх ядра агента.
//  Предоставляет HTTP-доступ к чату, навыкам, памяти, статистике и конфигурации.
//  Запуск: dotnet run --project Hercules.WebApi   (порт 5000)
// ============================================================================

var builder = WebApplication.CreateBuilder(args);

// --- Кодировка консоли для кириллицы ---
Console.OutputEncoding = Encoding.UTF8;

// --- Конфигурация (наследует appsettings.json + переменные окружения HERCULES_) ---
builder.Configuration.AddEnvironmentVariables("HERCULES_");

var appConfig = builder.Configuration.Get<AppConfig>() ?? new AppConfig();
var webCfg = builder.Configuration.GetSection("WebApi").Get<WebApiConfig>() ?? new WebApiConfig();
// task_079: health checks configuration (liveness, readiness, LLM ping timeout, disk/outbox thresholds)
var healthCfg = builder.Configuration.GetSection("HealthChecks").Get<HealthChecksConfig>() ?? new HealthChecksConfig();

// Делаем хранилище общим с CLI-приложением: проект Hercules лежит на уровень выше.
var sharedData = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "data"));
if (Directory.Exists(Path.GetDirectoryName(sharedData)!))
{
    appConfig.Storage.DataRoot = sharedData;
}

var runtimeConfigFile = Path.Combine(appConfig.Storage.DataRoot, "runtime-config.json");

// --- Регистрация сервисов ядра (как в консольном приложении) ---
builder.Services.AddSingleton(sp => new RuntimeConfigStore(
    appConfig,
    runtimeConfigFile,
    sp.GetRequiredService<ILogger<RuntimeConfigStore>>()));
// Конфигурационные секции резолвятся из RuntimeConfigStore.Current, чтобы при
// runtime-изменении конфигурации (через Web UI / PATCH /api/config) сервисы
// получали свежие значения, а не snapshot на момент первого resolve.
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Llm);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Storage);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Agent);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Telegram);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.CodeExecution);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Http);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Mcp);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.A2A);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Mesh);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.ToolPolicy);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.ToolRegistry);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Approval);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Memory);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Budget);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Otel);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Audit);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Secrets);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Eval);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.SelfImprovement);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Tasks);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.OfflineSync);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Degradation);
// task_079: health check thresholds (LLM ping timeout, disk/outbox limits)
builder.Services.AddSingleton(healthCfg);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Phase2);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.SkillQuality);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.LeastPrivilege);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Cache);

    // OpenTelemetry (task_013) — tracing + metrics
    builder.Services.AddHerculesOtel(appConfig.Otel);
builder.Services.AddSingleton(webCfg);

// task_078: IHttpClientFactory + named clients with standard resilience handlers
builder.Services.AddHttpClient();

// task_079: real health checks (liveness + readiness) replacing the static /api/health stub
builder.Services.AddSingleton<ILLMProviderProbe>(sp => new ProviderHealthCheckerAdapter(sp.GetRequiredService<ProviderHealthChecker>()));
builder.Services.AddHealthChecks()
    .AddCheck<SqliteHealthCheck>("sqlite", failureStatus: HealthStatus.Unhealthy, tags: new[] { "ready" })
    .AddCheck<LlmHealthCheck>("llm", failureStatus: HealthStatus.Unhealthy, tags: new[] { "ready" })
    .AddCheck<MeshBusHealthCheck>("mesh-bus", failureStatus: HealthStatus.Unhealthy, tags: new[] { "ready" })
    .AddCheck<DiskSpaceHealthCheck>("disk-space", failureStatus: HealthStatus.Unhealthy, tags: new[] { "ready" })
    .AddCheck<OutboxHealthCheck>("outbox", failureStatus: HealthStatus.Degraded, tags: new[] { "ready" })
    .AddCheck<SkillRegistryHealthCheck>("skill-registry", failureStatus: HealthStatus.Unhealthy, tags: new[] { "ready" });

var httpResilience = appConfig.Http.Resilience ?? new HttpResilienceConfig();

// LLM health/probe clients
builder.Services.AddHttpClient(ProviderHealthChecker.HttpClientName, c =>
{
    c.Timeout = TimeSpan.FromSeconds(ProviderHealthChecker.HealthCheckTimeout.TotalSeconds);
});
builder.Services.AddHttpClient(ProviderCapabilityDetector.HttpClientName, c =>
{
    c.Timeout = TimeSpan.FromSeconds(8);
});
builder.Services.AddHttpClient(LMStudioClient.HttpClientName, c =>
{
    c.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddHttpClient(NetworkMonitor.HttpClientName, c =>
{
    c.Timeout = TimeSpan.FromSeconds(appConfig.OfflineSync.NetworkPollTimeoutSeconds);
});

// Inter-agent transports (retry + circuit breaker + timeout)
builder.Services.AddHttpClient(IntentTransport.HttpClientName, c =>
{
    c.Timeout = TimeSpan.FromMilliseconds(appConfig.Mesh.IntentTimeoutMs);
}).AddStandardResilienceHandler(o =>
{
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(httpResilience.TimeoutSeconds);
    o.Retry.MaxRetryAttempts = httpResilience.RetryCount;
    o.Retry.Delay = TimeSpan.FromMilliseconds(httpResilience.RetryBaseDelayMs);
    o.Retry.UseJitter = true;
    o.CircuitBreaker.FailureRatio = httpResilience.CircuitBreakerFailureRatio;
    o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(httpResilience.CircuitBreakerSamplingDurationSeconds);
    o.CircuitBreaker.MinimumThroughput = httpResilience.CircuitBreakerMinimumThroughput;
    o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(httpResilience.CircuitBreakerBreakDurationSeconds);
});

builder.Services.AddHttpClient(HttpTransportAdapter.HttpClientName, c =>
{
    c.Timeout = TimeSpan.FromMilliseconds(appConfig.Mesh.IntentTimeoutMs);
}).AddStandardResilienceHandler(o =>
{
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(httpResilience.TimeoutSeconds);
    o.Retry.MaxRetryAttempts = httpResilience.RetryCount;
    o.Retry.Delay = TimeSpan.FromMilliseconds(httpResilience.RetryBaseDelayMs);
    o.Retry.UseJitter = true;
});

builder.Services.AddHttpClient(GrpcTransportAdapter.HttpClientName, c =>
{
    c.Timeout = TimeSpan.FromMilliseconds(appConfig.Mesh.IntentTimeoutMs);
});

// Outbound tool/agent clients
builder.Services.AddHttpClient(HttpTool.HttpClientName, c =>
{
    c.Timeout = TimeSpan.FromSeconds(appConfig.Http.TimeoutSeconds);
}).AddStandardResilienceHandler(o =>
{
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(httpResilience.TimeoutSeconds);
    o.Retry.MaxRetryAttempts = httpResilience.RetryCount;
    o.Retry.Delay = TimeSpan.FromMilliseconds(httpResilience.RetryBaseDelayMs);
    o.Retry.UseJitter = true;
});

builder.Services.AddHttpClient(A2AClient.HttpClientName, c =>
{
    c.Timeout = TimeSpan.FromSeconds(appConfig.A2A.TimeoutSeconds);
}).AddStandardResilienceHandler(o =>
{
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(httpResilience.TimeoutSeconds);
    o.Retry.MaxRetryAttempts = httpResilience.RetryCount;
    o.Retry.Delay = TimeSpan.FromMilliseconds(httpResilience.RetryBaseDelayMs);
    o.Retry.UseJitter = true;
});

builder.Services.AddHttpClient(OperatorNotificationService.HttpClientName, c =>
{
    c.Timeout = TimeSpan.FromSeconds(10);
}).AddStandardResilienceHandler(o =>
{
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(httpResilience.TimeoutSeconds);
    o.Retry.MaxRetryAttempts = Math.Max(1, httpResilience.RetryCount - 1);
    o.Retry.Delay = TimeSpan.FromMilliseconds(httpResilience.RetryBaseDelayMs);
    o.Retry.UseJitter = true;
});

builder.Services.AddHttpClient(SkillMarketplace.HttpClientName, c =>
{
    c.Timeout = TimeSpan.FromMinutes(2);
});

// LLM-слой (отказоустойчивый клиент с fallback + multi-role routing v2)
builder.Services.AddSingleton<LlmClientFactory>(sp =>
        new LlmClientFactory(
            sp.GetRequiredService<LlmConfig>(),
            sp.GetRequiredService<ICacheService>(),
            sp.GetService<IHttpClientFactory>()));
builder.Services.AddSingleton<RoleRouter>();
builder.Services.AddSingleton<IJsonRepairService, JsonRepairService>();
builder.Services.AddSingleton<ResilientLLMClient>(sp =>
    new ResilientLLMClient(
        sp.GetRequiredService<LlmConfig>(),
        sp.GetRequiredService<LlmClientFactory>(),
        sp.GetRequiredService<RoleRouter>(),
        sp.GetRequiredService<ILogger<ResilientLLMClient>>()));
builder.Services.AddSingleton<ILLMClient>(sp => sp.GetRequiredService<ResilientLLMClient>());
builder.Services.AddSingleton<ProviderHealthChecker>(sp =>
    new ProviderHealthChecker(
        sp.GetRequiredService<LlmConfig>(),
        sp.GetService<ILogger<ProviderHealthChecker>>(),
        sp.GetService<IHttpClientFactory>()));
builder.Services.AddSingleton<ProviderCapabilityDetector>(sp =>
    new ProviderCapabilityDetector(
        sp.GetRequiredService<LlmConfig>(),
        sp.GetService<ILogger<ProviderCapabilityDetector>>(),
        sp.GetRequiredService<ICacheService>(),
        sp.GetService<IHttpClientFactory>()));

// Code execution (Stage 2, v2)
builder.Services.AddSingleton<SandboxOptions>(sp =>
{
    var cfg = sp.GetRequiredService<CodeExecutionConfig>();
    var opts = new SandboxOptions
    {
        CpuTimeoutSeconds = cfg.CpuTimeoutSeconds,
        MaxFileSizeMb = cfg.MaxFileSizeMb,
        MaxProcesses = cfg.MaxProcesses,
        MaxOpenFiles = cfg.MaxOpenFiles,
        MaxVirtualMemoryMb = cfg.MaxVirtualMemoryMb,
        AllowNetwork = cfg.AllowNetwork,
        MaxCodeSizeKb = cfg.MaxCodeSizeKb,
        SessionTtlSeconds = cfg.SessionTtlSeconds
    };
    if (!string.IsNullOrWhiteSpace(cfg.TempRoot))
    {
        opts.TempRoot = cfg.TempRoot;
    }

    return opts;
});
builder.Services.AddSingleton<ICodeExecutor, DotnetFileBasedExecutor>();

// Tool ecosystem (Stage 3, v2)
builder.Services.AddSingleton<ITool, HttpTool>(sp =>
    new HttpTool(
        sp.GetRequiredService<HttpConfig>(),
        sp.GetRequiredService<ILogger<HttpTool>>(),
        sp.GetService<IHttpClientFactory>()));
builder.Services.AddSingleton<ITool, A2AClient>(sp =>
    new A2AClient(
        sp.GetRequiredService<A2AConfig>(),
        sp.GetService<IHttpClientFactory>()));
builder.Services.AddSingleton<ITool, CodeExecutionTool>();
// Tool policy engine (task_009)
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<ToolPolicyConfig>();
    var perms = ToolPermissionExtensions.ParseFromString(cfg.AgentPermissions);
    return new ToolPermissionSet(perms);
});
builder.Services.AddSingleton<IApprovalService>(sp =>
    new ApprovalService(
        sp.GetRequiredService<ApprovalConfig>(),
        sp.GetRequiredService<SqliteSessionStore>(),
        sp.GetRequiredService<ILogger<ApprovalService>>()));
builder.Services.AddSingleton<ToolPolicyEngine>(sp =>
    new ToolPolicyEngine(
        sp.GetRequiredService<ToolPolicyConfig>(),
        sp.GetRequiredService<ToolPermissionSet>(),
        sp.GetRequiredService<ILogger<ToolPolicyEngine>>(),
        sp.GetRequiredService<IApprovalService>(),
        sp.GetRequiredService<IAuditService>(),
        sp.GetRequiredService<Hercules.Tools.Grants.ISkillGrantService>()));
builder.Services.AddSingleton<ToolRegistry>();
builder.Services.AddSingleton<IToolRegistryService, ToolRegistryService>();
builder.Services.AddHostedService<ToolHealthService>();
builder.Services.AddSingleton<Hercules.Mcp.McpClientService>();
// McpServerHost (stdio MCP server) is registered in CLI Program.cs only —
// stdio transport requires console stdin/stdout which are unavailable in WebAPI mode.

// WASM sandbox (v3) — Wasmtime-based code execution с capability-based isolation.
// Регистрируем IWasmSandbox, CompilerRegistry, WasmTool и адаптер к ITool для AgentCore.
builder.Services.AddSingleton<IWasmSandbox, WasmtimeSandbox>();
builder.Services.AddSingleton<CompilerRegistry>(sp =>
{
    var registry = new CompilerRegistry();
    registry.Register(new PassthroughCompiler());
    return registry;
});
builder.Services.AddSingleton<WasmTool>();
builder.Services.AddSingleton<ITool, WasmToolAdapter>();

// HerculesBus (v3.1) — мессенджер для ИИ агентов (in-memory pub/sub + registry + channel store).
builder.Services.AddSingleton<IChannelStore, InMemoryChannelStore>();
builder.Services.AddSingleton<IAgentRegistry, InMemoryAgentRegistry>();
builder.Services.AddSingleton<IEventBus, InMemoryEventBus>();
builder.Services.AddSingleton<Bus>();

// Phase 3: Inter-agent mesh (manifest, capability registry, intent routing, transport)
builder.Services.AddMeshServices(appConfig, appConfig.Storage.DataRoot);

// Reactor подписывается на изменения конфигурации и перезагружает runtime-зависимости
builder.Services.AddHostedService<RuntimeConfigHostedService>();

// Хранилища
builder.Services.AddSingleton<FileSkillRepository>();
builder.Services.AddSingleton<MemoryStore>(sp =>
    new MemoryStore(
        sp.GetRequiredService<StorageConfig>(),
        sp.GetRequiredService<SecretsConfig>(),
        sp.GetRequiredService<ISecretMaskingService>()));
builder.Services.AddSingleton<SqliteSessionStore>();

// task_079: Outbox store (optional, used by OutboxHealthCheck and DegradationManager)
builder.Services.AddSingleton<IOutboxStore>(sp =>
    new SqliteOutboxStore(
        sp.GetRequiredService<SqliteSessionStore>(),
        sp.GetRequiredService<OfflineSyncConfig>(),
        sp.GetRequiredService<ILogger<SqliteOutboxStore>>()));

// task_026: Least-privilege grants
builder.Services.AddSingleton(sp =>
    new Hercules.Tools.Grants.SkillGrantStore(
        Path.Combine(sp.GetRequiredService<StorageConfig>().DataRoot, "grants.db")));
builder.Services.AddSingleton<Hercules.Tools.Grants.ISkillGrantService, Hercules.Tools.Grants.SkillGrantService>();

// Hybrid storage services (task_003)
builder.Services.AddSingleton<IBudgetService, BudgetService>();
builder.Services.AddSingleton<IAuditLog, AuditLogService>();

// Durable task lifecycle (task_018)
builder.Services.AddSingleton<ITaskRepository>(sp =>
    new SqliteTaskRepository(sp.GetRequiredService<SqliteSessionStore>()));
builder.Services.AddSingleton<TaskRetryHandler>();
builder.Services.AddSingleton<ITaskExecutionService>(sp =>
    new TaskExecutionService(
        sp.GetRequiredService<ITaskRepository>(),
        sp.GetRequiredService<TaskConfig>(),
        sp.GetRequiredService<ILogger<TaskExecutionService>>()));

// Budget and guardrails (task_012)
builder.Services.AddSingleton<IGuardrailService>(sp =>
    new GuardrailService(
        sp.GetRequiredService<BudgetConfig>(),
        sp.GetRequiredService<IBudgetService>()));
builder.Services.AddSingleton<BudgetGuard>(sp =>
    new BudgetGuard(
        sp.GetRequiredService<BudgetConfig>(),
        sp.GetRequiredService<ILogger<BudgetGuard>>()));

// Rate limits and quotas (task_056)
builder.Services.AddSingleton(appConfig.Quotas);
builder.Services.AddSingleton<IQuotaService>(sp =>
    new QuotaService(
        sp.GetRequiredService<QuotasConfig>(),
        sp.GetRequiredService<ILogger<QuotaService>>()));
builder.Services.AddSingleton<QuotaGuard>(sp =>
    new QuotaGuard(
        sp.GetRequiredService<ILogger<QuotaGuard>>()));

// Layered memory (task_011, task_075 H6)
builder.Services.AddSingleton<IDurableFactsStore, DurableFactsService>();
builder.Services.AddSingleton<IEpisodicStore, EpisodicStore>();
builder.Services.AddSingleton<LayerMetadataExtractor>();
// task_075 H6 fix: LayeredMemoryManager is now singleton and resolves working memory
// per session via ISessionStateStore (no more captive dependency on scoped IWorkingMemory).
builder.Services.AddSingleton<ISessionStateStore, InMemorySessionStateStore>();
builder.Services.AddSingleton<LayeredMemoryManager>(sp => new LayeredMemoryManager(
    sp.GetRequiredService<ISessionStateStore>(),
    sp.GetRequiredService<IDurableFactsStore>(),
    sp.GetRequiredService<IEpisodicStore>(),
    new LayeredMemoryConfig
    {
        MaxWorkingMemoryEntries = sp.GetRequiredService<MemoryConfig>().MaxWorkingMemoryEntries,
        MaxEpisodesInContext = sp.GetRequiredService<MemoryConfig>().MaxEpisodesInContext,
        DefaultFactTtlMinutes = sp.GetRequiredService<MemoryConfig>().DefaultFactTtlMinutes,
        SensitivityRedactionEnabled = sp.GetRequiredService<MemoryConfig>().SensitivityRedactionEnabled,
        MaxFactAgeDays = sp.GetRequiredService<MemoryConfig>().MaxFactAgeDays
    }));

// Phase 2: Skill packager (export/import .skillpkg) + signing (task_021)
builder.Services.AddSingleton(appConfig.Marketplace);
builder.Services.AddSingleton<IMarketplaceSigningService>(sp =>
    new MarketplaceSigningService(sp.GetRequiredService<MarketplaceConfig>()));
builder.Services.AddSingleton<SkillPackager>(sp =>
    new SkillPackager(
        sp.GetRequiredService<FileSkillRepository>(),
        sp.GetRequiredService<SecretsConfig>(),
        sp.GetRequiredService<ISecretMaskingService>(),
        sp.GetRequiredService<IMarketplaceSigningService>(),
        sp.GetRequiredService<Hercules.Config.LeastPrivilegeConfig>(),
        sp.GetRequiredService<Hercules.Tools.Grants.ISkillGrantService>()));

// Phase 2: Semantic routing (embedding-based). Stub provider — offline.
// Phase 2: Semantic routing (task_022) — scoring components
builder.Services.AddSingleton<IEmbeddingProvider, StubEmbeddingProvider>();
builder.Services.AddSingleton<LexicalScorer>();
builder.Services.AddSingleton<HistoricalQualityScorer>();
builder.Services.AddSingleton<SchemaCompatibilityScorer>(sp =>
    new SchemaCompatibilityScorer(
        sp.GetService<ToolRegistry>()?.Names ?? Enumerable.Empty<string>()));
builder.Services.AddSingleton<LatencyScorer>();
builder.Services.AddSingleton<PolicyEligibilityScorer>(sp =>
    new PolicyEligibilityScorer(
        sp.GetService<ToolPolicyConfig>(),
        sp.GetService<ToolPolicyConfig>()?.AgentPermissions.Split('|') ?? Enumerable.Empty<string>(),
        sp.GetService<ToolRegistry>()?.Names ?? Enumerable.Empty<string>()));
builder.Services.AddSingleton<EmbeddingScorer>(sp =>
    new EmbeddingScorer(
        sp.GetRequiredService<IEmbeddingProvider>(),
        sp.GetRequiredService<SkillManager>(),
        sp.GetRequiredService<Phase2Config>().SimilarityThreshold,
        sp.GetRequiredService<ICacheService>()));
builder.Services.AddSingleton<ISkillScoringEngine>(sp =>
    new SkillScoringEngine(
        sp.GetRequiredService<SkillManager>(),
        sp.GetRequiredService<Phase2Config>(),
        [
            sp.GetRequiredService<EmbeddingScorer>(),
            sp.GetRequiredService<LexicalScorer>(),
            sp.GetRequiredService<SchemaCompatibilityScorer>(),
            sp.GetRequiredService<HistoricalQualityScorer>(),
            sp.GetRequiredService<LatencyScorer>(),
            sp.GetRequiredService<PolicyEligibilityScorer>(),
            sp.GetRequiredService<SkillQualityScorer>()
        ],
        sp.GetService<ToolRegistry>()?.Names ?? Enumerable.Empty<string>(),
        sp.GetService<EmbeddingScorer>()));
// Task 023: Deterministic router (offline-safe keyword + tag + type matching)
builder.Services.AddSingleton<IDeterministicRouter>(sp =>
    new DeterministicRouter(
        sp.GetRequiredService<SkillManager>(),
        sp.GetRequiredService<Phase2Config>().DeterministicRouting,
        sp.GetRequiredService<ICacheService>()));

builder.Services.AddSingleton<EmbeddingSkillRouter>(sp =>
    new EmbeddingSkillRouter(
        sp.GetRequiredService<SkillManager>(),
        sp.GetRequiredService<Phase2Config>(),
        sp.GetRequiredService<SkillRouter>(),
        sp.GetService<ISkillScoringEngine>(),
        sp.GetService<IDeterministicRouter>()));

// Phase 2: Skill marketplace + agent templates (task_021)
builder.Services.AddSingleton<SkillQualityStore>(sp =>
    new SkillQualityStore(sp.GetRequiredService<StorageConfig>()));
builder.Services.AddSingleton<ISkillQualityService>(sp =>
    new SkillQualityService(
        sp.GetRequiredService<SkillQualityStore>(),
        sp.GetRequiredService<SkillQualityConfig>()));
builder.Services.AddSingleton<SkillQualityScorer>(sp =>
    new SkillQualityScorer(sp.GetRequiredService<ISkillQualityService>()));

builder.Services.AddSingleton<SkillMarketplace>(sp =>
    new SkillMarketplace(
        sp.GetRequiredService<StorageConfig>(),
        sp.GetRequiredService<SkillPackager>(),
        sp.GetRequiredService<IMarketplaceSigningService>(),
        sp.GetService<IHttpClientFactory>()));
builder.Services.AddSingleton<AgentTemplateManager>();

// Template simulation (task_031)
builder.Services.AddSingleton<ISensorSimulator>(sp =>
    new FileSensorSimulator(sp.GetRequiredService<ILogger<FileSensorSimulator>>())
    {
        TemplatesBaseDir = Path.Combine(AppContext.BaseDirectory, "templates")
    });
builder.Services.AddSingleton<FailureScenarioEngine>();
builder.Services.AddSingleton<TemplateSimulationService>();

// Skill lifecycle: policy, deprecation, evaluation
builder.Services.AddSingleton<SkillLifecyclePolicy>();
builder.Services.AddSingleton<SkillDeprecationManager>();
builder.Services.AddSingleton<SkillEvaluationEngine>();
builder.Services.AddSingleton<SkillLifecycleService>();

// Skill manifest & compatibility (task_020)
builder.Services.AddSingleton(appConfig.Phase2.SkillManifest);
builder.Services.AddSingleton<SkillManifestValidator>(sp =>
{
    var cfg = sp.GetRequiredService<SkillManifestConfig>();
    var allowedLevels = cfg.AllowedRiskLevels.Count > 0
        ? cfg.AllowedRiskLevels.Select(i => (Hercules.Skills.SkillRiskLevel)i).ToList()
        : (IReadOnlyList<Hercules.Skills.SkillRiskLevel>?)null;
    return new SkillManifestValidator(cfg.CurrentHerculesVersion, null, allowedLevels);
});

// Eval harness (task_016)
builder.Services.AddSingleton<BaselineManager>();
builder.Services.AddSingleton<SkillTestGenerator>();
builder.Services.AddSingleton<IEvalHarnessService, EvalHarnessService>();

// Audit and privacy (task_014)
builder.Services.AddSingleton<PayloadHashService>();
builder.Services.AddSingleton<IRedactionService>(sp =>
    new RedactionService(
        sp.GetRequiredService<AuditConfig>(),
        sp.GetRequiredService<ILogger<RedactionService>>()));
builder.Services.AddSingleton<IAuditService>(sp =>
    new AuditService(
        sp.GetRequiredService<IAuditLog>(),
        sp.GetRequiredService<AuditConfig>(),
        sp.GetService<IRedactionService>(),
        sp.GetRequiredService<PayloadHashService>(),
        sp.GetRequiredService<ILogger<AuditService>>()));

// Secrets and redaction (task_015)
builder.Services.AddSingleton<ISecretMaskingService>(sp =>
    new SecretMaskingService(
        sp.GetRequiredService<SecretsConfig>(),
        sp.GetRequiredService<IRedactionService>()));

// Self-improvement (task_017)
builder.Services.AddSingleton<Hercules.Reflection.ProposalStore>();
builder.Services.AddSingleton<Hercules.Reflection.ProposalDiffer>();
builder.Services.AddSingleton<Hercules.Reflection.MaintenanceWorkflow>();
builder.Services.AddSingleton<Hercules.Reflection.SelfImprovementService>();

// Агент
builder.Services.AddSingleton<SkillManager>();
builder.Services.AddSingleton<SkillRouter>();
builder.Services.AddSingleton<MemoryManager>();
builder.Services.AddSingleton<LayeredMemoryManager>();

// [task_027] Context Assembly
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Context);
builder.Services.AddSingleton<ITraceSummarizer, Hercules.Context.Summarizer.TraceSummarizer>();
builder.Services.AddSingleton<IContextBuilder>(sp =>
    new Hercules.Context.ContextBuilder(
        sp.GetRequiredService<LayeredMemoryManager>(),
        sp.GetRequiredService<ContextConfig>(),
        sp.GetRequiredService<ILogger<Hercules.Context.ContextBuilder>>(),
        sp.GetRequiredService<ITraceSummarizer>()));

// [task_028] Caching — unified cache service
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Cache);
builder.Services.AddSingleton<ICacheService, CacheService>();

builder.Services.AddSingleton<ReflectionEngine>();
builder.Services.AddSingleton<AgentCore>();
// Регистрируем сервисы, поддерживающие hot-reload конфигурации, как IConfigReload
// чтобы RuntimeConfigReactor мог прокидывать им новые настройки без перезагрузки.
builder.Services.AddSingleton<IConfigReload>(sp => sp.GetRequiredService<AgentCore>());
builder.Services.AddSingleton<IConfigReload>(sp => sp.GetRequiredService<SkillManager>());

// Адаптер Web API
builder.Services.AddSingleton<WebApiAdapter>();

// task_080: shutdown & drain primitives
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Shutdown);
builder.Services.AddSingleton<IAgentLifecycleState, AgentLifecycleStateHolder>();
builder.Services.AddSingleton<IInFlightTracker>(sp => new InFlightTracker(sp.GetService<ILogger<InFlightTracker>>()));

// task_057: Lifecycle management
builder.Services.AddSingleton<ILifecycleService>(sp =>
    new LifecycleService(
        sp.GetRequiredService<AgentCore>(),
        sp.GetRequiredService<SkillManager>(),
        sp.GetRequiredService<CapabilityRegistry>(),
        sp.GetRequiredService<ITransport>(),
        sp.GetRequiredService<ILogger<LifecycleService>>(),
        sp.GetRequiredService<IAgentLifecycleState>(),
        sp.GetRequiredService<IInFlightTracker>(),
        sp.GetRequiredService<ShutdownConfig>()));

// task_080: graceful drain — runs on host shutdown, awaits in-flight requests
builder.Services.AddHostedService<DrainHostedService>();

// task_058: Config & policy rollout — staged signed bundles with expiry and LKG fallback
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.ConfigRollout);
builder.Services.AddSingleton<ISignedBundleValidator, SignedBundleValidator>();
builder.Services.AddSingleton<LocalConfigValidator>();
builder.Services.AddSingleton<IRolloutManager, RolloutManager>();
builder.Services.AddHostedService<RolloutExpiryChecker>();

// --- CORS: разрешаем localhost-источники фронтенда ---
const string corsPolicy = "frontend";
builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicy, policy =>
    {
        switch (webCfg.AllowedCorsOrigins.Count)
        {
            case > 0:
                policy.WithOrigins(webCfg.AllowedCorsOrigins.ToArray())
                    .AllowAnyHeader()
                    .AllowAnyMethod();
                break;
            default:
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
                break;
        }
    });
});

// JSON: не экранировать кириллицу в ответах
builder.Services.ConfigureHttpJsonOptions(o => { o.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping; });

// Порт по умолчанию — 5000 (если не переопределён через --urls / ASPNETCORE_URLS / launchSettings)
// launchSettings.json в Development может навязать другой URL, поэтому отключаем его влияние.
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")) &&
    !args.Any(a => a.StartsWith("--urls")) &&
    builder.Environment.IsProduction())
{
    builder.WebHost.UseUrls("http://0.0.0.0:5000");
}
else if (builder.Environment.IsDevelopment())
{
    // В Development игнорируем launchSettings URL и фиксируем порт 5000,
    // чтобы не зависеть от случайного порта в Properties/launchSettings.json.
    builder.WebHost.UseUrls("http://localhost:5000");
}

var app = builder.Build();

// --- Middleware ---
app.UseCors(corsPolicy);
// task_080: drain check runs as early as possible so even a request that would be
// rejected by another middleware (e.g. CORS, ApiKey) still gets a clean 503.
app.UseMiddleware<DrainMiddleware>();
app.UseMiddleware<RequestBodyLimitMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();
app.UseMiddleware<RateLimitMiddleware>();
// task_039: peer auth middleware for /api/mesh/* endpoints
app.UseMiddleware<PeerAuthMiddleware>();

// --- Инициализация сессии агента ---
app.Services.GetRequiredService<WebApiAdapter>().EnsureSessionStarted();

// --- Служебные эндпоинты ---
app.MapGet("/", () => Results.Ok(new
{
    name = "Hercules Web API",
    version = "1.0",
    endpoints = (string[])
    [
        "POST /api/chat", "GET /api/skills", "POST /api/skills",
        "GET /api/skills/{id}", "PUT /api/skills/{id}", "POST /api/skills/{id}/improve",
        "GET /api/skills/{id}/export", "POST /api/skills/import",
        "POST /api/skills/{id}/evaluate", "POST /api/skills/{id}/deprecate",
        "POST /api/skills/{id}/rollback", "POST /api/skills/{id}/undeprecate",
        "GET /api/skills/deprecated",
        "GET /api/memory/profile", "PUT /api/memory/profile", "POST /api/memory/reset",
        "GET /api/reflect", "GET /api/stats",
        "GET /api/budget", "GET /api/budget/monthly",
        "GET /api/budget/guardrails", "GET /api/budget/guardrails/{type}",
        "GET /api/audit", "GET /api/audit/{target}",
        "GET /api/llm/health", "GET /api/llm/health/{provider}",
        "GET /api/llm/capabilities", "GET /api/llm/capabilities/{provider}",
        "GET /api/llm/config",
        "GET /api/config", "PUT /api/config", "PATCH /api/config",
        "GET /api/approvals/pending", "GET /api/approvals/{id}",
        "POST /api/approvals/{id}/approve", "POST /api/approvals/{id}/deny",
        "POST /api/skills/{id}/eval/harness", "POST /api/skills/{id}/eval/baseline",
        "GET /api/skills/{id}/eval/baseline", "GET /api/skills/{id}/eval/history",
        "GET /api/skills/{id}/manifest", "POST /api/skills/{id}/manifest/validate",
        "POST /api/skills/manifest/validate-all",
        "GET /api/eval/baselines",
        "POST /api/maintenance/run", "POST /api/maintenance/run-all",
        "GET /api/maintenance/proposals", "GET /api/maintenance/proposals/{id}",
        "POST /api/maintenance/proposals/{id}/approve",
        "POST /api/maintenance/proposals/{id}/reject",
        "GET /agent.manifest.json", "GET /api/mesh/agents", "POST /api/mesh/agents/register",
        "GET /api/mesh/agents/{id}", "DELETE /api/mesh/agents/{id}",
        "GET /api/mesh/capabilities", "GET /api/mesh/capabilities/{name}",
        "GET /api/mesh/capabilities/search", "POST /api/mesh/intent",
        "GET /api/tools", "GET /api/tools/{name}", "GET /api/tools/{name}/health",
        "GET /api/tools/categories", "POST /api/tools/{name}/enable", "POST /api/tools/{name}/disable"
    ]
}));
// task_079: real health endpoints replacing the static /api/health stub.
//   /api/health        — liveness, returns 200 if the process is alive (no checks)
//   /api/ready         — readiness, returns 200 only if all "ready"-tagged checks pass
//   /api/health/detail — JSON per-check breakdown (auth-gated by ApiKeyMiddleware)
app.MapHealthChecks("/api/health", new HealthCheckOptions
{
    Predicate = _ => false, // liveness: process-alive only
    ResponseWriter = Hercules.WebApi.Health.HealthCheckResponseWriter.WriteLiveness
});
app.MapHealthChecks("/api/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = Hercules.WebApi.Health.HealthCheckResponseWriter.WriteReadiness
});
app.MapHealthChecks("/api/health/detail", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = Hercules.WebApi.Health.HealthCheckResponseWriter.WriteDetail,
    AllowCachingResponses = false
}).AllowAnonymous(); // ApiKeyMiddleware already guards /api/* — no extra attribute needed

// A2A Agent Card — статический файл по спецификации (task_033)
app.MapGet("/agent-card.json", () =>
{
    // agent-card.json публикуется в dataRoot при старте;.TryReadFromFile чтобы избежать
    // NRE если файл ещё не создан (например, CLI-only запуск)
    var dataRoot = app.Services.GetRequiredService<StorageConfig>().DataRoot;
    var cardPath = Path.Combine(dataRoot, "agent-card.json");
    if (!File.Exists(cardPath))
    {
        return Results.NotFound(new { error = "agent-card.json not published yet" });
    }

    var json = File.ReadAllText(cardPath);
    return Results.Text(json, "application/json");
});

// --- Доменные эндпоинты ---
app.MapChat();
app.MapSkills();
app.MapSkillLifecycle();
app.MapSkillQuality();
app.MapMemory();
app.MapStats();
app.MapConfig();
app.MapRollout();
app.MapMesh();
app.MapMeshProfiles();
app.MapMeshObservability();
app.MapLifecycle();

// Agent manifest — публикация на startup (task_032)
try
{
    var manifestService = app.Services.GetRequiredService<AgentManifestService>();
    var manifest = manifestService.Save();
    var errors = manifestService.Validate();
    if (errors.Count > 0)
    {
        Console.WriteLine($"[Manifest] Опубликован с предупреждениями: {manifestService.ManifestPath}");
        foreach (var err in errors)
        {
            Console.WriteLine($"  ⚠ {err}");
        }
    }
    else
    {
        Console.WriteLine($"[Manifest] Опубликован: {manifestService.ManifestPath}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[Manifest] Publishing failed: {ex.Message}");
}

// A2A Agent Card — публикация на startup (task_033)
try
{
    var agentCardService = app.Services.GetRequiredService<IAgentCardService>();
    var a2aConfig = app.Services.GetRequiredService<A2AConfig>();
    if (a2aConfig.AgentCard.Publish)
    {
        var path = await agentCardService.PublishAsync();
        Console.WriteLine($"[AgentCard] Published: {path}");
    }
    else
    {
        Console.WriteLine("[AgentCard] Publishing disabled (A2A.AgentCard.Publish = false)");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[AgentCard] Publishing failed: {ex.Message}");
}

app.MapBudget();
app.MapQuotas();
app.MapAudit();
app.MapLlm();
app.MapA2A();
app.MapBackups();
app.MapFleetTemplates();
app.MapGrants();
app.MapSimulation();
app.MapSlos();
app.MapApprovals();
app.MapEscalations();
app.MapObservability();
app.MapSkillHarness();
app.MapSelfImprovement();
app.MapSkillManifest();
app.MapTasks();
app.MapContext();
app.MapCache();
app.MapCache();
app.MapToolRegistry();
app.MapMcpEndpoints();

Console.WriteLine("🌐 Hercules Web API запущен на http://localhost:5000");
Console.WriteLine($"🔑 X-Api-Key: {(string.IsNullOrEmpty(webCfg.ApiKey) ? "(отключён)" : webCfg.ApiKey)}");
Console.WriteLine($"💾 Данные: {appConfig.Storage.DataRoot}");

// Tool registry discovery (task_024)
try
{
    var registry = app.Services.GetRequiredService<IToolRegistryService>();
    var policyEngine = app.Services.GetService<ToolPolicyEngine>();
    var logger = app.Services.GetService<ILogger<Program>>();
    var discovered = ToolDiscovery.Discover(appConfig, registry, policyEngine, logger);
    Console.WriteLine($"[ToolRegistry] {discovered} tools discovered from file system");
}
catch (Exception ex)
{
    Console.WriteLine($"[ToolRegistry] Discovery failed: {ex.Message}");
}

// MCP client initialization (task_025)
try
{
    var mcpService = app.Services.GetRequiredService<Hercules.Mcp.McpClientService>();
    await mcpService.InitializeAsync();
    Console.WriteLine($"[MCP] Client initialized: {mcpService.ServerStates.Count} servers configured");
}
catch (Exception ex)
{
    Console.WriteLine($"[MCP] Initialization failed: {ex.Message}");
}

app.Run();
