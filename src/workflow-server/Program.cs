using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
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
// R33: the previous AddTransient<ClientAuthMiddleware> factory built a `next` delegate
// whose two ternary branches were both no-ops. It was dead code: UseMiddleware<T>
// constructs its own instance and injects the real pipeline `next`, so editing that
// factory had no effect while looking like the wiring point. Wiring is now the single
// `app.UseMiddleware<ClientAuthMiddleware>()` below.
builder.Services.AddSingleton<ClientCredentialStore>();
builder.Services.AddTransient<ClientAuthMiddleware>();

// --- Storage: SQLite ---
builder.Services.AddSingleton<SqliteWorkflowDefinitionStore>(sp =>
{
    var config = sp.GetRequiredService<WorkflowServerConfig>();
    return new SqliteWorkflowDefinitionStore(config.DataRoot);
});
builder.Services.AddSingleton<IWorkflowDefinitionStore>(sp =>
    sp.GetRequiredService<SqliteWorkflowDefinitionStore>());

// --- R16: parity with the agent's WebApi surface ---
// None of AddProblemDetails / UseExceptionHandler / AddRateLimiter / AddCors existed here,
// so unhandled exceptions surfaced raw and the client-credential check had no brute-force
// throttling. The agent side has all of these; this is asymmetry, not a design choice.
builder.Services.AddProblemDetails();
builder.Services.AddRateLimiter(options =>
{
    // Tight bucket on the auth path: the credential comparison is the only thing standing
    // between an anonymous caller and the whole API. Health is excluded so orchestrators
    // are never throttled into reporting a false outage.
    //
    // A GlobalLimiter with a path partition keeps this in one place; no controller has to
    // remember to opt in.
    var authLimit = cfg.Auth.RateLimitPerMinute > 0 ? cfg.Auth.RateLimitPerMinute : 60;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var path = context.Request.Path;

        if (path.StartsWithSegments("/api/workflows/health"))
        {
            return RateLimitPartition.GetNoLimiter("health");
        }

        var remote = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var isWebhook = path.StartsWithSegments("/api/workflows/triggers/webhook");
        var partition = isWebhook ? $"webhook:{remote}" : $"api:{remote}";

        return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
        {
            // Webhook is pre-auth (token in URL), so it gets a wider but still bounded bucket.
            PermitLimit = isWebhook ? authLimit * 10 : authLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Too many requests. Retry after 60 seconds." }, ct);
    };
});

// R16: CORS. ClientAuthMiddleware exempts OPTIONS "for CORS preflight", which was a
// promise the server never kept — preflight returned 405. Studio (Electron) calls this
// API cross-origin, so a real policy is required rather than removing the exemption.
const string CorsPolicy = "workflow-server";
builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
{
    var origins = cfg.Cors.AllowedOrigins;
    if (origins.Count == 0)
    {
        // Fail closed: no configured origins means browser clients cannot call the API.
        policy.WithOrigins(Array.Empty<string>());
    }
    else
    {
        policy.WithOrigins([.. origins]);
    }

    policy.AllowAnyHeader().AllowAnyMethod();
}));

// --- Kestrel: фиксируем порт из конфига (по умолчанию 8430) ---
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(cfg.Port);
});

var app = builder.Build();

// --- Middleware pipeline (R16) ---
// Rate limiting and CORS must run before ClientAuthMiddleware so an unauthenticated
// flood is throttled rather than reaching the credential comparison.
app.UseRateLimiter();
app.UseCors(CorsPolicy);

// Exception handling: converts unhandled failures into RFC 7807 responses instead of
// the raw developer exception page.
app.UseExceptionHandler();

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
