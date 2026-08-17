using System.Text;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Scalar.AspNetCore;
using Hercules.Agent;
using Hercules.WebApi.Logging;
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
using Hercules.Fleet;
using Hercules.Slo;
using Hercules.Security;
using Hercules.Mesh.Escalation;
using Hercules.Mesh.Observability;
using Hercules.Tasks;
using Hercules.Telegram;
using Hercules.Tools;
using Hercules.Tools.Approval;
using Hercules.Tools.Policy;
using Hercules.Tools.Registry;
using Hercules.WasmSandbox;
using Hercules.WasmSandbox.Compilation;
using Hercules.WebApi;
using Hercules.WebApi.Auth;
using Hercules.WebApi.Config;
using Hercules.WebApi.Controllers;
using Hercules.WebApi.Middleware;
using Hercules.Health;
using HerculesBus;
using HerculesBus.Core;
using HerculesBus.InMemory;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;

// ============================================================================
//  Hercules Web API — ASP.NET Core Minimal API поверх ядра агента.
//  Предоставляет HTTP-доступ к чату, навыкам, памяти, статистике и конфигурации.
//  Запуск: dotnet run --project Hercules.WebApi   (порт 8421, см. ADR-0003)
// ============================================================================

// [task_109] Build-time detection: when MSBuild invokes GetDocument.Insider
// for OpenAPI document generation, skip side-effecting service registration
// (DB-touching singletons, OpenTelemetry exporters, file I/O) and post-build
// bootstrap (key generation, manifest publishing, MCP init). The build target
// only needs `app.MapOpenApi()` to expose the document via DI; it never
// serves HTTP requests.
var isBuildTime = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

var builder = WebApplication.CreateBuilder(args);

// --- Кодировка консоли для кириллицы ---
Console.OutputEncoding = Encoding.UTF8;

// [task_085] Async-friendly JSON console logger (Web API). Mirrors the
// configuration in the console entry point so both surfaces emit
// machine-parseable JSON with scopes and UTC timestamps.
builder.Logging.ClearProviders();
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddSimpleConsole(options =>
    {
        options.IncludeScopes = true;
        options.TimestampFormat = "HH:mm:ss ";
        options.SingleLine = true;
    });
    builder.Logging.AddDebug();
}
else
{
    builder.Logging.AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        options.UseUtcTimestamp = true;
        options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions
        {
            Indented = false
        };
    });
}

// --- Конфигурация (наследует appsettings.json + переменные окружения HERCULES_) ---
builder.Configuration.AddEnvironmentVariables("HERCULES_");
if(builder.Environment.IsDevelopment()) builder.Configuration.AddUserSecrets<Program>();

var appConfig = builder.Configuration.Get<AppConfig>() ?? new AppConfig();

var webCfg = builder.Configuration.GetSection("WebApi").Get<WebApiConfig>() ?? new WebApiConfig();
// task_079: health checks configuration (liveness, readiness, LLM ping timeout, disk/outbox thresholds)
var healthCfg = builder.Configuration.GetSection("HealthChecks").Get<HealthChecksConfig>() ?? new HealthChecksConfig();

// task_097: backward-compat fallback — если ApiKeys не сконфигурированы, используем
// legacy ApiKey как contribute. Это позволяет существующим развёртываниям не ломаться.
if (webCfg.ApiKeys.Count == 0 && !string.IsNullOrEmpty(webCfg.ApiKey))
{
    webCfg.ApiKeys.Add(new ApiKeyEntry
    {
        Key = webCfg.ApiKey,
        Role = ApiKeyRole.Contribute,
        Description = "legacy single key (forwarded as contribute)"
    });
}

// The data root is already resolved by BuiltIn.ResolveDataRoot at AppConfig init.
// Web API intentionally shares the same runtime data directory as the console host.
var sharedData = appConfig.Storage.DataRoot;

var runtimeConfigFile = Path.Combine(appConfig.Storage.DataRoot, Hercules.BuiltIn.RuntimeConfigFileName);

var logsDir = Path.Combine(appConfig.Storage.DataRoot, Hercules.BuiltIn.LogsSubdir);
Directory.CreateDirectory(logsDir);
builder.Logging.AddProvider(new FileLoggerProvider(logsDir));

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

// [task_109] OpenAPI document generation (built-in .NET 10, OpenAPI 3.1).
// Exposes /openapi/v1.json at runtime and feeds the build-time `openapi.json`
// artefact produced by Microsoft.Extensions.ApiDescription.Server.
// `ShouldInclude = _ => true` opts every endpoint into the document — without
// this, minimal API routes are skipped unless they call `.WithOpenApi()`
// explicitly (see task_110 for WithTags / task_111 for Produces<T>).
builder.Services.AddOpenApi(options =>
{
    options.ShouldInclude = _ => true;
});

// [task_109] Bridges minimal API endpoints to MVC's IApiDescriptionProvider so
// that downstream tooling (Spectral in task_117, dotnet-getdocument build target)
// can enumerate them. The runtime OpenAPI service has its own minimal-API
// provider, so this is a no-op for `app.MapOpenApi()` but required for the
// build-time document to contain the routes.
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSingleton(webCfg);
// task_097: ApiKeyStore (load-or-generate API keys с ролями, см. ADR-0004).
// Регистрируем ДО app.Build(), чтобы можно было resolve при первом запросе.
builder.Services.AddSingleton<Hercules.WebApi.Auth.ApiKeyStore>();

// task_098: CheckInService для Studio-протокола (ADR-0005). Singleton — состояние
// CheckIn'ов живёт в памяти процесса, общий для всех запросов. IAuditService
// resolve'ится лениво через IServiceProvider, чтобы оставаться опциональным.
builder.Services.AddSingleton<Hercules.CheckIn.CheckInService>(sp =>
    new Hercules.CheckIn.CheckInService(
        sp.GetRequiredService<ILogger<Hercules.CheckIn.CheckInService>>(),
        sp.GetService<Hercules.Audit.IAuditService>()));

// task_099: RestartService для supervisor-протокола. Singleton — состояние
// restart-флага персистится в {DataRoot}/restart-state.json. IAuditService
// resolve'ится лениво через IServiceProvider (опционально).
builder.Services.AddSingleton<Hercules.Restart.RestartService>(sp =>
    new Hercules.Restart.RestartService(
        sp.GetRequiredService<ILogger<Hercules.Restart.RestartService>>(),
        sp.GetService<Hercules.Audit.IAuditService>(),
        Path.Combine(sp.GetRequiredService<Hercules.Config.StorageConfig>().DataRoot, Hercules.BuiltIn.RestartStateFileName)));

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
builder.Services.AddSingleton<ILLMClientFactory>(sp => sp.GetRequiredService<LlmClientFactory>());
builder.Services.AddSingleton<RoleRouter>();
builder.Services.AddSingleton<IJsonRepairService, JsonRepairService>();
builder.Services.AddSingleton<ResilientLLMClient>(sp =>
{
    var client = new ResilientLLMClient(
        sp.GetRequiredService<LlmConfig>(),
        sp.GetRequiredService<LlmClientFactory>(),
        sp.GetRequiredService<RoleRouter>(),
        sp.GetRequiredService<ILogger<ResilientLLMClient>>());
    // [task_085] Wire Otel.LoggingSampleRate.
    var otel = sp.GetService<OtelConfig>();
    if (otel is not null)
    {
        client.SetLogSampleRate(otel.LoggingSampleRate);
    }
    return client;
});
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

// SkillSdk context factory (task_101)
builder.Services.AddSingleton<ISkillContextFactory>(sp =>
    new SkillSdkContextFactory(
        sp.GetRequiredService<HttpConfig>(),
        sp.GetRequiredService<ILLMClient>(),
        sp.GetRequiredService<ToolRegistry>(),
        sp.GetRequiredService<LayeredMemoryManager>(),
        sp.GetRequiredService<IDurableFactsStore>(),
        sp.GetRequiredService<ILoggerFactory>(),
        sp.GetService<IHttpClientFactory>()));

// SkillSdk in-process executor (task_101). Registered separately so it can be resolved by CodeExecutionTool.
builder.Services.AddSingleton<SkillSdkExecutor>(sp =>
    new SkillSdkExecutor(
        sp.GetRequiredService<SandboxOptions>(),
        sp.GetRequiredService<ISkillContextFactory>()));
builder.Services.AddSingleton<ICodeExecutor>(sp => sp.GetRequiredService<SkillSdkExecutor>());

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
// [task_109] CodeExecutionTool registration skipped at build-time:
// its ambiguous constructors (IEnumerable<ICodeExecutor> vs ICodeExecutor) trip
// DI scope validation when the OpenAPI build target boots the host in a stripped
// environment. The tool itself is documented in task_024 and re-registered at
// runtime via `dotnet run` / `dotnet exec` where validation is properly scoped.
if (!isBuildTime)
{
    builder.Services.AddSingleton<ITool, CodeExecutionTool>(sp => new CodeExecutionTool(sp));
}
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
// task_086: pass BusConfig so the bus uses bounded channels with the
// configured backpressure / drop policy.
builder.Services.AddSingleton<IEventBus>(sp => new InMemoryEventBus(
    sp.GetRequiredService<ILogger<InMemoryEventBus>>(),
    appConfig.Bus));
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

// task_103: session store backend selection (sqlite | postgres). The
// ISessionStore contract is satisfied by SqliteSessionStore by default; set
// Storage.SessionStore.Provider = "postgres" + ConnectionString to switch
// to the shared Npgsql-backed store. SqliteSessionStore stays registered as
// a concrete type so consumers that need the SQLite-specific escape
// hatches (SqliteOutboxStore, SqliteTaskRepository, SqliteDistillationStore,
// SkillQualityStore) keep compiling unchanged.
var sessionStoreCfg = builder.Configuration.GetSection("Storage:SessionStore").Get<SessionStoreBackendConfig>()
    ?? new SessionStoreBackendConfig();
if (string.Equals(sessionStoreCfg.Provider, "postgres", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<ISessionStore>(sp => new PostgresSessionStore(
        sessionStoreCfg,
        sp.GetRequiredService<StorageConfig>()));
}
else
{
    builder.Services.AddSingleton<ISessionStore>(sp => sp.GetRequiredService<SqliteSessionStore>());
}

// task_079: Outbox store (optional, used by OutboxHealthCheck and DegradationManager)
builder.Services.AddSingleton<IOutboxStore>(sp =>
    new SqliteOutboxStore(
        sp.GetRequiredService<SqliteSessionStore>(),
        sp.GetRequiredService<OfflineSyncConfig>(),
        sp.GetRequiredService<ILogger<SqliteOutboxStore>>()));
builder.Services.AddSingleton<NetworkMonitor>(sp =>
    new NetworkMonitor(
        sp.GetRequiredService<OfflineSyncConfig>(),
        sp.GetRequiredService<ILogger<NetworkMonitor>>(),
        sp.GetService<IHttpClientFactory>()!));
builder.Services.AddSingleton<INetworkMonitor>(sp => sp.GetRequiredService<NetworkMonitor>());

// task_026: Least-privilege grants
builder.Services.AddSingleton(sp =>
    new Hercules.Tools.Grants.SkillGrantStore(
        Path.Combine(sp.GetRequiredService<StorageConfig>().DataRoot, Hercules.BuiltIn.GrantsDatabaseFileName)));
builder.Services.AddSingleton<Hercules.Tools.Grants.ISkillGrantService, Hercules.Tools.Grants.SkillGrantService>();

// Hybrid storage services (task_003)
builder.Services.AddSingleton<IBudgetService, BudgetService>();
builder.Services.AddSingleton<IAuditLog, AuditLogService>();

// Backup & Recovery (task_063)
builder.Services.AddSingleton(appConfig.Backup);
builder.Services.AddSingleton<Hercules.Backup.EncryptionService>();
builder.Services.AddSingleton<Hercules.Backup.IBackupService>(sp =>
    new Hercules.Backup.BackupService(
        sp.GetRequiredService<Hercules.Backup.BackupConfig>(),
        sp.GetRequiredService<Hercules.Backup.EncryptionService>(),
        sp.GetRequiredService<ILogger<Hercules.Backup.BackupService>>(),
        sp.GetRequiredService<StorageConfig>().DataRoot));

// Fleet templates (task_062)
builder.Services.AddSingleton<AgentTemplateManager>();
builder.Services.AddSingleton<IFleetTemplateManager, FleetTemplateManager>();

// Security operations (task_055)
builder.Services.AddSingleton(appConfig.SecurityOps);
builder.Services.AddSingleton<IVulnerabilityReporter>(sp =>
    new VulnerabilityReporterService(
        sp.GetRequiredService<SecurityOpsConfig>(),
        sp.GetRequiredService<IAuditService>(),
        sp.GetRequiredService<ILogger<VulnerabilityReporterService>>()));
builder.Services.AddSingleton<ISecurityAuditExporter>(sp =>
    new SecurityAuditExporterService(
        sp.GetRequiredService<SecurityOpsConfig>(),
        sp.GetRequiredService<IAuditService>(),
        sp.GetRequiredService<ILogger<SecurityAuditExporterService>>()));

// Durable task lifecycle (task_018)
builder.Services.AddSingleton<ITaskRepository>(sp =>
    new SqliteTaskRepository(sp.GetRequiredService<SqliteSessionStore>()));
builder.Services.AddSingleton<TaskRetryHandler>();
builder.Services.AddSingleton<ITaskExecutionService>(sp =>
    new TaskExecutionService(
        sp.GetRequiredService<ITaskRepository>(),
        sp.GetRequiredService<ISessionStore>(),
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
{
    var guard = new QuotaGuard(
        sp.GetRequiredService<ILogger<QuotaGuard>>());
    // [task_085] Wire Otel.LoggingSampleRate.
    var otel = sp.GetService<OtelConfig>();
    if (otel is not null)
    {
        guard.SetLogSampleRate(otel.LoggingSampleRate);
    }
    return guard;
});

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
        TemplatesBaseDir = Path.Combine(AppContext.BaseDirectory, Hercules.BuiltIn.TemplatesSubdir)
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
        sp.GetRequiredService<ITraceSummarizer>(),
        sp.GetService<Hercules.Context.Distillation.ContextDistillationService>(),
        sp.GetRequiredService<Hercules.Storage.SqliteSessionStore>()));
// task_102: context distillation store + service.
builder.Services.AddSingleton<Hercules.Context.Distillation.IDistillationStore>(sp =>
    new Hercules.Context.Distillation.SqliteDistillationStore(
        sp.GetRequiredService<Hercules.Storage.SqliteSessionStore>(),
        sp.GetRequiredService<ILogger<Hercules.Context.Distillation.SqliteDistillationStore>>()));
builder.Services.AddSingleton<Hercules.Context.Distillation.ContextDistillationService>();

// [task_028] Caching — unified cache service
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Cache);
builder.Services.AddSingleton<ICacheService, CacheService>();

builder.Services.AddSingleton<ReflectionEngine>();
builder.Services.AddSingleton<AgentCore>();
// Регистрируем сервисы, поддерживающие hot-reload конфигурации, как IConfigReload
// чтобы RuntimeConfigReactor мог прокидывать им новые настройки без перезагрузки.
builder.Services.AddSingleton<IConfigReload>(sp => sp.GetRequiredService<AgentCore>());
builder.Services.AddSingleton<IConfigReload>(sp => sp.GetRequiredService<SkillManager>());
// [task_100] MCP hot-reload: при PATCH /api/config с mcp.servers секцией
// RuntimeConfigReactor вызовет McpClientService.Reload и применит изменения без рестарта.
builder.Services.AddSingleton<IConfigReload>(sp => sp.GetRequiredService<Hercules.Mcp.McpClientService>());

// Адаптер Web API
builder.Services.AddSingleton<WebApiAdapter>();

// task_080: shutdown & drain primitives
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Shutdown);
builder.Services.AddSingleton<IAgentLifecycleState, AgentLifecycleStateHolder>();
builder.Services.AddSingleton<IInFlightTracker>(sp => new InFlightTracker(sp.GetService<ILogger<InFlightTracker>>()));

// Escalations (task_049)
builder.Services.AddSingleton(appConfig.Escalation);
builder.Services.AddSingleton<IEscalationService, Hercules.Mesh.Escalation.EscalationService>();

// Operational SLOs (task_064)
builder.Services.AddSingleton(appConfig.Slos);
builder.Services.AddSingleton<Hercules.Slo.ISloLatencyTracker, Hercules.Slo.SloLatencyTracker>();
builder.Services.AddSingleton<Hercules.Slo.IConnectivityStateProvider>(sp =>
    sp.GetRequiredService<Hercules.Offline.NetworkMonitor>());
builder.Services.AddSingleton<ISloService>(sp =>
    new SloService(
        sp.GetRequiredService<SlosConfig>(),
        sp.GetRequiredService<IAuditService>(),
        sp.GetRequiredService<IMeshObservabilityService>(),
        sp.GetService<Hercules.Offline.IOutboxStore>(),
        sp.GetService<IBudgetService>(),
        sp.GetRequiredService<ILogger<SloService>>(),
        sp.GetService<Hercules.Slo.ISloLatencyTracker>(),
        sp.GetService<Hercules.Slo.IConnectivityStateProvider>()));

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
// task_081: заменили "AllowAnyOrigin" по умолчанию на dev whitelist (localhost).
// Для production укажите AllowedCorsOrigins в appsettings.json.
const string corsPolicy = "frontend";
builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicy, policy =>
    {
        if (webCfg.AllowAnyOrigin)
        {
            // Явный opt-in для dev/edge — НЕ рекомендуется для production.
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
            return;
        }

        if (webCfg.AllowedCorsOrigins.Count > 0)
        {
            policy.WithOrigins(webCfg.AllowedCorsOrigins.ToArray())
                .AllowAnyHeader()
                .AllowAnyMethod();
            return;
        }

        // Dev fallback: разрешаем только localhost-источники.
        // 4322 = Hercules Studio (Vite dev server), 8421 = сам WebApi.
        string[] devOrigins =
        {
            "http://localhost:4322",
            "http://localhost:8421",
            "http://127.0.0.1:4322",
            "http://127.0.0.1:8421"
        };
        policy.WithOrigins(devOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// --- Kestrel server tuning (task_081) ---
// Поднимаем лимиты до production-grade значений и ограничиваем request body.
builder.WebHost.ConfigureKestrel((ctx, opts) =>
{
    var k = webCfg.Kestrel;
    opts.Limits.MaxConcurrentConnections = k.MaxConcurrentConnections;
    opts.Limits.MaxConcurrentUpgradedConnections = k.MaxConcurrentUpgradedConnections;
    opts.Limits.MaxRequestBodySize = k.MaxRequestBodySize;
    opts.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(k.KeepAliveTimeoutSeconds);
    opts.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(k.RequestHeadersTimeoutSeconds);
});

// --- Framework rate limiter (task_081) ---
// Заменяет кастомный RateLimitMiddleware: fixed window per-IP для /api/chat и
// concurrency limiter для дорогих эндпоинтов (reflection, eval, SLO).
builder.Services.AddRateLimiter(o =>
{
    // Rejection handler сохраняет X-RateLimit-* headers + Retry-After.
    o.OnRejected = async (context, ct) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
            context.HttpContext.Response.Headers["Retry-After"] = seconds.ToString();
            context.HttpContext.Response.Headers["X-RateLimit-Reset"] = seconds.ToString();
        }

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"error\":\"Rate limit exceeded. Try again later.\"}", ct);
    };

    // /api/chat — fixed window per IP (30/min по умолчанию).
    o.AddPolicy(RateLimitPolicies.Chat, httpContext =>
    {
        var key = RateLimitPolicies.GetClientKey(httpContext);
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, webCfg.RateLimiting.ChatPerMinute),
            Window = TimeSpan.FromSeconds(Math.Max(1, webCfg.RateLimiting.ChatWindowSeconds)),
            QueueLimit = Math.Max(0, webCfg.RateLimiting.ChatQueueLimit),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });
    });

    // Expensive endpoints (reflection, eval, SLO) — глобальный concurrency cap.
    o.AddPolicy(RateLimitPolicies.Expensive, _ =>
        RateLimitPartition.GetConcurrencyLimiter("expensive", _ => new ConcurrencyLimiterOptions
        {
            PermitLimit = Math.Max(1, webCfg.RateLimiting.ExpensiveConcurrency),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        }));
});

// --- Response compression (task_081) ---
// Brotli + Gzip для application/json, text/plain и SSE (text/event-stream).
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
    o.MimeTypes =
    [
        "application/json",
        "text/plain",
        "text/event-stream",
        "application/xml",
        "text/html"
    ];
});

// --- Output cache (task_081) ---
// Кэшируем read-only GET-эндпоинты на короткий TTL.
builder.Services.AddOutputCache(o =>
{
    o.AddBasePolicy(b => b.Expire(TimeSpan.FromSeconds(30)));
    o.AddPolicy(OutputCachePolicies.Skills, b => b
        .Expire(TimeSpan.FromMinutes(5))
        .Tag("skills")
        .SetVaryByQuery("skillId", "includeDeprecated"));
    o.AddPolicy(OutputCachePolicies.Config, b => b
        .Expire(TimeSpan.FromSeconds(30))
        .Tag("config"));
});

// JSON: не экранировать кириллицу в ответах
builder.Services.ConfigureHttpJsonOptions(o => { o.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping; });

// Порт по умолчанию — 8421 (см. ADR-0003, диапазон 8421-8521).
// Если не переопределён через --urls / ASPNETCORE_URLS / launchSettings.
// launchSettings.json в Development может навязать другой URL, поэтому отключаем его влияние.
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")) &&
    !args.Any(a => a.StartsWith("--urls")) &&
    builder.Environment.IsProduction())
{
    builder.WebHost.UseUrls("http://0.0.0.0:8421");
}
else if (builder.Environment.IsDevelopment())
{
    // В Development игнорируем launchSettings URL и фиксируем порт 8421,
    // чтобы не зависеть от случайного порта в Properties/launchSettings.json.
    builder.WebHost.UseUrls("http://localhost:8421");
}

var app = builder.Build();
var log = app.Services.GetRequiredService<ILogger<Program>>();

// [task_109] OpenAPI document is exposed on /openapi/v1.json and Scalar
// interactive UI on /scalar. Both paths live outside /api/* so ApiKeyMiddleware
// already lets them through without X-Api-Key (open access to documentation).
// Important: MapOpenApi() MUST be called after all domain `MapXxx()` calls so
// the route table is complete before the OpenAPI document provider snapshots it.
app.MapOpenApi();
app.MapScalarApiReference();

// --- task_097: финализируем список API-ключей до первого запроса ---
// Если ни в appsettings, ни в legacy ApiKey ничего нет — генерируем пару и сохраняем
// в data/security/keys.json (ADR-0004). Делаем это ДО app.Run(), чтобы оператор увидел
// ключи в логах при первом старте.
if (!isBuildTime)
{
    var keyStore = app.Services.GetRequiredService<ApiKeyStore>();
    var resolved = keyStore.LoadOrGenerate(webCfg.ApiKeys);
    if (resolved.Count > 0)
    {
        // Перезаписываем snapshot конфига: middleware читает именно webCfg.ApiKeys.
        webCfg.ApiKeys.Clear();
        webCfg.ApiKeys.AddRange(resolved);
    }
}

// --- Middleware ---
// task_081: response compression first so downstream responses are emitted
// compressed (RateLimiter, ApiKey, Drain, OutputCache все пишут в поток).
app.UseResponseCompression();
app.UseCors(corsPolicy);
// task_080: drain check runs as early as possible so even a request that would be
// rejected by another middleware (e.g. CORS, ApiKey) still gets a clean 503.
app.UseMiddleware<DrainMiddleware>();
app.UseMiddleware<RequestBodyLimitMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();
// task_081: framework rate limiter replaces legacy RateLimitMiddleware.
app.UseRateLimiter();
// task_081: output cache (должен быть после ApiKey/Auth, чтобы не кэшировать 401/429).
app.UseOutputCache();
// task_039: peer auth middleware for /api/mesh/* endpoints
app.UseMiddleware<PeerAuthMiddleware>();

// --- Инициализация сессии агента ---
// [task_109] skipped at build-time (touches SQLite via SqliteSessionStore)
if (!isBuildTime)
{
    try
    {
        log.LogInformation("Initializing agent session...");
        app.Services.GetRequiredService<WebApiAdapter>().EnsureSessionStarted();
        log.LogInformation("Agent session initialized");
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Failed to initialize agent session");
        throw;
    }
}

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
        $"GET /{Hercules.BuiltIn.AgentManifestFileName}", "GET /api/mesh/agents", "POST /api/mesh/agents/register",
        "GET /api/mesh/agents/{id}", "DELETE /api/mesh/agents/{id}",
        "GET /api/mesh/capabilities", "GET /api/mesh/capabilities/{name}",
        "GET /api/mesh/capabilities/search", "POST /api/mesh/intent",
        "GET /api/tools", "GET /api/tools/{name}", "GET /api/tools/{name}/health",
        "GET /api/tools/categories", "POST /api/tools/{name}/enable", "POST /api/tools/{name}/disable",
        "POST /api/system/checkin", "POST /api/system/checkout",
        "POST /api/system/checkin/heartbeat", "GET /api/system/checkin/status",
        "POST /api/system/checkin/force"
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
app.MapGet($"/{Hercules.BuiltIn.AgentCardFileName}", () =>
{
    // agent-card.json публикуется в dataRoot при старте;.TryReadFromFile чтобы избежать
    // NRE если файл ещё не создан (например, CLI-only запуск)
    var dataRoot = app.Services.GetRequiredService<StorageConfig>().DataRoot;
    var cardPath = Path.Combine(dataRoot, Hercules.BuiltIn.AgentCardFileName);
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
// [task_109] skipped at build-time (writes files into DataRoot)
if (!isBuildTime)
{
    try
    {
        var manifestService = app.Services.GetRequiredService<AgentManifestService>();
        var manifest = manifestService.Save();
        var errors = manifestService.Validate();
        if (errors.Count > 0)
        {
                log.LogInformation("Manifest published with warnings: {Path}", manifestService.ManifestPath);
                foreach (var err in errors)
                {
                    log.LogWarning("  ⚠ {Warning}", err);
                }
        }
        else
        {
            log.LogInformation("Manifest published: {Path}", manifestService.ManifestPath);
        }
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Manifest publishing failed");
    }
}

// A2A Agent Card — публикация на startup (task_033)
// [task_109] skipped at build-time (writes files)
if (!isBuildTime)
{
    try
    {
        var agentCardService = app.Services.GetRequiredService<IAgentCardService>();
        var a2aConfig = app.Services.GetRequiredService<A2AConfig>();
        if (a2aConfig.AgentCard.Publish)
        {
            var path = await agentCardService.PublishAsync();
            log.LogInformation("AgentCard published: {Path}", path);
        }
        else
        {
            log.LogInformation("AgentCard publishing disabled (A2A.AgentCard.Publish = false)");
        }
    }
    catch (Exception ex)
    {
        log.LogError(ex, "AgentCard publishing failed");
    }
}

app.MapBudget();
app.MapQuotas();
app.MapAudit();
app.MapSecurityOps();
app.MapSystem();
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
app.MapToolRegistry();
app.MapMcpEndpoints();

log.LogInformation("Hercules Web API started on http://localhost:8421");
if (webCfg.ApiKeys.Count > 0)
{
    foreach (var entry in webCfg.ApiKeys)
    {
        var role = entry.Role == ApiKeyRole.System ? "system" : "contribute";
        log.LogInformation("X-Api-Key [{Role}]: {Key}", role, entry.Key);
    }
    if (webCfg.ApiKeys.Count > 0)
    {
        var keyStore = app.Services.GetRequiredService<Hercules.WebApi.Auth.ApiKeyStore>();
        if (File.Exists(keyStore.KeysFilePath))
        {
            log.LogInformation("Keys file: {Path}", keyStore.KeysFilePath);
        }
    }
}
else
{
    log.LogWarning("X-Api-Key: disabled (auth bypass)");
}
log.LogInformation("Data root: {DataRoot}", appConfig.Storage.DataRoot);

// Tool registry discovery (task_024)
// [task_109] skipped at build-time (filesystem I/O and side effects)
if (!isBuildTime)
{
    try
    {
        var registry = app.Services.GetRequiredService<IToolRegistryService>();
        var policyEngine = app.Services.GetService<ToolPolicyEngine>();
        var logger = app.Services.GetService<ILogger<Program>>();
        var discovered = ToolDiscovery.Discover(appConfig, registry, policyEngine, logger);
        log.LogInformation("[ToolRegistry] {Count} tools discovered from file system", discovered);
    }
    catch (Exception ex)
    {
        log.LogError(ex, "ToolRegistry discovery failed");
    }
}

// MCP client initialization (task_025)
// [task_109] skipped at build-time (network I/O via MCP servers)
if (!isBuildTime)
{
    try
    {
        var mcpService = app.Services.GetRequiredService<Hercules.Mcp.McpClientService>();
        await mcpService.InitializeAsync();
        log.LogInformation("[MCP] Client initialized: {Count} servers configured", mcpService.ServerStates.Count);
    }
    catch (Exception ex)
    {
        log.LogError(ex, "MCP initialization failed");
    }

    // task_108: recover durable tasks (Running/Paused) after a process restart and
    // apply checkpoint retention cleanup. Failures are logged but do not block startup.
    try
    {
        var taskExec = app.Services.GetRequiredService<ITaskExecutionService>();
        var recovered = await taskExec.RecoverIncompleteTasksAsync();
        if (recovered > 0)
        {
            log.LogInformation("[Tasks] Recovered {Count} incomplete durable task(s) on startup", recovered);
        }
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Tasks startup recovery failed");
    }

    app.Run();
}
