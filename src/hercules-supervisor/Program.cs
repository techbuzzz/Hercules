using Hercules.Supervisor;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

// Hercules.Supervisor (ADR-0009)
// ---------------------------------------------------------------------------
// Studio is a browser SPA, so it cannot inspect the process table or kill
// anything. The agent has always documented its restart protocol as being owned
// by an external supervisor ("Studio / systemd / watcher") and never kills
// itself. This process is that owner:
//   * polls each agent's health endpoint and restarts it when it stays down;
//   * honours POST /api/system/restart issued by an operator, then clears it;
//   * publishes a small status API so the UI can show live PIDs instead of
//     scraping a process list.

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SupervisorOptions>(
    builder.Configuration.GetSection(SupervisorOptions.SectionName));

builder.Services.AddSingleton<SupervisorApiClient>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<SupervisorOptions>>().Value;
    var logger = sp.GetRequiredService<ILogger<SupervisorApiClient>>();
    // Do not keep sockets pooled across a restart. On Windows Kestrel binds with
    // SO_EXCLUSIVEADDRUSE, so a lingering ESTABLISHED connection *from this
    // process* to 8421 prevents the replacement agent from binding the same port
    // and it dies with "address already in use".
    var handler = new SupervisorApiClient.ApiKeyHandler(
        opts.ApiKey,
        new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.Zero,
            PooledConnectionIdleTimeout = TimeSpan.Zero,
        });

    var http = new HttpClient(handler)
    {
        BaseAddress = new Uri(opts.ApiBaseUrl.TrimEnd('/')),
        Timeout = TimeSpan.FromSeconds(10),
    };
    http.DefaultRequestHeaders.ConnectionClose = true;
    return new SupervisorApiClient(http, logger)
    {
        HasCredentials = !string.IsNullOrWhiteSpace(opts.ApiKey),
    };
});

builder.Services.AddSingleton<SupervisorService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SupervisorService>());

// Health client: ConnectionClose for the same reason as the API client — a pooled
// socket held by this process blocks the agent from re-binding its own port after
// a restart (Windows SO_EXCLUSIVEADDRUSE).
builder.Services.AddHttpClient("health", c =>
    {
        c.Timeout = TimeSpan.FromSeconds(5);
        c.DefaultRequestHeaders.ConnectionClose = true;
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromSeconds(5),
    });

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

var app = builder.Build();

// The supervisor binds its own status port, never the agent's 8421.
var opts = app.Services.GetRequiredService<IOptions<SupervisorOptions>>().Value;
app.Urls.Clear();
app.Urls.Add($"http://127.0.0.1:{opts.StatusPort}");

app.Services.GetRequiredService<SupervisorService>().Configure(
    app.Services.GetRequiredService<IHttpClientFactory>());

var service = app.Services.GetRequiredService<SupervisorService>();

// Replaces the process-table scan the Electron UI used to perform.
app.MapGet("/supervisor/agents", () =>
{
    var agents = service.Agents.Select(a => a.Snapshot).ToArray();
    return Results.Ok(new { count = agents.Length, agents });
});

app.MapGet("/supervisor/health", () => Results.Ok(new { status = "alive" }));

// Manual control, so an operator can act without the agent's restart flag.
app.MapPost("/supervisor/agents/{name}/restart", async (string name) =>
{
    var agent = service.Agents.FirstOrDefault(a =>
        string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
    if (agent is null) return Results.NotFound(new { error = $"unknown agent '{name}'" });
    var ok = await agent.RestartAsync("manual supervisor request", TimeSpan.Zero);
    return ok ? Results.Ok(agent.Snapshot) : Results.Conflict(new { error = "restart failed" });
});

app.MapPost("/supervisor/agents/{name}/stop", async (string name) =>
{
    var agent = service.Agents.FirstOrDefault(a =>
        string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
    if (agent is null) return Results.NotFound(new { error = $"unknown agent '{name}'" });
    await agent.StopAsync();
    return Results.Ok(agent.Snapshot);
});

app.MapPost("/supervisor/agents/{name}/start", async (string name) =>
{
    var agent = service.Agents.FirstOrDefault(a =>
        string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
    if (agent is null) return Results.NotFound(new { error = $"unknown agent '{name}'" });
    await agent.StartAsync();
    return Results.Ok(agent.Snapshot);
});

app.Lifetime.ApplicationStopping.Register(() =>
{
    // Never orphan child processes.
    service.StopAllAsync().GetAwaiter().GetResult();
});

app.Logger.LogInformation(
    "Hercules Supervisor starting — status on http://127.0.0.1:{Port}, {Count} agent(s) configured",
    opts.StatusPort, opts.Agents.Count);

app.Run();