using Hercules.Tools.Approval;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Эндпоинты управления approval gates (task_010).
///     Позволяет запрашивать, просматривать, одобрять и отклонять pending-подтверждения.
/// </summary>
public static class ApprovalController
{
    public static void MapApprovals(this IEndpointRouteBuilder app)
    {
        // GET /api/approvals/pending — список pending-запросов (опционально: ?sessionId=xxx)
        app.MapGet("/api/approvals/pending", (
            IApprovalService svc,
            string? sessionId) =>
        {
            var pending = svc.GetPending(sessionId);
            return Results.Ok(new
            {
                count = pending.Count,
                approvals = pending.Select(ToDto).ToList()
            });
        }).WithName("ListPendingApprovals");

        // GET /api/approvals/{id} — получить конкретный запрос
        app.MapGet("/api/approvals/{id}", (
            string id,
            IApprovalService svc) =>
        {
            var result = svc.Get(id);
            return result is null
                ? Results.NotFound(new { error = $"Approval request '{id}' not found." })
                : Results.Ok(ToDto(result));
        }).WithName("GetApproval");

        // POST /api/approvals/{id}/approve — одобрить запрос
        app.MapPost("/api/approvals/{id}/approve", async (
            string id,
            IApprovalService svc,
            CancellationToken ct) =>
        {
            var ok = await svc.ApproveAsync(id, ct);
            if (!ok)
            {
                return Results.NotFound(new { error = $"Approval request '{id}' not found or already processed." });
            }

            return Results.Ok(new
            {
                message = $"Approval request '{id}' has been approved.",
                id,
                status = "Approved"
            });
        }).WithName("ApproveTool");

        // POST /api/approvals/{id}/deny — отклонить запрос
        app.MapPost("/api/approvals/{id}/deny", async (
            string id,
            IApprovalService svc,
            CancellationToken ct) =>
        {
            var ok = await svc.DenyAsync(id, ct);
            if (!ok)
            {
                return Results.NotFound(new { error = $"Approval request '{id}' not found or already processed." });
            }

            return Results.Ok(new
            {
                message = $"Approval request '{id}' has been denied.",
                id,
                status = "Denied"
            });
        }).WithName("DenyTool");

        // GET /api/approvals — все запросы (last 100)
        app.MapGet("/api/approvals", (
            IApprovalService svc,
            string? sessionId) =>
        {
            var all = svc.GetPending(sessionId);
            return Results.Ok(new
            {
                count = all.Count,
                approvals = all.Select(ToDto).ToList()
            });
        }).WithName("ListAllApprovals");
    }

    private static object ToDto(ApprovalResult r) => new
    {
        requestId = r.RequestId,
        sessionId = r.SessionId,
        toolName = r.ToolName,
        argumentsJson = r.ArgumentsJson,
        reason = r.Reason,
        requestedAt = r.RequestedAt,
        status = r.Status.ToString()
    };
}
