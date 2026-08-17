using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hercules.WorkflowServer.Auth;
using Hercules.WorkflowServer.Config;
using Hercules.WorkflowServer.Controllers;
using Hercules.WorkflowServer.Storage;
using Microsoft.AspNetCore.Http.Json;

// ============================================================================
//  Hercules Workflow Server — отдельный ASP.NET Core minimal API для durable
//  workflow execution (task_104, ADR-0008). Порт 8430.
//  Запуск: dotnet run --project src/workflow-server
// ============================================================================

var builder = WebApplication.CreateBuilder(args);

// --- Кодировка консоли для кириллицы ---
Console.OutputEncoding = Encoding.UTF8;

// --- JSON-настройки ---
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

// --- Конфигурация сервиса (секция "WorkflowServer" в appsettings.json) ---
var cfg = new WorkflowServerConfig();
builder.Configuration.GetSection("WorkflowServer").Bind(cfg);
builder.Services.AddSingleton(cfg);

// --- Auth: clientId/secret (auto-generated на первом старте, ADR-0008) ---
builder.Services.AddSingleton<ClientCredentialStore>();
builder.Services.AddTransient<ClientAuthMiddleware>(sp =>
{
    var next = sp.GetRequiredService<IConfiguration>() is not null
        ? (RequestDelegate)(_ => Task.CompletedTask)
        : (_ => Task.CompletedTask);
    // Конструктор с store + cfg заполняет expected bytes.
    return new ClientAuthMiddleware(
        next,
        sp.GetRequiredService<ClientCredentialStore>(),
        sp.GetRequiredService<WorkflowServerConfig>(),
        sp.GetRequiredService<ILogger<ClientAuthMiddleware>>());
});

// --- Storage: SQLite ---
builder.Services.AddSingleton<SqliteWorkflowDefinitionStore>(sp =>
{
    var config = sp.GetRequiredService<WorkflowServerConfig>();
    return new SqliteWorkflowDefinitionStore(config.DataRoot);
});
builder.Services.AddSingleton<IWorkflowDefinitionStore>(sp =>
    sp.GetRequiredService<SqliteWorkflowDefinitionStore>());

// --- Kestrel: фиксируем порт из конфига (по умолчанию 8430) ---
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(cfg.Port);
});

var app = builder.Build();

// --- Auth pipeline: ClientAuthMiddleware первый ---
app.UseMiddleware<ClientAuthMiddleware>();

// --- Маршруты ---
app.MapWorkflows();
app.MapTriggers();
app.MapWorkflowServerHealth();

app.Logger.LogInformation("Hercules Workflow Server started on port {Port}", cfg.Port);
app.Logger.LogInformation("DataRoot: {DataRoot}", cfg.DataRoot);

app.Run();

/// <summary>Маркерный тип для тестов (WebApplicationFactory&lt;Program&gt;).</summary>
public partial class Program;
