using Hercules.Agent;
using Hercules.Skills.Quality;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Skill quality score endpoints (task_029).
/// </summary>
public static class SkillQualityController
{
    public static void MapSkillQuality(this IEndpointRouteBuilder app)
    {
        // GET /api/skills/{id}/quality — current metrics + composite score
        app.MapGet("/api/skills/{id}/quality",
            async (string id, ISkillQualityService service,
                WebApiAdapter adapter, CancellationToken ct) =>
            {
                var skill = adapter.GetSkill(id);
                if (skill is null)
                    return Results.NotFound(new { error = "Навык не найден." });

                var score = await service.ComputeScoreAsync(id, skill.Meta.Version, ct);
                return Results.Ok(score);
            }).WithName("GetSkillQuality");

        // GET /api/skills/{id}/quality/history — all versions history
        app.MapGet("/api/skills/{id}/quality/history",
            (string id, ISkillQualityService service) =>
            {
                return Results.Ok(service.GetHistoryAsync(id));
            }).WithName("GetSkillQualityHistory");

        // POST /api/skills/{id}/quality/record — record a quality event
        app.MapPost("/api/skills/{id}/quality/record",
            async (string id, RecordQualityEventRequest req,
                ISkillQualityService service, WebApiAdapter adapter,
                CancellationToken ct) =>
            {
                var skill = adapter.GetSkill(id);
                if (skill is null)
                    return Results.NotFound(new { error = "Навык не найден." });

                switch (req.Event?.ToLowerInvariant())
                {
                    case "fallback":
                        await service.RecordFallbackAsync(id, skill.Meta.Version, ct);
                        break;
                    case "usercorrection":
                        await service.RecordUserCorrectionAsync(id, skill.Meta.Version, ct);
                        break;
                    case "safetydenial":
                        await service.RecordSafetyDenialAsync(id, skill.Meta.Version, ct);
                        break;
                    case "latencysample":
                        await service.RecordLatencySampleAsync(
                            id, skill.Meta.Version, req.LatencyMs ?? 0, req.CostUsd ?? 0, ct);
                        break;
                    default:
                        return Results.BadRequest(new { error = $"Unknown event type: {req.Event}" });
                }

                return Results.Ok(new { recorded = true, skillId = id, skillVersion = skill.Meta.Version });
            }).WithName("RecordSkillQualityEvent");

        // GET /api/skills/{id}/quality/score — composite score only
        app.MapGet("/api/skills/{id}/quality/score",
            async (string id, ISkillQualityService service,
                WebApiAdapter adapter, CancellationToken ct) =>
            {
                var skill = adapter.GetSkill(id);
                if (skill is null)
                    return Results.NotFound(new { error = "Навык не найден." });

                var score = await service.ComputeScoreAsync(id, skill.Meta.Version, ct);
                return Results.Ok(new
                {
                    skillId = score.SkillId,
                    version = score.Version,
                    compositeScore = score.CompositeScore,
                    isReliable = score.IsReliable,
                    reason = score.Reason,
                });
            }).WithName("GetSkillQualityScore");
    }
}

/// <summary>
///     Request body для POST /api/skills/{id}/quality/record.
/// </summary>
public sealed class RecordQualityEventRequest
{
    /// <summary>Event type: fallback, usercorrection, safetydenial, latencysample.</summary>
    public string? Event { get; set; }

    /// <summary>Latency in milliseconds (for latencysample event).</summary>
    public int? LatencyMs { get; set; }

    /// <summary>Cost in USD (for latencysample event).</summary>
    public double? CostUsd { get; set; }
}
