using System.Text;
using System.Text.Encodings.Web;
using Hercules.Agent;
using Hercules.CodeExecution;
using Hercules.Config;
using Hercules.LLM;
using Hercules.Mesh;
using Hercules.Skills;
using Hercules.Storage;
using Hercules.Tools;
using Hercules.WasmSandbox;
using Hercules.WasmSandbox.Compilation;
using Hercules.WebApi.Auth;
using Hercules.WebApi.Config;
using Hercules.WebApi.Controllers;
using HerculesBus;
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
var runtimeConfigStore = new RuntimeConfigStore(appConfig, runtimeConfigFile);

// --- Регистрация сервисов ядра (как в консольном приложении) ---
builder.Services.AddSingleton(runtimeConfigStore);
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
builder.Services.AddSingleton(webCfg);

// LLM-слой (отказоустойчивый клиент с fallback + multi-role routing v2)
builder.Services.AddSingleton<LlmClientFactory>();
builder.Services.AddSingleton<RoleRouter>();
builder.Services.AddSingleton<ResilientLLMClient>(sp =>
    new ResilientLLMClient(
        sp.GetRequiredService<LlmConfig>(),
        sp.GetRequiredService<LlmClientFactory>(),
        sp.GetRequiredService<RoleRouter>()));
builder.Services.AddSingleton<ILLMClient>(sp => sp.GetRequiredService<ResilientLLMClient>());

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
builder.Services.AddSingleton<InMemoryChannelStore>();
builder.Services.AddSingleton<InMemoryAgentRegistry>();
builder.Services.AddSingleton<InMemoryEventBus>();
builder.Services.AddSingleton<Bus>();

// Phase 3: Inter-agent mesh (manifest, capability registry, intent routing, transport)
builder.Services.AddMeshServices(appConfig.Mesh, appConfig.Storage.DataRoot);

// Reactor подписывается на изменения конфигурации и перезагружает runtime-зависимости
builder.Services.AddHostedService<RuntimeConfigHostedService>();

// Хранилища
builder.Services.AddSingleton<FileSkillRepository>();
builder.Services.AddSingleton<MemoryStore>();
builder.Services.AddSingleton<SqliteSessionStore>();

// Phase 2: Skill packager (export/import .skillpkg)
builder.Services.AddSingleton<SkillPackager>();

// Phase 2: Semantic routing (embedding-based). Stub provider — offline.
builder.Services.AddSingleton<IEmbeddingProvider, StubEmbeddingProvider>();
builder.Services.AddSingleton<EmbeddingSkillRouter>();

// Phase 2: Skill marketplace + agent templates
builder.Services.AddSingleton<SkillMarketplace>();
builder.Services.AddSingleton<AgentTemplateManager>();

// Агент
builder.Services.AddSingleton<SkillManager>();
builder.Services.AddSingleton<SkillRouter>();
builder.Services.AddSingleton<MemoryManager>();
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
        "GET /api/memory/profile", "PUT /api/memory/profile", "POST /api/memory/reset",
        "GET /api/reflect", "GET /api/stats",
        "GET /api/config", "PUT /api/config", "PATCH /api/config",
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
app.MapMemory();
app.MapStats();
app.MapConfig();
app.MapMesh();

Console.WriteLine("🌐 Hercules Web API запущен на http://localhost:5000");
Console.WriteLine($"🔑 X-Api-Key: {(string.IsNullOrEmpty(webCfg.ApiKey) ? "(отключён)" : webCfg.ApiKey)}");
Console.WriteLine($"💾 Данные: {appConfig.Storage.DataRoot}");

app.Run();
