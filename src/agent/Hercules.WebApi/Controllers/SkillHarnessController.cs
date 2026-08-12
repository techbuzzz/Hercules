using Hercules.Agent;
using Hercules.Skills;
using Hercules.Skills.Eval;
using Hercules.Storage;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Eval harness endpoints: run harness, record baseline, get baseline/history.
/// </summary>
public static class SkillHarnessController
{
    public static void MapSkillHarness(this IEndpointRouteBuilder app)
    {
        // POST /api/skills/{id}/eval/harness — запуск eval harness
        app.MapPost("/api/skills/{id}/eval/harness",
            async (string id, IEvalHarnessService harness, CancellationToken ct) =>
            {
                try
                {
                    var result = await harness.RunHarnessAsync(id, ct);
                    return Results.Ok(result);
                }
                catch (Exception ex)
                {
                    return Results.Problem(
                        detail: ex.Message,
                        statusCode: 500);
                }
            }).WithName("RunSkillHarness");

        // POST /api/skills/{id}/eval/baseline — записать baseline
        app.MapPost("/api/skills/{id}/eval/baseline",
            async (string id, RecordBaselineRequest? req, IEvalHarnessService harness,
                WebApiAdapter adapter, CancellationToken ct) =>
            {
                try
                {
                    var skill = adapter.GetSkill(id);
                    if (skill is null)
                    {
                        return Results.NotFound(new { error = "Навык не найден." });
                    }

                    // Запускаем harness для получения текущего результата
                    var regressionResult = await harness.RunHarnessAsync(id, ct);
                    var record = await harness.RecordBaselineAsync(
                        id, new SkillEvaluationResult
                        {
                            SkillId = id,
                            SkillName = skill.Meta.Name,
                            TotalTests = 0,
                            PassedTests = 0,
                            FailedTests = 0,
                            Score = regressionResult.CurrentScore,
                            TestResults = [],
                            EvaluatedAt = regressionResult.EvaluatedAt
                        },
                        req?.RecordedBy ?? "user",
                        req?.Reason ?? "manual");

                    return Results.Created($"/api/skills/{id}/eval/baseline", record);
                }
                catch (Exception ex)
                {
                    return Results.Problem(
                        detail: ex.Message,
                        statusCode: 500);
                }
            }).WithName("RecordSkillBaseline");

        // GET /api/skills/{id}/eval/baseline — получить baseline
        app.MapGet("/api/skills/{id}/eval/baseline",
            (string id, IEvalHarnessService harness) =>
            {
                var baseline = harness.GetBaseline(id);
                return baseline is null
                    ? Results.NotFound(new { error = "Baseline не найден." })
                    : Results.Ok(baseline);
            }).WithName("GetSkillBaseline");

        // GET /api/skills/{id}/eval/history — история baseline
        app.MapGet("/api/skills/{id}/eval/history",
            (string id, IEvalHarnessService harness) =>
            {
                var history = harness.GetBaselineHistory(id);
                return Results.Ok(history);
            }).WithName("GetSkillEvalHistory");

        // GET /api/eval/baselines — все baselines (global)
        app.MapGet("/api/eval/baselines",
            (IEvalHarnessService harness) =>
            {
                var all = harness.GetBaselineHistory();
                return Results.Ok(all);
            }).WithName("ListAllBaselines");
    }
}

/// <summary>
///     Request body для POST /api/skills/{id}/eval/baseline.
/// </summary>
public sealed class RecordBaselineRequest
{
    public string? RecordedBy { get; set; }
    public string? Reason { get; set; }
}
