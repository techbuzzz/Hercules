using System.Text;
using Hercules.Agent;
using Hercules.Audit;
using Hercules.Budget;
using Hercules.CLI;
using Hercules.CodeExecution;
using Hercules.Cache;
using Hercules.Config;
using Hercules.Context;
using Hercules.Context.Summarizer;
using Hercules.LLM;
using Hercules.LLM.JsonRepair;
using Hercules.Mcp;
using Hercules.Memory.Layers;
using Hercules.Mesh;
using Hercules.Observability;
using Hercules.Redaction;
using Hercules.Reflection;
using Hercules.Skills;
using Hercules.Skills.Eval;
using Hercules.Skills.Marketplace;
using Hercules.Skills.Quality;
using Hercules.Skills.Routing;
using Hercules.Skills.Routing.ScoringComponents;
using Hercules.Skills.Routing.Deterministic;
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
    services.AddSingleton(appConfig.ToolPolicy);
    services.AddSingleton(appConfig.Phase2);
    services.AddSingleton(appConfig.SkillQuality);
    services.AddSingleton(appConfig.LeastPrivilege);

    // OpenTelemetry (task_013) — tracing + metrics
    services.AddHerculesOtel(appConfig.Otel);

    // LLM-слой (отказоустойчивый клиент с fallback + multi-role routing v2)
    services.AddSingleton<LlmClientFactory>(sp =>
        new LlmClientFactory(
            sp.GetRequiredService<LlmConfig>(),
            sp.GetRequiredService<ICacheService>()));
    services.AddSingleton<RoleRouter>();
    services.AddSingleton<IJsonRepairService, JsonRepairService>();
    services.AddSingleton<ResilientLLMClient>(sp =>
        new ResilientLLMClient(
            sp.GetRequiredService<LlmConfig>(),
            sp.GetRequiredService<LlmClientFactory>(),
            sp.GetRequiredService<RoleRouter>(),
            sp.GetRequiredService<ILogger<ResilientLLMClient>>()));
    services.AddSingleton<ILLMClient>(sp => sp.GetRequiredService<ResilientLLMClient>());
    services.AddSingleton<ProviderHealthChecker>();
    services.AddSingleton<ProviderCapabilityDetector>(sp =>
        new ProviderCapabilityDetector(
            sp.GetRequiredService<LlmConfig>(),
            sp.GetService<ILogger<ProviderCapabilityDetector>>(),
            sp.GetRequiredService<ICacheService>()));

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

    // Tool ecosystem (Stage 3, v2)
    services.AddSingleton<ITool, HttpTool>();
    services.AddSingleton<ITool, A2AClient>();
    services.AddSingleton<ITool, CodeExecutionTool>();
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
    services.AddSingleton<IEventBus, InMemoryEventBus>();
    services.AddSingleton<Bus>();

    // Phase 3: Inter-agent mesh
    services.AddMeshServices(appConfig.Mesh, appConfig.Storage.DataRoot);

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

    // Layered memory (task_011)
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
    services.AddScoped<IWorkingMemory, WorkingMemoryService>();
    services.AddSingleton<IDurableFactsStore, DurableFactsService>();
    services.AddSingleton<IEpisodicStore, EpisodicStore>();
    services.AddSingleton<LayerMetadataExtractor>();
    services.AddSingleton<LayeredMemoryManager>();

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
            sp.GetRequiredService<IMarketplaceSigningService>()));
    services.AddSingleton<AgentTemplateManager>();

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
            sp.GetRequiredService<ITraceSummarizer>()));

    // [task_028] Caching — unified cache service
    services.AddSingleton(appConfig.Cache);
    services.AddSingleton<ICacheService, CacheService>();

    services.AddSingleton<ReflectionEngine>();
    services.AddSingleton<AgentCore>();

    // Интерфейсы
    services.AddSingleton<ConsoleUI>();
    services.AddSingleton<TelegramBotInterface>();
});

using var host = builder.Build();

var appConfig = host.Services.GetRequiredService<AppConfig>();

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

// --- Выбор режима запуска ---
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
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