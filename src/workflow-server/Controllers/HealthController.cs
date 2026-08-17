using System.Diagnostics;
using Hercules.WorkflowServer.Storage;

namespace Hercules.WorkflowServer.Controllers;

/// <summary>
///     Health-check endpoint (task_104). Публичный, не требует clientId/secret
///     (обрабатывается в <c>ClientAuthMiddleware</c> через path-bypass).
///     Возвращает: статус, версию сервиса, uptime и состояние storage.
/// </summary>
public static class HealthController
{
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;
    private static readonly string Version =
        typeof(HealthController).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    public static void MapWorkflowServerHealth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/workflows/health", (SqliteWorkflowDefinitionStore store) =>
        {
            var uptime = DateTimeOffset.UtcNow - StartedAt;
            var healthy = store.IsHealthy();
            return Results.Ok(new
            {
                status = healthy ? "ok" : "degraded",
                service = "hercules-workflow-server",
                version = Version,
                uptimeSeconds = (long)uptime.TotalSeconds,
                storage = healthy ? "ok" : "unavailable",
            });
        }).WithName("WorkflowServerHealth")
          .AllowAnonymous();
    }
}
