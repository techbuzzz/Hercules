using System.Text;
using System.Text.Encodings.Web;
using Hercules.Agent;
using Hercules.Audit;
using Hercules.Budget;
using Hercules.CodeExecution;
using Hercules.Config;
using Hercules.LLM;
using Hercules.LLM.JsonRepair;
using Hercules.Memory.Layers;
using Hercules.Mesh;
using Hercules.Observability;
using Hercules.Redaction;
using Hercules.Skills;
using Hercules.Skills.Eval;
using Hercules.Storage;
using Hercules.Tasks;
using Hercules.Telegram;
using Hercules.Tools;
using Hercules.Tools.Approval;
using Hercules.Tools.Policy;
using Hercules.WasmSandbox;
using Hercules.WasmSandbox.Compilation;
using Hercules.WebApi.Auth;
using Hercules.WebApi.Config;
using Hercules.WebApi.Controllers;
using HerculesBus;
using HerculesBus.Core;
using HerculesBus.InMemory;

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
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Approval);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Memory);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Budget);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Otel);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Audit);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Secrets);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Eval);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.SelfImprovement);
builder.Services.AddSingleton(sp => sp.GetRequiredService<RuntimeConfigStore>().Current.Tasks);

    // OpenTelemetry (task_013) — tracing + metrics
    builder.Services.AddHerculesOtel(appConfig.Otel);
builder.Services.AddSingleton(webCfg);

// LLM-слой (отказоустойчивый клиент с fallback + multi-role routing v2)
builder.Services.AddSingleton<LlmClientFactory>();
builder.Services.AddSingleton<RoleRouter>();
builder.Services.AddSingleton<IJsonRepairService, JsonRepairService>();
builder.Services.AddSingleton<ResilientLLMClient>(sp =>
    new ResilientLLMClient(
        sp.GetRequiredService<LlmConfig>(),
        sp.GetRequiredService<LlmClientFactory>(),
        sp.GetRequiredService<RoleRouter>(),
        sp.GetRequiredService<ILogger<ResilientLLMClient>>()));
builder.Services.AddSingleton<ILLMClient>(sp => sp.GetRequiredService<ResilientLLMClient>());
builder.Services.AddSingleton<ProviderHealthChecker>();
builder.Services.AddSingleton<ProviderCapabilityDetector>();

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
builder.Services.AddSingleton<ITool, HttpTool>();
builder.Services.AddSingleton<ITool, A2AClient>();
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
        sp.GetRequiredService<IAuditService>()));
builder.Services.AddSingleton<ToolRegistry>();
builder.Services.AddSingleton<McpClient>();

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
builder.Services.AddMeshServices(appConfig.Mesh, appConfig.Storage.DataRoot);

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

// Layered memory (task_011)
builder.Services.AddScoped<IWorkingMemory, WorkingMemoryService>();
builder.Services.AddSingleton<IDurableFactsStore, DurableFactsService>();
builder.Services.AddSingleton<IEpisodicStore, EpisodicStore>();
builder.Services.AddSingleton<LayerMetadataExtractor>();
builder.Services.AddSingleton<LayeredMemoryManager>();

// Phase 2: Skill packager (export/import .skillpkg)
builder.Services.AddSingleton<SkillPackager>(sp =>
    new SkillPackager(
        sp.GetRequiredService<FileSkillRepository>(),
        sp.GetRequiredService<SecretsConfig>(),
        sp.GetRequiredService<ISecretMaskingService>()));

// Phase 2: Semantic routing (embedding-based). Stub provider — offline.
builder.Services.AddSingleton<IEmbeddingProvider, StubEmbeddingProvider>();
builder.Services.AddSingleton<EmbeddingSkillRouter>();

// Phase 2: Skill marketplace + agent templates
builder.Services.AddSingleton<SkillMarketplace>();
builder.Services.AddSingleton<AgentTemplateManager>();

// Skill lifecycle: policy, deprecation, evaluation
builder.Services.AddSingleton<SkillLifecyclePolicy>();
builder.Services.AddSingleton<SkillDeprecationManager>();
builder.Services.AddSingleton<SkillEvaluationEngine>();
builder.Services.AddSingleton<SkillLifecycleService>();

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
builder.Services.AddSingleton<ReflectionEngine>();
builder.Services.AddSingleton<AgentCore>();
// Регистрируем сервисы, поддерживающие hot-reload конфигурации, как IConfigReload
// чтобы RuntimeConfigReactor мог прокидывать им новые настройки без перезагрузки.
builder.Services.AddSingleton<IConfigReload>(sp => sp.GetRequiredService<AgentCore>());
builder.Services.AddSingleton<IConfigReload>(sp => sp.GetRequiredService<SkillManager>());

// Адаптер Web API
builder.Services.AddSingleton<WebApiAdapter>();

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
app.UseMiddleware<RequestBodyLimitMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();
app.UseMiddleware<RateLimitMiddleware>();

// --- Инициализация сессии агента ---
app.Services.GetRequiredService<WebApiAdapter>().EnsureSessionStarted();

// --- Служебные эндпоинты ---
app.MapGet("/", () => Results.Ok(new
{
    name = "Hercules Web API",
    version = "1.0",
    endpoints = new[]
    {
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
        "GET /api/eval/baselines",
        "POST /api/maintenance/run", "POST /api/maintenance/run-all",
        "GET /api/maintenance/proposals", "GET /api/maintenance/proposals/{id}",
        "POST /api/maintenance/proposals/{id}/approve",
        "POST /api/maintenance/proposals/{id}/reject",
        "GET /agent.manifest.json", "GET /api/mesh/agents", "POST /api/mesh/agents/register",
        "GET /api/mesh/agents/{id}", "DELETE /api/mesh/agents/{id}",
        "GET /api/mesh/capabilities", "GET /api/mesh/capabilities/{name}",
        "GET /api/mesh/capabilities/search", "POST /api/mesh/intent"
    }
}));
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", time = DateTime.UtcNow }));

// --- Доменные эндпоинты ---
app.MapChat();
app.MapSkills();
app.MapSkillLifecycle();
app.MapMemory();
app.MapStats();
app.MapConfig();
app.MapMesh();
app.MapBudget();
app.MapAudit();
app.MapLlm();
app.MapApprovals();
app.MapObservability();
app.MapSkillHarness();
app.MapSelfImprovement();
app.MapTasks();

Console.WriteLine("🌐 Hercules Web API запущен на http://localhost:5000");
Console.WriteLine($"🔑 X-Api-Key: {(string.IsNullOrEmpty(webCfg.ApiKey) ? "(отключён)" : webCfg.ApiKey)}");
Console.WriteLine($"💾 Данные: {appConfig.Storage.DataRoot}");

app.Run();
