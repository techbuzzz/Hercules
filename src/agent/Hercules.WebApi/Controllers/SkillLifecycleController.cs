using Hercules.Agent;
using Hercules.Skills;
using Hercules.Storage;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Эндпоинты жизненного цикла навыков: оценка, депрекация, откат.
///     Использует SkillLifecycleService.
/// </summary>
public static class SkillLifecycleController
{
    public static void MapSkillLifecycle(this IEndpointRouteBuilder app)
    {
        // POST /api/skills/{id}/evaluate — запустить оценку навыка
        app.MapPost("/api/skills/{id}/evaluate", async (
            string id,
            SkillLifecycleService lifecycle,
            CancellationToken ct) =>
        {
            try
            {
                var result = await lifecycle.EvaluateAsync(id, ct);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Оценка не удалась: {ex.Message}");
            }
        }).WithName("EvaluateSkill");

        // POST /api/skills/{id}/deprecate — пометить навык deprecated
        app.MapPost("/api/skills/{id}/deprecate", async (
            string id,
            DeprecateRequest req,
            SkillLifecycleService lifecycle,
            CancellationToken ct) =>
        {
            try
            {
                var skill = lifecycle.Deprecate(id, req.Reason);
                return skill is null
                    ? Results.NotFound(new { error = "Навык не найден." })
                    : Results.Ok(new { message = "Навык помечен deprecated.", skill = ToDto(skill) });
            }
            catch (ApprovalRequiredException ex)
            {
                return Results.Accepted($"/api/approvals/pending", new
                {
                    approvalRequired = true,
                    action = ex.Action.ToString(),
                    risk = ex.Risk.ToString(),
                    reason = ex.Message
                });
            }
        }).WithName("DeprecateSkill");

        // POST /api/skills/{id}/rollback — откатить к предыдущей версии
        app.MapPost("/api/skills/{id}/rollback", (
            string id,
            SkillLifecycleService lifecycle) =>
        {
            try
            {
                var skill = lifecycle.Rollback(id);
                return skill is null
                    ? Results.NotFound(new { error = "Навык не найден или откат невозможен (версия 1)." })
                    : Results.Ok(new { message = "Навык откащен.", skill = ToDto(skill) });
            }
            catch (ApprovalRequiredException ex)
            {
                return Results.Accepted($"/api/approvals/pending", new
                {
                    approvalRequired = true,
                    action = ex.Action.ToString(),
                    risk = ex.Risk.ToString(),
                    reason = ex.Message
                });
            }
        }).WithName("RollbackSkill");

        // POST /api/skills/{id}/undeprecate — снять deprecated-статус
        app.MapPost("/api/skills/{id}/undeprecate", (
            string id,
            SkillLifecycleService lifecycle) =>
        {
            var skill = lifecycle.Undeprecate(id);
            return skill is null
                ? Results.NotFound(new { error = "Навык не найден." })
                : Results.Ok(new { message = "Deprecated-статус снят.", skill = ToDto(skill) });
        }).WithName("UndeprecateSkill");

        // GET /api/skills/deprecated — список deprecated-навыков
        app.MapGet("/api/skills/deprecated", (
            SkillLifecycleService lifecycle) =>
        {
            var skills = lifecycle.GetDeprecated().Select(ToDto).ToList();
            return Results.Ok(new { count = skills.Count, skills });
        }).WithName("ListDeprecatedSkills");

        // POST /api/skills/{id}/improve — улучшение навыка; проверяет policy
        app.MapPost("/api/skills/{id}/improve", async (
            string id,
            SkillLifecycleService lifecycle,
            WebApiAdapter adapter,
            CancellationToken ct) =>
        {
            try
            {
                // Проверяем policy — нужен ли approval
                var policy = lifecycle.CheckImprovePolicy(id);
                if (policy.NeedsHumanApproval)
                {
                    return Results.Accepted($"/api/approvals/pending", new
                    {
                        approvalRequired = true,
                        action = "Improve",
                        risk = policy.Risk.ToString(),
                        reason = policy.Reason
                    });
                }

                var skill = await adapter.ImproveSkillAsync(id, ct);
                return skill is null
                    ? Results.NotFound(new { error = "Навык не найден." })
                    : Results.Ok(skill);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Improve failed: {ex.Message}");
            }
        }).WithName("ImproveSkillLifecycle");
    }

    private static SkillDto ToDto(Skill s) =>
        new(
            s.Meta.Id, s.Meta.Name, s.Meta.Description, s.Meta.PhraseReceivers,
            s.Meta.Version, s.Meta.SuccessRate, s.Meta.TotalUses, s.Meta.CreatedAt,
            s.Meta.DeprecatedAt, s.Meta.DeprecationReason, s.Meta.LastEvaluationScore);
}

/// <summary>Запрос на депрекацию навыка.</summary>
public sealed record DeprecateRequest(string Reason);
