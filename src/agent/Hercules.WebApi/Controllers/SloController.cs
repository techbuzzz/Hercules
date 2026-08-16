using Hercules.Slo;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Operational SLO endpoints (task_064).
///     Availability, response-time, data-loss, recovery-time, cost tracking and reporting.
///     task_077: handlers are async — no sync-over-async at the boundary.
/// </summary>
public static class SloController
{
    public static void MapSlos(this IEndpointRouteBuilder app)
    {
        // task_081: дорогие вычисления → concurrency limiter.
        // GET /api/slos — сводка по SLO-статусу всех вертикалей.
        app.MapGet("/api/slos", async (ISloService slo, CancellationToken ct) =>
        {
            var summary = await slo.GetSummaryAsync(ct);
            return Results.Ok(summary);
        }).WithName("SloSummary").RequireRateLimiting(RateLimitPolicies.Expensive);

        // GET /api/slos/{vertical}/definition — SLO-определение для вертикали.
        app.MapGet("/api/slos/{vertical}/definition", (string vertical, ISloService slo) =>
        {
            var def = slo.GetDefinition(vertical);
            if (def == null)
            {
                return Results.NotFound(new { error = $"No SLO definition found for vertical: {vertical}" });
            }

            return Results.Ok(def);
        }).WithName("SloDefinition").RequireRateLimiting(RateLimitPolicies.Expensive);

        // GET /api/slos/{vertical} — текущий SLO-статус для вертикали.
        app.MapGet("/api/slos/{vertical}", async (string vertical, ISloService slo, CancellationToken ct) =>
        {
            var status = await slo.GetStatusAsync(vertical, ct);
            return Results.Ok(status);
        }).WithName("SloStatus").RequireRateLimiting(RateLimitPolicies.Expensive);

        // GET /api/slos/{vertical}/report — полный SLO-отчёт по вертикали.
        app.MapGet("/api/slos/{vertical}/report", async (string vertical, ISloService slo, CancellationToken ct) =>
        {
            var report = await slo.GetReportAsync(vertical, ct);
            return Results.Ok(report);
        }).WithName("SloReport").RequireRateLimiting(RateLimitPolicies.Expensive);

        // POST /api/slos/{vertical}/ack/{violationId}?acknowledgedBy=xxx —
        // подтвердить конкретное нарушение (подавляет повторные алерты).
        app.MapPost("/api/slos/{vertical}/ack/{violationId}", (string vertical, string violationId, string? acknowledgedBy, ISloService slo) =>
        {
            var who = acknowledgedBy ?? "operator";
            slo.AcknowledgeViolation(vertical, violationId, who);
            return Results.Ok(new { acknowledged = true, violationId, by = who });
        }).WithName("SloAcknowledgeViolation").RequireRateLimiting(RateLimitPolicies.Expensive);

        // POST /api/slos/{vertical}/ack?acknowledgedBy=xxx —
        // подтвердить все активные нарушения для вертикали.
        app.MapPost("/api/slos/{vertical}/ack", (string vertical, string? acknowledgedBy, ISloService slo) =>
        {
            var who = acknowledgedBy ?? "operator";
            slo.AcknowledgeAll(vertical, who);
            return Results.Ok(new { acknowledgedAll = true, vertical, by = who });
        }).WithName("SloAcknowledgeAll").RequireRateLimiting(RateLimitPolicies.Expensive);
    }
}
