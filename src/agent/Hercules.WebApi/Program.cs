using Hercules.Agent;
using Hercules.Config;
using Hercules.LLM;
using Hercules.Storage;
using Hercules.WebApi.Auth;
using Hercules.WebApi.Config;
using Hercules.WebApi.Controllers;

// ============================================================================
//  Hercules Web API — ASP.NET Core Minimal API поверх ядра агента.
//  Предоставляет HTTP-доступ к чату, навыкам, памяти, рефлексии и статистике.
//  Запуск: dotnet run --project Hercules.WebApi   (порт 5000)
// ============================================================================

var builder = WebApplication.CreateBuilder(args);

// --- Кодировка консоли для кириллицы ---
Console.OutputEncoding = System.Text.Encoding.UTF8;

// --- Конфигурация (наследует appsettings.json + переменные окружения HERCULES_) ---
builder.Configuration.AddEnvironmentVariables(prefix: "HERCULES_");

var appConfig = builder.Configuration.Get<AppConfig>() ?? new AppConfig();
var webCfg = builder.Configuration.GetSection("WebApi").Get<WebApiConfig>() ?? new WebApiConfig();

// Делаем хранилище общим с CLI-приложением: проект Hercules лежит на уровень выше.
var sharedData = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "data"));
if (Directory.Exists(Path.GetDirectoryName(sharedData)!))
    appConfig.Storage.DataRoot = sharedData;

// --- Регистрация сервисов ядра (как в консольном приложении) ---
builder.Services.AddSingleton(appConfig);
builder.Services.AddSingleton(appConfig.Llm);
builder.Services.AddSingleton(appConfig.Storage);
builder.Services.AddSingleton(appConfig.Agent);
builder.Services.AddSingleton(appConfig.Telegram);
builder.Services.AddSingleton(webCfg);

// LLM-слой
builder.Services.AddSingleton<LlmClientFactory>();
builder.Services.AddSingleton<ILLMClient>(sp =>
    new ResilientLLMClient(sp.GetRequiredService<LlmConfig>(), sp.GetRequiredService<LlmClientFactory>()));

// Хранилища
builder.Services.AddSingleton<FileSkillRepository>();
builder.Services.AddSingleton<MemoryStore>();
builder.Services.AddSingleton<SqliteSessionStore>();

// Агент
builder.Services.AddSingleton<SkillManager>();
builder.Services.AddSingleton<SkillRouter>();
builder.Services.AddSingleton<MemoryManager>();
builder.Services.AddSingleton<ReflectionEngine>();
builder.Services.AddSingleton<AgentCore>();

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
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
});

// Порт по умолчанию — 5000 (если не переопределён через --urls / ASPNETCORE_URLS)
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")) && !args.Any(a => a.StartsWith("--urls")))
{
    builder.WebHost.UseUrls("http://0.0.0.0:5000");
}

var app = builder.Build();

// --- Middleware ---
app.UseCors(corsPolicy);
app.UseMiddleware<ApiKeyMiddleware>();

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
        "GET /api/memory/profile", "PUT /api/memory/profile", "POST /api/memory/reset",
        "GET /api/reflect", "GET /api/stats"
    }
}));
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", time = DateTime.UtcNow }));

// --- Доменные эндпоинты ---
app.MapChat();
app.MapSkills();
app.MapMemory();
app.MapStats();

Console.WriteLine("🌐 Hercules Web API запущен на http://localhost:5000");
Console.WriteLine($"🔑 X-Api-Key: {(string.IsNullOrEmpty(webCfg.ApiKey) ? "(отключён)" : webCfg.ApiKey)}");
Console.WriteLine($"💾 Данные: {appConfig.Storage.DataRoot}");

app.Run();
