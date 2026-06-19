using Hercules.Agent;

namespace Hercules.WebApi.Controllers;

/// <summary>
/// Эндпоинты навыков: список, создание, чтение, обновление, улучшение.
/// Делегируют в SkillManager через WebApiAdapter.
/// </summary>
public static class SkillsController
{
    public static void MapSkills(this IEndpointRouteBuilder app)
    {
        // GET /api/skills — список навыков
        app.MapGet("/api/skills", (WebApiAdapter adapter) =>
            Results.Ok(adapter.ListSkills()))
            .WithName("ListSkills");

        // GET /api/skills/{id} — получить навык
        app.MapGet("/api/skills/{id}", (string id, WebApiAdapter adapter) =>
        {
            var s = adapter.GetSkill(id);
            return s is null ? Results.NotFound(new { error = "Навык не найден." }) : Results.Ok(s);
        }).WithName("GetSkill");

        // POST /api/skills — создать навык.
        // По умолчанию создаётся вручную (trigger + prompt).
        // ?ai=true&topic=... — сгенерировать навык через LLM по теме.
        app.MapPost("/api/skills", async (CreateSkillRequest req, bool? ai, string? topic,
            WebApiAdapter adapter, CancellationToken ct) =>
        {
            try
            {
                if (ai == true)
                {
                    var t = !string.IsNullOrWhiteSpace(topic) ? topic
                          : !string.IsNullOrWhiteSpace(req.Name) ? req.Name
                          : req.Trigger ?? "";
                    if (string.IsNullOrWhiteSpace(t))
                    {
                        return Results.BadRequest(new { error = "Для AI-генерации укажите topic/name." });
                    }

                    var aiSkill = await adapter.CreateSkillWithLlmAsync(t, ct);
                    return Results.Created($"/api/skills/{aiSkill.Id}", aiSkill);
                }

                var skill = adapter.CreateSkill(req);
                return Results.Created($"/api/skills/{skill.Id}", skill);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).WithName("CreateSkill");

        // PUT /api/skills/{id} — обновить навык (новая версия)
        app.MapPut("/api/skills/{id}", (string id, UpdateSkillRequest req, WebApiAdapter adapter) =>
        {
            var updated = adapter.UpdateSkill(id, req);
            return updated is null ? Results.NotFound(new { error = "Навык не найден." }) : Results.Ok(updated);
        }).WithName("UpdateSkill");

        // POST /api/skills/{id}/improve — улучшить навык через LLM
        app.MapPost("/api/skills/{id}/improve", async (string id, WebApiAdapter adapter, CancellationToken ct) =>
        {
            var improved = await adapter.ImproveSkillAsync(id, ct);
            return improved is null ? Results.NotFound(new { error = "Навык не найден." }) : Results.Ok(improved);
        }).WithName("ImproveSkill");
    }
}
