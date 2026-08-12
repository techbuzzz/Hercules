using Hercules.Tools.Registry;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Эндпоинты управления реестром инструментов (task_024).
///     Позволяет просматривать tools, их health status, категории и enable/disable.
/// </summary>
public static class ToolRegistryController
{
    public static void MapToolRegistry(this IEndpointRouteBuilder app)
    {
        // GET /api/tools — все tools с health и metadata
        app.MapGet("/api/tools", (IToolRegistryService registry) =>
        {
            var all = registry.GetAllEntries();
            return Results.Ok(new
            {
                count = all.Count,
                allowedCount = registry.GetAllowedTools().Count(),
                tools = all.Select(ToDto).ToList()
            });
        }).WithName("ListTools").WithTags("Tools");

        // GET /api/tools/{name} — один tool
        app.MapGet("/api/tools/{name}", (string name, IToolRegistryService registry) =>
        {
            var entry = registry.GetEntry(name);
            return entry is null
                ? Results.NotFound(new { error = $"Tool '{name}' not found" })
                : Results.Ok(ToDto(entry));
        }).WithName("GetTool").WithTags("Tools");

        // GET /api/tools/{name}/health — health status конкретного tool
        app.MapGet("/api/tools/{name}/health", (string name, IToolRegistryService registry) =>
        {
            var entry = registry.GetEntry(name);
            if (entry is null)
                return Results.NotFound(new { error = $"Tool '{name}' not found" });

            return Results.Ok(ToHealthDto(entry));
        }).WithName("GetToolHealth").WithTags("Tools");

        // GET /api/tools/categories — tools по категориям
        app.MapGet("/api/tools/categories", (IToolRegistryService registry) =>
        {
            var all = registry.GetAllEntries();
            var byCategory = all
                .GroupBy(e => e.Category)
                .ToDictionary(
                    g => g.Key.ToString(),
                    g => g.Select(ToDto).ToList());

            return Results.Ok(new
            {
                categories = Enum.GetValues<ToolCategory>()
                    .Where(c => c != ToolCategory.Unknown)
                    .Select(c => new
                    {
                        category = c.ToString(),
                        count = all.Count(e => e.Category == c),
                        tools = all.Where(e => e.Category == c).Select(t => t.Name).ToList()
                    })
                    .ToList(),
                total = all.Count
            });
        }).WithName("GetToolsByCategory").WithTags("Tools");

        // POST /api/tools/{name}/enable — включить tool
        app.MapPost("/api/tools/{name}/enable", (string name, IToolRegistryService registry) =>
        {
            var entry = registry.GetEntry(name);
            if (entry is null)
                return Results.NotFound(new { error = $"Tool '{name}' not found" });

            registry.SetEnabled(name, true);
            return Results.Ok(new { tool = name, enabled = true, message = $"Tool '{name}' enabled" });
        }).WithName("EnableTool").WithTags("Tools");

        // POST /api/tools/{name}/disable — выключить tool
        app.MapPost("/api/tools/{name}/disable", (string name, IToolRegistryService registry) =>
        {
            var entry = registry.GetEntry(name);
            if (entry is null)
                return Results.NotFound(new { error = $"Tool '{name}' not found" });

            registry.SetEnabled(name, false);
            return Results.Ok(new { tool = name, enabled = false, message = $"Tool '{name}' disabled" });
        }).WithName("DisableTool").WithTags("Tools");
    }

    private static object ToDto(ToolRegistryEntry e) => new
    {
        name = e.Name,
        category = e.Category.ToString(),
        description = e.Description,
        enabled = e.Enabled,
        allowed = e.Enabled,
        registeredAt = e.RegisteredAt,
        source = e.Source,
        supportsHealthCheck = e.SupportsHealthCheck,
        healthStatus = e.HealthState.Status.ToString(),
        lastCheckedAt = e.HealthState.LastCheckedAt == DateTime.MinValue
            ? (string?)null
            : e.HealthState.LastCheckedAt.ToString("O"),
        lastError = e.HealthState.LastError,
        consecutiveFailures = e.HealthState.ConsecutiveFailures,
        sideEffectLevel = e.Descriptor?.SideEffectLevel.ToString(),
        requiredPermissions = e.Descriptor?.RequiredPermissions.ToString(),
        timeoutSeconds = e.Descriptor?.TimeoutSeconds ?? 0,
        limits = e.Limits != null ? new
        {
            maxCallsPerMinute = e.Limits.MaxCallsPerMinute,
            timeoutSeconds = e.Limits.TimeoutSeconds,
            maxRetries = e.Limits.MaxRetries,
            maxConcurrent = e.Limits.MaxConcurrent
        } : null
    };

    private static object ToHealthDto(ToolRegistryEntry e) => new
    {
        name = e.Name,
        status = e.HealthState.Status.ToString(),
        lastCheckedAt = e.HealthState.LastCheckedAt == DateTime.MinValue
            ? (string?)null
            : e.HealthState.LastCheckedAt.ToString("O"),
        lastError = e.HealthState.LastError,
        consecutiveFailures = e.HealthState.ConsecutiveFailures,
        enabled = e.Enabled
    };
}
