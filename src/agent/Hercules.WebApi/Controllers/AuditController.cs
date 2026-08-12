using Hercules.Storage;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Эндпоинты аудит-лога.
/// </summary>
public static class AuditController
{
    public static void MapAudit(this IEndpointRouteBuilder app)
    {
        // GET /api/audit — последние N записей
        app.MapGet("/api/audit", async (IAuditLog log, int limit = 100, CancellationToken ct = default) =>
        {
            var entries = await log.GetRecentAsync(limit, ct);
            return Results.Ok(new
            {
                count = entries.Count,
                entries = entries.Select(e => new
                {
                    e.Id,
                    e.Actor,
                    e.Action,
                    e.Target,
                    e.Details,
                    e.SessionId,
                    e.CreatedAt
                })
            });
        }).WithName("AuditLog");

        // GET /api/audit/{target} — записи по target
        app.MapGet("/api/audit/{target}", async (IAuditLog log, string target, int limit = 50, CancellationToken ct = default) =>
        {
            var entries = await log.GetByTargetAsync(target, limit, ct);
            return Results.Ok(new
            {
                target,
                count = entries.Count,
                entries = entries.Select(e => new
                {
                    e.Id,
                    e.Actor,
                    e.Action,
                    e.Target,
                    e.Details,
                    e.SessionId,
                    e.CreatedAt
                })
            });
        }).WithName("AuditLogByTarget");
    }
}
