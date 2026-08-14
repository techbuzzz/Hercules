using System.Text.Json;
using Hercules.Config;
using Hercules.Config.Rollout;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     DTO и эндпоинты staged rollout конфигурационных и policy бандлов (task_058).
///     GET  /api/rollout/state     — текущее состояние
///     POST /api/rollout/apply     — загрузить и применить бандл
///     POST /api/rollout/promote  — продвинуть бандл на следующую стадию
///     POST /api/rollout/rollback — откатиться к last-known-good
///     GET  /api/rollout/bundle/{id} — получить бандл по ID
/// </summary>
public static class RolloutController
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static void MapRollout(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rollout").WithTags("Rollout");

        // GET /api/rollout/state
        group.MapGet("/state", (IRolloutManager manager) =>
        {
            var state = manager.GetState();
            return Results.Ok(new { state });
        }).WithName("GetRolloutState");

        // POST /api/rollout/apply
        group.MapPost("/apply", async (JsonElement body, IRolloutManager manager, CancellationToken ct) =>
        {
            try
            {
                var bundle = body.Deserialize<ConfigBundle>(JsonOptions);
                if (bundle == null)
                {
                    return Results.BadRequest(new { error = "Invalid bundle JSON." });
                }

                var result = await manager.ApplyBundleAsync(bundle, ct);
                if (!result.Success)
                {
                    return Results.BadRequest(new
                    {
                        error = result.Error,
                        bundleId = result.AppliedBundleId,
                        stage = result.NewStage.ToString()
                    });
                }

                return Results.Ok(new
                {
                    status = "applied",
                    bundleId = result.AppliedBundleId,
                    stage = result.NewStage.ToString()
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).WithName("ApplyRolloutBundle");

        // POST /api/rollout/promote
        group.MapPost("/promote", ( PromoteRequest req, IRolloutManager manager) =>
        {
            var result = manager.PromoteStageAsync(req.BundleId).GetAwaiter().GetResult();
            if (!result.Success)
            {
                return Results.BadRequest(new
                {
                    error = result.Error,
                    bundleId = result.AppliedBundleId,
                    stage = result.NewStage.ToString()
                });
            }

            return Results.Ok(new
            {
                status = "promoted",
                bundleId = result.AppliedBundleId,
                stage = result.NewStage.ToString()
            });
        }).WithName("PromoteRolloutStage");

        // POST /api/rollout/rollback
        group.MapPost("/rollback", (RollbackRequest? req, IRolloutManager manager) =>
        {
            var result = manager.RollbackAsync(req?.Reason).GetAwaiter().GetResult();
            if (!result.Success)
            {
                return Results.BadRequest(new
                {
                    error = result.Error,
                    bundleId = result.AppliedBundleId,
                    stage = result.NewStage.ToString()
                });
            }

            return Results.Ok(new
            {
                status = "rolled_back",
                bundleId = result.AppliedBundleId,
                stage = result.NewStage.ToString()
            });
        }).WithName("RollbackRollout");

        // GET /api/rollout/bundle/{id}
        group.MapGet("/bundle/{id}", (string id, IRolloutManager manager) =>
        {
            var bundle = manager.GetBundle(id);
            return bundle == null
                ? Results.NotFound(new { error = $"Bundle '{id}' not found." })
                : Results.Ok(new { bundle });
        }).WithName("GetRolloutBundle");
    }
}

public sealed class PromoteRequest
{
    public string BundleId { get; set; } = "";
}

public sealed class RollbackRequest
{
    public string? Reason { get; set; }
}
