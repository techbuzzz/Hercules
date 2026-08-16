using Hercules.Agent;
using Hercules.Storage;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Эндпоинты статистики и рефлексии.
/// </summary>
public static class StatsController
{
    public static void MapStats(this IEndpointRouteBuilder app)
    {
        // GET /api/stats — использование: skill vs direct, success_rate, по дням
        app.MapGet("/api/stats", (WebApiAdapter adapter) =>
                Results.Ok(adapter.GetStats()))
            .WithName("Stats");

        // GET /api/reflect — запустить рефлексию вручную
        app.MapGet("/api/reflect", async (WebApiAdapter adapter, CancellationToken ct) =>
                Results.Ok(await adapter.ReflectAsync(ct)))
            .WithName("Reflect")
            .RequireRateLimiting(RateLimitPolicies.Expensive);

        // GET /api/health/detailed — SQLite health check
        app.MapGet("/api/health/detailed", (SqliteSessionStore sessions) =>
                Results.Ok(new { sqlite = new { healthy = sessions.IsHealthy() } }))
            .WithName("HealthDetailed");
    }
}
