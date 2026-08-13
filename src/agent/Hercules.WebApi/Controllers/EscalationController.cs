using Hercules.Mesh.Escalation;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     API endpoints for human-in-the-loop escalation (task_049).
///     Allows listing, approving, denying, and batch-approving escalations.
/// </summary>
public static class EscalationController
{
    public static void MapEscalations(this IEndpointRouteBuilder app)
    {
        // GET /api/escalations/pending — list pending escalations
        app.MapGet("/api/escalations/pending", (
            IEscalationService svc,
            string? sessionId,
            string? minSeverity) =>
        {
            EscalationSeverity? minSev = null;
            if (!string.IsNullOrEmpty(minSeverity) &&
                Enum.TryParse<EscalationSeverity>(minSeverity, true, out var parsed))
            {
                minSev = parsed;
            }
            var pending = svc.GetPending(sessionId, minSev);
            return Results.Ok(new
            {
                count = pending.Count,
                escalations = pending.Select(ToDto).ToList()
            });
        }).WithName("ListPendingEscalations");

        // GET /api/escalations/{id} — get specific escalation
        app.MapGet("/api/escalations/{id}", (
            string id,
            IEscalationService svc) =>
        {
            var result = svc.Get(id);
            return result is null
                ? Results.NotFound(new { error = $"Escalation '{id}' not found." })
                : Results.Ok(ToDto(result));
        }).WithName("GetEscalation");

        // POST /api/escalations/{id}/approve — approve one escalation
        app.MapPost("/api/escalations/{id}/approve", async (
            string id,
            IEscalationService svc,
            string? resolvedBy,
            CancellationToken ct) =>
        {
            var ok = await svc.ApproveAsync(id, resolvedBy ?? "operator", ct);
            if (!ok)
                return Results.NotFound(new { error = $"Escalation '{id}' not found or already processed." });

            return Results.Ok(new
            {
                message = $"Escalation '{id}' approved.",
                id,
                status = "Approved"
            });
        }).WithName("ApproveEscalation");

        // POST /api/escalations/{id}/deny — deny one escalation
        app.MapPost("/api/escalations/{id}/deny", async (
            string id,
            IEscalationService svc,
            string? resolvedBy,
            CancellationToken ct) =>
        {
            var ok = await svc.DenyAsync(id, resolvedBy ?? "operator", ct);
            if (!ok)
                return Results.NotFound(new { error = $"Escalation '{id}' not found or already processed." });

            return Results.Ok(new
            {
                message = $"Escalation '{id}' denied.",
                id,
                status = "Denied"
            });
        }).WithName("DenyEscalation");

        // POST /api/escalations/batch-approve — batch approve multiple escalations
        app.MapPost("/api/escalations/batch-approve", async (
            BatchApproveRequest body,
            IEscalationService svc,
            string? resolvedBy,
            CancellationToken ct) =>
        {
            if (body.Ids is null || body.Ids.Count == 0)
                return Results.BadRequest(new { error = "No escalation IDs provided." });

            var approved = await svc.BatchApproveAsync(body.Ids, resolvedBy ?? "operator", ct);
            return Results.Ok(new
            {
                message = $"{approved}/{body.Ids.Count} escalations approved.",
                approved,
                total = body.Ids.Count
            });
        }).WithName("BatchApproveEscalations");
    }

    private static object ToDto(EscalationResult r) => new
    {
        escalationId = r.EscalationId,
        requestId = r.RequestId,
        sessionId = r.SessionId,
        type = r.Type.ToString(),
        severity = r.Severity.ToString(),
        status = r.Status.ToString(),
        actionPlan = r.ActionPlan,
        context = r.Context,
        payloadJson = r.PayloadJson,
        toolOrIntentName = r.ToolOrIntentName,
        requestedBy = r.RequestedBy,
        createdAt = r.CreatedAt,
        resolvedAt = r.ResolvedAt,
        resolvedBy = r.ResolvedBy
    };
}

/// <summary>Request body for batch approve.</summary>
public sealed record BatchApproveRequest(List<string>? Ids);
