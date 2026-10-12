using Hercules.Agent;
using Hercules.Skills;
using Hercules.Storage;
using Hercules.WebApi.Contracts;
using Hercules.WebApi.Skills;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Эндпоинты навыков: список, создание, чтение, обновление, улучшение, экспорт, импорт.
///     Делегируют в SkillManager через WebApiAdapter.
/// </summary>
public static class SkillsController
{
    public static void MapSkills(this IEndpointRouteBuilder app)
    {
        // GET /api/skills — список навыков
        // task_081: short-TTL output cache (5 min) для read-only skill listings.
        app.MapGet("/api/skills", (WebApiAdapter adapter) =>
                Results.Ok(adapter.ListSkills()))
            .WithName("ListSkills")
            .CacheOutput(OutputCachePolicies.Skills).WithTags("Skills")
            // ListSkills() already returns a named type, but the minimal-API
            // analyzer did not infer it — declaring it keeps Studio's generated
            // client in step with the agent.
            .Produces<List<SkillDto>>(200);

        // GET /api/skills/{id} — получить навык
        app.MapGet("/api/skills/{id}", (string id, WebApiAdapter adapter) =>
        {
            var s = adapter.GetSkill(id);
            return s is null
                ? Results.NotFound(new { error = "Навык не найден." })
                : Results.Ok(s);
        }).WithName("GetSkill")
          .CacheOutput(OutputCachePolicies.Skills).WithTags("Skills")
          .Produces<SkillDetailDto>(200);

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
        }).WithName("CreateSkill").WithTags("Skills");

        // PUT /api/skills/{id} — обновить навык (новая версия)
        app.MapPut("/api/skills/{id}", async (
            string id,
            UpdateSkillRequest req,
            WebApiAdapter adapter,
            SkillPromptHistoryStore history) =>
        {
            var updated = adapter.UpdateSkill(id, req);
            if (updated is null) return Results.NotFound(new { error = "Навык не найден." });

            // Stage 2: keep the prompt we are about to overwrite, otherwise an
            // edit is destructive and there is nothing left to diff against.
            if (req.Prompt is not null)
            {
                await history.RecordAsync(id, updated.Version, req.Prompt, "manual");
            }

            return Results.Ok(updated);
        })
          .WithName("UpdateSkill")
          .WithTags("Skills")
          .Produces<SkillDetailDto>(200);

        // POST /api/skills/{id}/improve — улучшить навык через LLM
        app.MapPost("/api/skills/{id}/improve", async (
            string id, WebApiAdapter adapter, SkillPromptHistoryStore history, CancellationToken ct) =>
        {
            var improved = await adapter.ImproveSkillAsync(id, ct);
            if (improved is null) return Results.NotFound(new { error = "Навык не найден." });

            var full = adapter.GetSkill(id);
            if (full?.Prompt is { } prompt)
            {
                await history.RecordAsync(id, improved.Version, prompt, "llm-improve");
            }

            return Results.Ok(improved);
        }).WithName("ImproveSkill").WithTags("Skills");

        // GET /api/skills/{id}/prompt-history — предыдущие версии prompt (Stage 2).
        app.MapGet("/api/skills/{id}/prompt-history", (
            string id, SkillPromptHistoryStore history) =>
        {
            var revisions = history.Get(id);
            return Results.Ok(new SkillPromptHistoryResponseDto
            {
                SkillId = id,
                Count = revisions.Count,
                Revisions = revisions
                    .Select(r => new SkillPromptRevisionDto
                    {
                        Version = r.Version,
                        Prompt = r.Prompt,
                        ChangedAt = r.ChangedAt,
                        Source = r.Source,
                        Author = r.Author,
                    })
                    .ToList()
            });
        })
          .WithName("GetSkillPromptHistory")
          .WithTags("Skills")
          .Produces<SkillPromptHistoryResponseDto>(200);

        // GET /api/skills/{id}/export — экспортировать навык в .skillpkg (ZIP)
        // Возвращает файл application/octet-stream.
        app.MapGet("/api/skills/{id}/export", (string id, SkillPackager packager) =>
        {
            try
            {
                var path = packager.Export(id);
                var bytes = File.ReadAllBytes(path);
                var fileName = Path.GetFileName(path);
                return Results.File(bytes, "application/octet-stream", fileName);
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        }).WithName("ExportSkill").WithTags("Skills");

        // POST /api/skills/import — импортировать навык из загруженного .skillpkg файла.
        // Параметр conflict = "replace" | "skip" | "rename" (default: rename).
        app.MapPost("/api/skills/import", async (HttpContext ctx, SkillPackager packager, CancellationToken ct) =>
        {
            var form = await ctx.Request.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "Файл не загружен. Используйте multipart/form-data с полем file." });
            }

            var conflictStr = form["conflict"].ToString();
            var conflict = Enum.TryParse<ConflictResolution>(conflictStr, true, out var cr)
                ? cr
                : ConflictResolution.Rename;

            // Сохраняем во временный файл
            var tempPath = Path.Combine(Path.GetTempPath(), $"hercules-import-{Guid.NewGuid():N}.skillpkg");
            try
            {
                await using (var fs = File.Create(tempPath))
                {
                    await file.CopyToAsync(fs, ct);
                }

                // Валидация
                var errors = packager.Validate(tempPath);
                if (errors.Count > 0)
                {
                    return Results.BadRequest(new { errors });
                }

                var skill = packager.Import(tempPath, conflict);
                return Results.Created($"/api/skills/{skill.Meta.Id}", ToDto(skill));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    /* best effort */
                }
            }
        }).WithName("ImportSkill").DisableAntiforgery().WithTags("Skills");
    }

    private static SkillDto ToDto(Skill s)
    {
        return new SkillDto(
            s.Meta.Id, s.Meta.Name, s.Meta.Description, s.Meta.PhraseReceivers,
            s.Meta.Version, s.Meta.SuccessRate, s.Meta.TotalUses, s.Meta.CreatedAt);
    }
}
