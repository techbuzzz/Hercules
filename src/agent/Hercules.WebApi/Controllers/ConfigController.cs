using System.Text.Json;
using Hercules.Config;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     DTO и эндпоинты конфигурации агента.
///     GET /api/config — текущая конфигурация
///     PUT /api/config — полная замена
///     PATCH /api/config — частичное обновление (merge)
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
        app.MapGet("/api/config", (RuntimeConfigStore store) => Results.Ok(new { config = store.Current, source = "runtime" })).WithName("GetConfig");

        // PUT /api/config — полная замена конфигурации
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
        }).WithName("UpdateConfig");

        // PATCH /api/config — частичное обновление (merge patch)
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
