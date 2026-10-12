using System.Text.Json;
using Hercules.Config;
using Hercules.WebApi.Auth;
using Hercules.WebApi.Config;
using Hercules.WebApi.Contracts;

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
        app.MapGet("/api/config", (RuntimeConfigStore store) => Results.Ok(new AgentConfigDto
        {
            Config = ToDictionary(store.Current),
            Source = "runtime"
        })).WithName("GetConfig").CacheOutput(OutputCachePolicies.Config).WithTags("Config").Produces<AgentConfigDto>(200);

        // PUT /api/config — полная замена конфигурации (system-only, task_097).
        app.MapPut("/api/config", (JsonElement body, RuntimeConfigStore store) =>
        {
            try
            {
                var next = body.Deserialize<AppConfig>(JsonOptions) ?? throw new InvalidOperationException("Config body is empty");
                store.Update(next);
                return Results.Ok(new ConfigUpdateResponseDto
                {
                    Status = "updated",
                    Config = ToDictionary(store.Current)
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"Не удалось разобрать конфигурацию: {ex.Message}" });
            }
        }).WithName("UpdateConfig").RequireSystemRole().WithTags("Config").Produces<ConfigUpdateResponseDto>(200);

        // PATCH /api/config — частичное обновление (merge patch), доступно обеим ролям.
        app.MapPatch("/api/config", (JsonElement patch, RuntimeConfigStore store) =>
        {
            try
            {
                // Studio reads masked config and patches it back after an edit. Drop any
                // property still holding the marker so the merge leaves the real secret
                // alone instead of overwriting it with the mask.
                store.Patch(ConfigRedactor.StripRedacted(patch));
                return Results.Ok(new ConfigUpdateResponseDto
                {
                    Status = "patched",
                    Config = ToDictionary(store.Current)
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"Не удалось применить патч: {ex.Message}" });
            }
        }).WithName("PatchConfig").WithTags("Config").Produces<ConfigUpdateResponseDto>(200);
    }

    /// <summary>
    /// Flattens <see cref="AppConfig"/> into the open-ended dictionary the DTOs expose.
    /// The configuration has no fixed member set, so the schema cannot enumerate keys.
    /// <para>
    /// Secret-named values are replaced with a redaction marker: this endpoint is readable
    /// by any authenticated session, and <c>AppConfig</c> embeds LLM API keys, the Postgres
    /// connection string, a bot token and a signing key.
    /// </para>
    /// </summary>
    private static IReadOnlyDictionary<string, object?> ToDictionary(AppConfig config)
    {
        var element = JsonSerializer.SerializeToElement(config, JsonOptions);
        var root = element.ValueKind == JsonValueKind.Object && element.TryGetProperty("config", out var inner)
            ? inner
            : element;

        var redacted = ConfigRedactor.Redact(root);

        return redacted.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
    }
}
