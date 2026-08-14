using Hercules.Reflection;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Self-improvement endpoints (task_017): maintenance workflow, proposals, approve/reject.
/// </summary>
public static class SelfImprovementController
{
    public static void MapSelfImprovement(this IEndpointRouteBuilder app)
    {
        // POST /api/maintenance/run — запустить maintenance workflow для конкретного навыка
        app.MapPost("/api/maintenance/run",
            async (RunMaintenanceRequest? req, SelfImprovementService service, CancellationToken ct) =>
            {
                if (req is null || string.IsNullOrWhiteSpace(req.SkillId))
                {
                    return Results.BadRequest(new { error = "SkillId is required" });
                }

                var proposal = await service.RunMaintenanceAsync(
                    req.SkillId,
                    req.TriggeredBy ?? "user",
                    ct);

                if (proposal is null)
                {
                    return Results.Ok(new { message = "No improvement needed or workflow disabled", skillId = req.SkillId });
                }

                return Results.Created($"/api/maintenance/proposals/{proposal.Id}", proposal);
            }).WithName("RunMaintenance");

        // POST /api/maintenance/run-all — запустить для всех навыков
        app.MapPost("/api/maintenance/run-all",
            async (RunMaintenanceRequest? req, SelfImprovementService service, CancellationToken ct) =>
            {
                var proposals = await service.RunMaintenanceAllAsync(req?.TriggeredBy ?? "user", ct);
                return Results.Ok(new { count = proposals.Count, proposals });
            }).WithName("RunMaintenanceAll");

        // GET /api/maintenance/proposals — список proposals
        app.MapGet("/api/maintenance/proposals",
            (int limit, string? skillId, SelfImprovementService service) =>
            {
                List<Proposal> proposals;
                if (!string.IsNullOrEmpty(skillId))
                {
                    proposals = service.GetProposalsForSkill(skillId);
                }
                else
                {
                    proposals = service.GetProposals(limit);
                }
                return Results.Ok(new { count = proposals.Count, proposals });
            }).WithName("ListProposals");

        // GET /api/maintenance/proposals/{id} — один proposal
        app.MapGet("/api/maintenance/proposals/{id}",
            (string id, SelfImprovementService service) =>
            {
                var proposal = service.GetProposal(id);
                return proposal is null
                    ? Results.NotFound(new { error = "Proposal not found" })
                    : Results.Ok(proposal);
            }).WithName("GetProposal");

        // POST /api/maintenance/proposals/{id}/approve — применить proposal
        app.MapPost("/api/maintenance/proposals/{id}/approve",
            async (string id, ApproveRequest? req, SelfImprovementService service, CancellationToken ct) =>
            {
                var result = await service.ApplyProposalAsync(id, req?.ApprovedBy ?? "user", ct);

                if (!result.Success)
                {
                    return Results.BadRequest(new
                    {
                        error = result.Error,
                        hasRegression = result.HasRegression,
                        evalResult = result.EvalResult
                    });
                }

                return Results.Ok(new
                {
                    success = true,
                    appliedVersion = result.AppliedVersion,
                    newScore = result.NewScore,
                    scoreGain = result.ScoreGain,
                    evalResult = result.EvalResult
                });
            }).WithName("ApproveProposal");

        // POST /api/maintenance/proposals/{id}/reject — отклонить proposal
        app.MapPost("/api/maintenance/proposals/{id}/reject",
            (string id, RejectRequest? req, SelfImprovementService service) =>
            {
                var ok = service.RejectProposal(id, req?.RejectedBy ?? "user", req?.Reason);
                return ok
                    ? Results.Ok(new { success = true, proposalId = id })
                    : Results.BadRequest(new { error = "Proposal not found or already resolved" });
            }).WithName("RejectProposal");
    }
}

public sealed class RunMaintenanceRequest
{
    public string SkillId { get; set; } = "";
    public string? TriggeredBy { get; set; }
}

public sealed class ApproveRequest
{
    public string? ApprovedBy { get; set; }
}

public sealed class RejectRequest
{
    public string? RejectedBy { get; set; }
    public string? Reason { get; set; }
}
