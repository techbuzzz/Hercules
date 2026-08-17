using System.Text.Json;
using Hercules.Config;
using Hercules.WebApi.Auth;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     DTO и эндпоинты конфигурации агента.
///     GET /api/config — текущая конфигурация
///     PUT /api/config — полная замена (system-only, task_097, ADR-0004)
///     PATCH /api/config — частичное обновление (merge, contribute+system)
/// </summary>
public static class ConfigController
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static void MapConfig(this IEndpointRouteBuilder app)
    {
        // GET /api/config — текущая "живая" конфигурация
        // task_081: short-TTL output cache (30s) для read-only config endpoint.
        app.MapGet("/api/config", (RuntimeConfigStore store) => Results.Ok(new { config = store.Current, source = "runtime" })).WithName("GetConfig").CacheOutput(OutputCachePolicies.Config);

        // PUT /api/config — полная замена конфигурации (system-only, task_097).
        app.MapPut("/api/config", (JsonElement body, RuntimeConfigStore store) =>
        {
            try
            {
                var next = body.Deserialize<AppConfig>(JsonOptions) ?? throw new InvalidOperationException("Config body is empty");
                store.Update(next);
                return Results.Ok(new { status = "updated", config = store.Current });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"Не удалось разобрать конфигурацию: {ex.Message}" });
            }
        }).WithName("UpdateConfig").RequireSystemRole();

        // PATCH /api/config — частичное обновление (merge patch), доступно обеим ролям.
        app.MapPatch("/api/config", (JsonElement patch, RuntimeConfigStore store) =>
        {
            try
            {
                store.Patch(patch);
                return Results.Ok(new { status = "patched", config = store.Current });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"Не удалось применить патч: {ex.Message}" });
            }
        }).WithName("PatchConfig");
    }
}
