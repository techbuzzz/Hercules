using System.Text;
using Hercules.Agent;
using Hercules.Audit;
using Hercules.Backup;
using Hercules.Budget;
using Hercules.CLI;
using Hercules.CodeExecution;
using Hercules.Degradation;
using Hercules.Edge;
using Hercules.Fleet;
using Hercules.Cache;
using Hercules.Config;
using Hercules.Context;
using Hercules.Context.Summarizer;
using Hercules.LLM;
using Hercules.LLM.JsonRepair;
using Hercules.LLM.Providers;
using Hercules.Lifecycle;
using Hercules.Mesh.Transport;
using Hercules.Mesh.Abstractions;
using Hercules.Mcp;
using Hercules.Memory.Layers;
using Hercules.Mesh;
using Hercules.Mesh.Verification;
using Hercules.Offline;
using Hercules.Observability;
using Hercules.Quotas;
using Hercules.Redaction;
using Hercules.Security;
using Hercules.Skills;
using Hercules.Skills.Eval;
using Hercules.Skills.Marketplace;
using Hercules.Skills.Quality;
using Hercules.Skills.Routing;
using Hercules.Skills.Routing.ScoringComponents;
using Hercules.Skills.Routing.Deterministic;
using Hercules.Slo;
using Hercules.Simulation;
using Hercules.Reflection;
using Hercules.Storage;
using Hercules.Tasks;
using Hercules.Telegram;
using Hercules.Tools;
using Hercules.Tools.Approval;
using Hercules.Tools.Policy;
using Hercules.Tools.Registry;
using Hercules.WasmSandbox;
using Hercules.WasmSandbox.Compilation;
using HerculesBus;
using HerculesBus.Core;
using HerculesBus.InMemory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;

// ============================================================================
//  Hercules — самообучающийся микроагент (C# 15 / .NET 10)
//  Точка входа: настройка конфигурации, DI и запуск выбранного интерфейса.
// ============================================================================

// Поддержка корректного отображения кириллицы
Console.OutputEncoding = Encoding.UTF8;

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureAppConfiguration(config =>
{
    config.SetBasePath(AppContext.BaseDirectory)
        .AddUserSecrets<Program>()
        .AddJsonFile("appsettings.json", false, false)
        .AddEnvironmentVariables("HERCULES_");
});

// [task_085] Async-friendly JSON console logger. Replaces the default SimpleConsole
// logger so logs are line-buffered, non-blocking on the I/O path, and
// machine-parseable. SingleConsoleFormatter drops the extra blank line that
// default console output emits between records.
builder.ConfigureLogging(logging =>
{
    logging.ClearProviders();
    logging.AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        options.UseUtcTimestamp = true;
        options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions
        {
            Indented = false
        };
    });
});

builder.ConfigureServices((context, services) =>
{
    var appConfig = context.Configuration.Get<AppConfig>() ?? new AppConfig();

    // Конфигурационные секции
    services.AddSingleton(appConfig);
    services.AddSingleton(appConfig.Llm);
    services.AddSingleton(appConfig.Storage);
    services.AddSingleton(appConfig.Agent);
    services.AddSingleton(appConfig.Telegram);
    services.AddSingleton(appConfig.CodeExecution);
    services.AddSingleton(appConfig.Http);
    services.AddSingleton(appConfig.Mcp);
    services.AddSingleton(appConfig.A2A);
    services.AddSingleton(appConfig.Mesh);
    services.AddSingleton(appConfig.MeshProfiles);
    services.AddSingleton(appConfig.ToolPolicy);
    services.AddSingleton(appConfig.Phase2);
    services.AddSingleton(appConfig.SkillQuality);
    services.AddSingleton(appConfig.LeastPrivilege);

    // OpenTelemetry (task_013) — tracing + metrics
    services.AddHerculesOtel(appConfig.Otel);

    // task_078: IHttpClientFactory + named clients with standard resilience handlers.
    // Default factory for ad-hoc CreateClient() calls; named clients used by tools/transport.
    services.AddHttpClient();

    var httpResilience = appConfig.Http.Resilience ?? new HttpResilienceConfig();

    // LLM health/probe clients (no resilience — these are already best-effort)
    services.AddHttpClient(ProviderHealthChecker.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(ProviderHealthChecker.HealthCheckTimeout.TotalSeconds);
        });
    services.AddHttpClient(ProviderCapabilityDetector.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(8);
        });
    services.AddHttpClient(LMStudioClient.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(5);
        });

    // Degradation/network: short probe
    services.AddHttpClient(NetworkMonitor.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(appConfig.OfflineSync.NetworkPollTimeoutSeconds);
        });

    // Inter-agent transports (retry + circuit breaker + timeout)
    services.AddHttpClient(IntentTransport.HttpClientName, c =>
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

    services.AddHttpClient(HttpTransportAdapter.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromMilliseconds(appConfig.Mesh.IntentTimeoutMs);
        }).AddStandardResilienceHandler(o =>
        {
            o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(httpResilience.TimeoutSeconds);
            o.Retry.MaxRetryAttempts = httpResilience.RetryCount;
            o.Retry.Delay = TimeSpan.FromMilliseconds(httpResilience.RetryBaseDelayMs);
            o.Retry.UseJitter = true;
        });

    services.AddHttpClient(GrpcTransportAdapter.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromMilliseconds(appConfig.Mesh.IntentTimeoutMs);
        });

    // Outbound tool/agent clients (retry on transient)
    services.AddHttpClient(HttpTool.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(appConfig.Http.TimeoutSeconds);
        }).AddStandardResilienceHandler(o =>
        {
            o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(httpResilience.TimeoutSeconds);
            o.Retry.MaxRetryAttempts = httpResilience.RetryCount;
            o.Retry.Delay = TimeSpan.FromMilliseconds(httpResilience.RetryBaseDelayMs);
            o.Retry.UseJitter = true;
        });

    services.AddHttpClient(A2AClient.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(appConfig.A2A.TimeoutSeconds);
        }).AddStandardResilienceHandler(o =>
        {
            o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(httpResilience.TimeoutSeconds);
            o.Retry.MaxRetryAttempts = httpResilience.RetryCount;
            o.Retry.Delay = TimeSpan.FromMilliseconds(httpResilience.RetryBaseDelayMs);
            o.Retry.UseJitter = true;
        });

    // Operator notify (webhook/telegram) — best effort, soft retry
    services.AddHttpClient(OperatorNotificationService.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(10);
        }).AddStandardResilienceHandler(o =>
        {
            o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(httpResilience.TimeoutSeconds);
            o.Retry.MaxRetryAttempts = Math.Max(1, httpResilience.RetryCount - 1);
            o.Retry.Delay = TimeSpan.FromMilliseconds(httpResilience.RetryBaseDelayMs);
            o.Retry.UseJitter = true;
        });

    // Skill marketplace HTTP import
    services.AddHttpClient(SkillMarketplace.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromMinutes(2);
        });

    // LLM-слой (отказоустойчивый клиент с fallback + multi-role routing v2)
    services.AddSingleton<LlmClientFactory>(sp =>
        new LlmClientFactory(
            sp.GetRequiredService<LlmConfig>(),
            sp.GetRequiredService<ICacheService>(),
            sp.GetService<IHttpClientFactory>()));
    services.AddSingleton<ILLMClientFactory>(sp => sp.GetRequiredService<LlmClientFactory>());
    services.AddSingleton<RoleRouter>();
    services.AddSingleton<IJsonRepairService, JsonRepairService>();
    services.AddSingleton<ResilientLLMClient>(sp =>
    {
        var client = new ResilientLLMClient(
            sp.GetRequiredService<LlmConfig>(),
            sp.GetRequiredService<LlmClientFactory>(),
            sp.GetRequiredService<RoleRouter>(),
            sp.GetRequiredService<ILogger<ResilientLLMClient>>());
        // [task_085] Wire Otel.LoggingSampleRate into the LLM client so retry
        // warnings are sampled at the same rate as the rest of the agent.
        var otel = sp.GetService<OtelConfig>();
        if (otel is not null)
        {
            client.SetLogSampleRate(otel.LoggingSampleRate);
        }
        return client;
    });
    services.AddSingleton<ILLMClient>(sp => sp.GetRequiredService<ResilientLLMClient>());
    services.AddSingleton<ProviderHealthChecker>(sp =>
        new ProviderHealthChecker(
            sp.GetRequiredService<LlmConfig>(),
            sp.GetService<ILogger<ProviderHealthChecker>>(),
            sp.GetService<IHttpClientFactory>()));
    services.AddSingleton<ProviderCapabilityDetector>(sp =>
        new ProviderCapabilityDetector(
            sp.GetRequiredService<LlmConfig>(),
            sp.GetService<ILogger<ProviderCapabilityDetector>>(),
            sp.GetRequiredService<ICacheService>(),
            sp.GetService<IHttpClientFactory>()));

    // Code execution (Stage 2, v2)
    services.AddSingleton<SandboxOptions>(sp =>
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
    services.AddSingleton<ICodeExecutor, DotnetFileBasedExecutor>();

    // SkillSdk context factory + in-process executor (task_101)
    services.AddSingleton<ISkillContextFactory>(sp =>
        new SkillSdkContextFactory(
            sp.GetRequiredService<HttpConfig>(),
            sp.GetRequiredService<ILLMClient>(),
            sp.GetRequiredService<ToolRegistry>(),
            sp.GetRequiredService<LayeredMemoryManager>(),
            sp.GetRequiredService<IDurableFactsStore>(),
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetService<IHttpClientFactory>()));
    services.AddSingleton<SkillSdkExecutor>(sp =>
        new SkillSdkExecutor(
            sp.GetRequiredService<SandboxOptions>(),
            sp.GetRequiredService<ISkillContextFactory>()));
    services.AddSingleton<ICodeExecutor>(sp => sp.GetRequiredService<SkillSdkExecutor>());

    // Tool ecosystem (Stage 3, v2)
    services.AddSingleton<ITool, HttpTool>(sp =>
        new HttpTool(
            sp.GetRequiredService<HttpConfig>(),
            sp.GetRequiredService<ILogger<HttpTool>>(),
            sp.GetService<IHttpClientFactory>()));
    services.AddSingleton<ITool, A2AClient>(sp =>
        new A2AClient(
            sp.GetRequiredService<A2AConfig>(),
            sp.GetService<IHttpClientFactory>()));
    services.AddSingleton<ITool, CodeExecutionTool>(sp => new CodeExecutionTool(sp));
    // Tool policy engine (task_009) — registered before ToolRegistry so it can be injected
    services.AddSingleton(sp =>
    {
        var cfg = sp.GetRequiredService<ToolPolicyConfig>();
        var perms = ToolPermissionExtensions.ParseFromString(cfg.AgentPermissions);
        return new ToolPermissionSet(perms);
    });
    // Approval gates (task_010)
    services.AddSingleton(appConfig.Approval);
    services.AddSingleton<IApprovalService>(sp =>
        new ApprovalService(
            sp.GetRequiredService<ApprovalConfig>(),
            sp.GetRequiredService<SqliteSessionStore>(),
            sp.GetRequiredService<ILogger<ApprovalService>>()));
    services.AddSingleton<ToolPolicyEngine>(sp =>
        new ToolPolicyEngine(
            sp.GetRequiredService<ToolPolicyConfig>(),
            sp.GetRequiredService<ToolPermissionSet>(),
            sp.GetRequiredService<ILogger<ToolPolicyEngine>>(),
            sp.GetRequiredService<IApprovalService>(),
            sp.GetRequiredService<IAuditService>(),
            sp.GetRequiredService<Hercules.Tools.Grants.ISkillGrantService>()));
    services.AddSingleton<ToolRegistry>();
    services.AddSingleton(appConfig.ToolRegistry);
    services.AddSingleton<IToolRegistryService, ToolRegistryService>();
    services.AddHostedService<ToolHealthService>();
    services.AddSingleton<Hercules.Mcp.McpClientService>();
    services.AddHostedService<Hercules.Mcp.McpServerHost>();

    // WASM sandbox (v3)
    services.AddSingleton<IWasmSandbox, WasmtimeSandbox>();
    services.AddSingleton<CompilerRegistry>(sp =>
    {
        var registry = new CompilerRegistry();
        registry.Register(new PassthroughCompiler());
        return registry;
    });
    services.AddSingleton<WasmTool>();
    services.AddSingleton<ITool, WasmToolAdapter>();

    // HerculesBus (v3.1)
    services.AddSingleton<IChannelStore, InMemoryChannelStore>();
    services.AddSingleton<IAgentRegistry, InMemoryAgentRegistry>();
    // task_086: pass BusConfig so the bus uses bounded channels with the
    // configured backpressure / drop policy.
    services.AddSingleton<IEventBus>(sp => new InMemoryEventBus(
        sp.GetRequiredService<ILogger<InMemoryEventBus>>(),
        appConfig.Bus));
    services.AddSingleton<Bus>();

    // Phase 3: Inter-agent mesh
    services.AddMeshServices(appConfig, appConfig.Storage.DataRoot);

    // Phase 4: Verification pipeline (task_046)
    // Register no-op pipeline as fallback; AddMeshServices overrides with real pipeline when enabled.
    services.AddSingleton<IVerificationPipeline>(sp =>
        new VerificationPipeline(
            Array.Empty<IVerifier>(),
            new VerificationConfig { Enabled = false },
            sp.GetRequiredService<ILogger<VerificationPipeline>>()));

    // Хранилища
    services.AddSingleton<FileSkillRepository>();
    services.AddSingleton<MemoryStore>(sp =>
        new MemoryStore(
            sp.GetRequiredService<StorageConfig>(),
            sp.GetRequiredService<SecretsConfig>(),
            sp.GetRequiredService<ISecretMaskingService>()));
    services.AddSingleton<SqliteSessionStore>();

    // task_026: Least-privilege grants
    services.AddSingleton(sp =>
        new Hercules.Tools.Grants.SkillGrantStore(
            Path.Combine(sp.GetRequiredService<StorageConfig>().DataRoot, "grants.db")));
    services.AddSingleton<Hercules.Tools.Grants.ISkillGrantService, Hercules.Tools.Grants.SkillGrantService>();

    // Layered memory (task_011, task_075 H6)
    services.AddSingleton(appConfig.Memory);
    services.AddSingleton<LayeredMemoryConfig>(sp =>
    {
        var cfg = sp.GetRequiredService<MemoryConfig>();
        return new LayeredMemoryConfig
        {
            MaxWorkingMemoryEntries = cfg.MaxWorkingMemoryEntries,
            MaxEpisodesInContext = cfg.MaxEpisodesInContext,
            DefaultFactTtlMinutes = cfg.DefaultFactTtlMinutes,
            SensitivityRedactionEnabled = cfg.SensitivityRedactionEnabled,
            MaxFactAgeDays = cfg.MaxFactAgeDays
        };
    });
    services.AddSingleton<IDurableFactsStore, DurableFactsService>();
    services.AddSingleton<IEpisodicStore, EpisodicStore>();
    services.AddSingleton<LayerMetadataExtractor>();
    // task_075 H6 fix: LayeredMemoryManager is now singleton and resolves working memory
    // per session via ISessionStateStore (no more captive dependency on scoped IWorkingMemory).
    services.AddSingleton<ISessionStateStore, InMemorySessionStateStore>();
    services.AddSingleton<LayeredMemoryManager>(sp => new LayeredMemoryManager(
        sp.GetRequiredService<ISessionStateStore>(),
        sp.GetRequiredService<IDurableFactsStore>(),
        sp.GetRequiredService<IEpisodicStore>(),
        sp.GetRequiredService<LayeredMemoryConfig>()));

    // Hybrid storage services (task_003)
    services.AddSingleton<IBudgetService, BudgetService>();
    services.AddSingleton<IAuditLog, AuditLogService>();

    // Budget and guardrails (task_012)
    services.AddSingleton(appConfig.Budget);
    services.AddSingleton<IGuardrailService>(sp =>
        new GuardrailService(
            sp.GetRequiredService<BudgetConfig>(),
            sp.GetRequiredService<IBudgetService>()));
    services.AddSingleton<BudgetGuard>(sp =>
        new BudgetGuard(
            sp.GetRequiredService<BudgetConfig>(),
            sp.GetRequiredService<ILogger<BudgetGuard>>()));

    // Rate limits and quotas (task_056, task_072)
    services.AddSingleton(appConfig.Quotas);
    services.AddSingleton<QuotaService>(sp =>
        new QuotaService(
            sp.GetRequiredService<QuotasConfig>(),
            sp.GetRequiredService<ILogger<QuotaService>>()));
    // IQuotaService: distributed wrapper when Quotas.DistributedEnabled, in-memory otherwise.
    // DistributedQuotaService is a decorator over QuotaService that mirrors rate-limit
    // counters to IMeshStateStore and falls back to in-memory when the store is unreachable.
    services.AddSingleton<IQuotaService>(sp =>
    {
        var cfg = sp.GetRequiredService<QuotasConfig>();
        var inner = sp.GetRequiredService<QuotaService>();
        if (cfg.DistributedEnabled && sp.GetService<Hercules.Mesh.Abstractions.IMeshStateStore>() is { } store)
        {
            return new DistributedQuotaService(
                inner,
                store,
                cfg,
                sp.GetRequiredService<ILogger<DistributedQuotaService>>());
        }
        return inner;
    });
    services.AddSingleton<QuotaGuard>(sp =>
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
    // Periodic sweep of rate-limit buckets so they don't accumulate between queries (task_072)
    services.AddHostedService<QuotaCleanupBackgroundService>();

    // Phase 2: Skill packager
    services.AddSingleton(appConfig.Marketplace);
    services.AddSingleton<IMarketplaceSigningService>(sp =>
        new MarketplaceSigningService(sp.GetRequiredService<MarketplaceConfig>()));
    services.AddSingleton<SkillPackager>(sp =>
        new SkillPackager(
            sp.GetRequiredService<FileSkillRepository>(),
            sp.GetRequiredService<SecretsConfig>(),
            sp.GetRequiredService<ISecretMaskingService>(),
            sp.GetRequiredService<IMarketplaceSigningService>(),
            sp.GetRequiredService<Hercules.Config.LeastPrivilegeConfig>(),
            sp.GetRequiredService<Hercules.Tools.Grants.ISkillGrantService>()));

    // Phase 2: Semantic routing (task_022)
    services.AddSingleton<IEmbeddingProvider, StubEmbeddingProvider>();

    // Scoring components
    services.AddSingleton<LexicalScorer>();
    services.AddSingleton<HistoricalQualityScorer>();
    services.AddSingleton<SchemaCompatibilityScorer>(sp =>
        new SchemaCompatibilityScorer(
            sp.GetService<ToolRegistry>()?.Names ?? Enumerable.Empty<string>()));
    services.AddSingleton<LatencyScorer>();
    services.AddSingleton<PolicyEligibilityScorer>(sp =>
        new PolicyEligibilityScorer(
            sp.GetService<ToolPolicyConfig>(),
            sp.GetService<ToolPolicyConfig>()?.AgentPermissions.Split('|') ?? Enumerable.Empty<string>(),
            sp.GetService<ToolRegistry>()?.Names ?? Enumerable.Empty<string>()));

    // Embedding scorer (requires IEmbeddingProvider)
    services.AddSingleton<EmbeddingScorer>(sp =>
        new EmbeddingScorer(
            sp.GetRequiredService<IEmbeddingProvider>(),
            sp.GetRequiredService<SkillManager>(),
            sp.GetRequiredService<Phase2Config>().SimilarityThreshold,
            sp.GetRequiredService<ICacheService>()));

    // Skill scoring engine
    services.AddSingleton<ISkillScoringEngine>(sp =>
        new SkillScoringEngine(
            sp.GetRequiredService<SkillManager>(),
            sp.GetRequiredService<Phase2Config>(),
            new ISkillScorer[]
            {
                sp.GetRequiredService<EmbeddingScorer>(),
                sp.GetRequiredService<LexicalScorer>(),
                sp.GetRequiredService<SchemaCompatibilityScorer>(),
                sp.GetRequiredService<HistoricalQualityScorer>(),
                sp.GetRequiredService<LatencyScorer>(),
                sp.GetRequiredService<PolicyEligibilityScorer>(),
                sp.GetRequiredService<SkillQualityScorer>(),
            },
            sp.GetService<ToolRegistry>()?.Names ?? Enumerable.Empty<string>(),
            sp.GetService<EmbeddingScorer>()));

    // Task 023: Deterministic router (offline-safe keyword + tag + type matching)
    services.AddSingleton<IDeterministicRouter>(sp =>
        new DeterministicRouter(
            sp.GetRequiredService<SkillManager>(),
            sp.GetRequiredService<Phase2Config>().DeterministicRouting,
            sp.GetRequiredService<ICacheService>()));

    // EmbeddingSkillRouter (wraps SkillScoringEngine)
    services.AddSingleton<EmbeddingSkillRouter>(sp =>
        new EmbeddingSkillRouter(
            sp.GetRequiredService<SkillManager>(),
            sp.GetRequiredService<Phase2Config>(),
            sp.GetRequiredService<SkillRouter>(),
            sp.GetService<ISkillScoringEngine>(),
            sp.GetService<IDeterministicRouter>()));

    // Phase 2: Skill marketplace + agent templates (task_021)
    services.AddSingleton<SkillQualityStore>(sp =>
        new SkillQualityStore(sp.GetRequiredService<StorageConfig>()));
    services.AddSingleton<ISkillQualityService>(sp =>
        new SkillQualityService(
            sp.GetRequiredService<SkillQualityStore>(),
            sp.GetRequiredService<SkillQualityConfig>()));
    services.AddSingleton<SkillQualityScorer>(sp =>
        new SkillQualityScorer(sp.GetRequiredService<ISkillQualityService>()));

    services.AddSingleton<SkillMarketplace>(sp =>
        new SkillMarketplace(
            sp.GetRequiredService<StorageConfig>(),
            sp.GetRequiredService<SkillPackager>(),
            sp.GetRequiredService<IMarketplaceSigningService>(),
            sp.GetService<IHttpClientFactory>()));
    services.AddSingleton<AgentTemplateManager>();

    // Fleet templates (task_062)
    services.AddSingleton<IFleetTemplateManager, FleetTemplateManager>();

    // Template simulation (task_031)
    services.AddSingleton<ISensorSimulator>(sp =>
        new FileSensorSimulator(sp.GetRequiredService<ILogger<FileSensorSimulator>>())
        {
            TemplatesBaseDir = Path.Combine(AppContext.BaseDirectory, "templates")
        });
    services.AddSingleton<FailureScenarioEngine>();
    services.AddSingleton<TemplateSimulationService>();

    // Skill lifecycle (task_005)
    services.AddSingleton<SkillLifecyclePolicy>();
    services.AddSingleton<SkillDeprecationManager>();
    services.AddSingleton<SkillEvaluationEngine>();
    services.AddSingleton<SkillLifecycleService>();

    // Skill manifest & compatibility (task_020)
    services.AddSingleton(appConfig.Phase2.SkillManifest);
    services.AddSingleton<SkillManifestValidator>(sp =>
    {
        var cfg = sp.GetRequiredService<SkillManifestConfig>();
        var allowedLevels = cfg.AllowedRiskLevels.Count > 0
            ? cfg.AllowedRiskLevels.Select(i => (Hercules.Skills.SkillRiskLevel)i).ToList()
            : (IReadOnlyList<Hercules.Skills.SkillRiskLevel>?)null;
        return new SkillManifestValidator(cfg.CurrentHerculesVersion, null, allowedLevels);
    });

    // Eval harness (task_016)
    services.AddSingleton(appConfig.Eval);
    services.AddSingleton<BaselineManager>();
    services.AddSingleton<SkillTestGenerator>();
    services.AddSingleton<IEvalHarnessService, EvalHarnessService>();

    // Self-improvement (task_017)
    services.AddSingleton(appConfig.SelfImprovement);
    services.AddSingleton<global::Hercules.Reflection.ProposalStore>();
    services.AddSingleton<global::Hercules.Reflection.ProposalDiffer>();
    services.AddSingleton<global::Hercules.Reflection.MaintenanceWorkflow>();
    services.AddSingleton<global::Hercules.Reflection.SelfImprovementService>();

    // Durable task lifecycle (task_018)
    services.AddSingleton(appConfig.Tasks);
    services.AddSingleton<ITaskRepository>(sp =>
        new SqliteTaskRepository(sp.GetRequiredService<SqliteSessionStore>()));
    services.AddSingleton<TaskRetryHandler>();
    services.AddSingleton<ITaskExecutionService>(sp =>
        new TaskExecutionService(
            sp.GetRequiredService<ITaskRepository>(),
            sp.GetRequiredService<ISessionStore>(),
            sp.GetRequiredService<TaskConfig>(),
            sp.GetRequiredService<ILogger<TaskExecutionService>>()));

    // Audit and privacy (task_014)
    services.AddSingleton(appConfig.Audit);
    services.AddSingleton<PayloadHashService>();
    services.AddSingleton<IRedactionService>(sp =>
        new RedactionService(
            sp.GetRequiredService<AuditConfig>(),
            sp.GetRequiredService<ILogger<RedactionService>>()));
    services.AddSingleton<IAuditService>(sp =>
        new AuditService(
            sp.GetRequiredService<IAuditLog>(),
            sp.GetRequiredService<AuditConfig>(),
            sp.GetService<IRedactionService>(),
            sp.GetRequiredService<PayloadHashService>(),
            sp.GetRequiredService<ILogger<AuditService>>()));

    // Secrets and redaction (task_015)
    services.AddSingleton(appConfig.Secrets);
    services.AddSingleton<ISecretMaskingService>(sp =>
        new SecretMaskingService(
            sp.GetRequiredService<SecretsConfig>(),
            sp.GetRequiredService<IRedactionService>()));

    // Security operations (task_055): fleet identity, certificates, package signing, vulnerability reporting, audit export
    services.AddSingleton(appConfig.SecurityOps);
    services.AddSingleton<IFleetIdentityService>(sp =>
        new FleetIdentityService(
            sp.GetRequiredService<SecurityOpsConfig>(),
            sp.GetRequiredService<IAuditService>(),
            sp.GetRequiredService<ILogger<FleetIdentityService>>()));
    services.AddSingleton<ICertificateService>(sp =>
        new CertificateService(
            sp.GetRequiredService<SecurityOpsConfig>(),
            sp.GetRequiredService<IAuditService>(),
            sp.GetRequiredService<ILogger<CertificateService>>()));
    services.AddSingleton<IPackageSigningService>(sp =>
        new PackageSigningService(
            sp.GetRequiredService<SecurityOpsConfig>(),
            sp.GetRequiredService<IAuditService>(),
            sp.GetRequiredService<ILogger<PackageSigningService>>()));
    services.AddSingleton<IVulnerabilityReporter>(sp =>
        new VulnerabilityReporterService(
            sp.GetRequiredService<SecurityOpsConfig>(),
            sp.GetRequiredService<IAuditService>(),
            sp.GetRequiredService<ILogger<VulnerabilityReporterService>>()));
    services.AddSingleton<ISecurityAuditExporter>(sp =>
        new SecurityAuditExporterService(
            sp.GetRequiredService<SecurityOpsConfig>(),
            sp.GetRequiredService<IAuditService>(),
            sp.GetRequiredService<ILogger<SecurityAuditExporterService>>()));

    // task_059: Edge provisioning — identity enrollment, cert activation, secure defaults on Raspberry Pi
    services.AddSingleton(appConfig.Edge);
    services.AddSingleton<IEdgeProvisioningService>(sp =>
        new EdgeProvisioningService(
            sp.GetRequiredService<EdgeConfig>(),
            sp.GetRequiredService<SecurityOpsConfig>(),
            sp.GetRequiredService<StorageConfig>(),
            sp.GetRequiredService<IFleetIdentityService>(),
            sp.GetRequiredService<ICertificateService>(),
            sp.GetRequiredService<IAuditService>(),
            sp.GetRequiredService<ILogger<EdgeProvisioningService>>()));

    // task_060: Offline resilience — bounded outbox queue for sensor logs, task results, alerts
    services.AddSingleton(appConfig.OfflineSync);
    services.AddSingleton<IOutboxStore>(sp =>
        new SqliteOutboxStore(
            sp.GetRequiredService<SqliteSessionStore>(),
            sp.GetRequiredService<OfflineSyncConfig>(),
            sp.GetRequiredService<ILogger<SqliteOutboxStore>>()));
    services.AddSingleton<NetworkMonitor>(sp =>
        new NetworkMonitor(
            sp.GetRequiredService<OfflineSyncConfig>(),
            sp.GetRequiredService<ILogger<NetworkMonitor>>(),
            sp.GetService<IHttpClientFactory>()!));
    services.AddSingleton<INetworkMonitor>(sp => sp.GetRequiredService<NetworkMonitor>());
    services.AddSingleton<OfflineSyncService>(); // BackgroundService

    // task_061: Local-first degradation — deterministic fallback, operator notifications, observability
    services.AddSingleton(appConfig.Degradation);
    services.AddSingleton<DegradationObservability>();
    services.AddSingleton<OperatorNotificationService>(sp =>
        new OperatorNotificationService(
            sp.GetRequiredService<DegradationConfig>(),
            sp.GetRequiredService<ILogger<OperatorNotificationService>>(),
            sp.GetService<IHttpClientFactory>()!));
    services.AddSingleton<DegradationManager>(sp =>
        new DegradationManager(
            sp.GetRequiredService<DegradationConfig>(),
            sp.GetRequiredService<OperatorNotificationService>(),
            sp.GetService<ILLMClient>(),
            sp.GetService<INetworkMonitor>(),
            sp.GetService<ProviderHealthChecker>(),
            sp.GetService<LlmConfig>(),
            sp.GetService<IMeshBus>(),
            sp.GetService<FileSkillRepository>(),
            sp.GetRequiredService<ILogger<DegradationManager>>())); // BackgroundService

    // task_063: Backup & Recovery — encrypted backup archives, scheduled backups, restore
    services.AddSingleton(appConfig.Backup);
    services.AddSingleton<EncryptionService>();
    services.AddSingleton<IBackupService>(sp =>
        new BackupService(
            sp.GetRequiredService<BackupConfig>(),
            sp.GetRequiredService<EncryptionService>(),
            sp.GetRequiredService<ILogger<BackupService>>(),
            sp.GetRequiredService<StorageConfig>().DataRoot));
    services.AddHostedService<BackupScheduler>();

    // task_064: Operational SLOs — availability, response-time, data-loss, recovery-time, cost targets
    services.AddSingleton(appConfig.Slos);
    // task_087: real P95 latency tracker + connectivity provider feed the SLO
    // service with measured values instead of the previous synthetic heuristics.
    services.AddSingleton<Hercules.Slo.ISloLatencyTracker, Hercules.Slo.SloLatencyTracker>();
    services.AddSingleton<Hercules.Slo.IConnectivityStateProvider>(sp =>
        sp.GetRequiredService<Hercules.Offline.NetworkMonitor>());
    services.AddSingleton<ISloService>(sp =>
        new SloService(
            sp.GetRequiredService<SlosConfig>(),
            sp.GetRequiredService<IAuditService>(),
            sp.GetRequiredService<Hercules.Mesh.Observability.IMeshObservabilityService>(),
            sp.GetService<Hercules.Offline.IOutboxStore>(),
            sp.GetService<IBudgetService>(),
            sp.GetRequiredService<ILogger<SloService>>(),
            sp.GetService<Hercules.Slo.ISloLatencyTracker>(),
            sp.GetService<Hercules.Slo.IConnectivityStateProvider>()));

    // Агент
    services.AddSingleton<SkillManager>();
    services.AddSingleton<SkillRouter>();
    services.AddSingleton<MemoryManager>();
    services.AddSingleton<LayeredMemoryManager>();

    // [task_027] Context Assembly
    services.AddSingleton(appConfig.Context);
    services.AddSingleton<ITraceSummarizer, TraceSummarizer>();
    services.AddSingleton<IContextBuilder>(sp =>
        new ContextBuilder(
            sp.GetRequiredService<LayeredMemoryManager>(),
            sp.GetRequiredService<ContextConfig>(),
            sp.GetRequiredService<ILogger<ContextBuilder>>(),
            sp.GetRequiredService<ITraceSummarizer>(),
            sp.GetService<Hercules.Context.Distillation.ContextDistillationService>(),
            sp.GetRequiredService<SqliteSessionStore>()));

    // task_102: context distillation store + service.
    services.AddSingleton<Hercules.Context.Distillation.IDistillationStore>(sp =>
        new Hercules.Context.Distillation.SqliteDistillationStore(
            sp.GetRequiredService<SqliteSessionStore>(),
            sp.GetRequiredService<ILogger<Hercules.Context.Distillation.SqliteDistillationStore>>()));
    services.AddSingleton<Hercules.Context.Distillation.ContextDistillationService>();

    // [task_028] Caching — unified cache service
    services.AddSingleton(appConfig.Cache);
    services.AddSingleton<ICacheService, CacheService>();

    services.AddSingleton<ReflectionEngine>();
    services.AddSingleton<AgentCore>();

    // task_080: shutdown & drain primitives
    services.AddSingleton(appConfig.Shutdown);
    services.AddSingleton<IAgentLifecycleState, AgentLifecycleStateHolder>();
    services.AddSingleton<IInFlightTracker>(sp => new InFlightTracker(sp.GetService<ILogger<InFlightTracker>>()));

    // task_057: Lifecycle management
    services.AddSingleton<ILifecycleService>(sp =>
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
    services.AddHostedService<DrainHostedService>();

    // Интерфейсы
    services.AddSingleton<ConsoleUI>();
    services.AddSingleton<TelegramBotInterface>();
});

using var host = builder.Build();

var appConfig = host.Services.GetRequiredService<AppConfig>();

// task_059: Edge provisioning — enroll on startup if not already enrolled
try
{
    var edgeService = host.Services.GetRequiredService<IEdgeProvisioningService>();
    if (!edgeService.IsEnrolled)
    {
        var result = await edgeService.EnsureEnrolledAsync();
        if (result.Success)
        {
            Console.WriteLine($"[Edge] Device enrolled: {result.DeviceId}");
        }
        else
        {
            Console.WriteLine($"[Edge] Enrollment deferred: {result.ErrorMessage}");
        }
    }
}
catch (Exception ex)
{
    // Enrollment failure is non-fatal — agent starts in standalone mode
    Console.WriteLine($"[Edge] Enrollment error (non-fatal): {ex.Message}");
}

// Tool registry discovery (task_024)
try
{
    var registry = host.Services.GetRequiredService<IToolRegistryService>();
    var policyEngine = host.Services.GetService<ToolPolicyEngine>();
    var logger = host.Services.GetService<ILogger>();
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
    var mcpService = host.Services.GetRequiredService<Hercules.Mcp.McpClientService>();
    await mcpService.InitializeAsync();
    Console.WriteLine($"[MCP] Client initialized: {mcpService.ServerStates.Count} servers configured");
}
catch (Exception ex)
{
    Console.WriteLine($"[MCP] Initialization failed: {ex.Message}");
}

// Agent manifest — публикация на startup (task_032)
try
{
    var manifestService = host.Services.GetRequiredService<AgentManifestService>();
    var manifest = manifestService.Save();
    var errors = manifestService.Validate();
    if (errors.Count > 0)
    {
        Console.WriteLine($"[Manifest] Опубликован с предупреждениями: {manifestService.ManifestPath}");
        foreach (string err in errors)
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

// --- Выбор режима запуска ---
using var cts = new CancellationTokenSource();
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    // task_080: route shutdown through IHostApplicationLifetime so the
    // DrainHostedService gets a chance to wait for in-flight work before
    // the process exits. We still cancel the local CTS so the active run
    // loop (ConsoleUI / Telegram) can stop accepting new input.
    cts.Cancel();
    lifetime.StopApplication();
};

var telegramMode = args.Contains("--telegram") || (appConfig.Telegram.Enabled && args.Contains("--bot"));
var benchmarkMode = args.Contains("--benchmark");

try
{
    if (benchmarkMode)
    {
        Console.WriteLine("Hercules Benchmark Suite");
        Console.WriteLine("=======================\n");

        var agent = host.Services.GetRequiredService<AgentCore>();
        var skills = host.Services.GetRequiredService<SkillManager>();
        var sessions = host.Services.GetRequiredService<SqliteSessionStore>();
        var budget = (BudgetService)host.Services.GetRequiredService<IBudgetService>();
        var memory = host.Services.GetRequiredService<MemoryStore>();

        var runner = new BenchmarkRunner(agent, skills, sessions, budget, memory);
        await runner.RunAsync(ct: cts.Token);
    }
    else if (telegramMode)
    {
        TelegramBotInterface bot = host.Services.GetRequiredService<TelegramBotInterface>();
        Console.WriteLine("Запуск в режиме Telegram-бота. Ctrl+C для остановки.");
        await bot.RunAsync(cts.Token);
    }
    else
    {
        ConsoleUI ui = host.Services.GetRequiredService<ConsoleUI>();
        await ui.RunAsync(cts.Token);
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("\nОстановлено пользователем.");
}