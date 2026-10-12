using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Hercules.WebApi.Health;

/// <summary>
/// task_079: response writers for the three /api/health endpoints.
/// Liveness: terse 200 OK. Readiness: 200/503 + status string.
/// Detail: full per-check JSON breakdown (auth-gated by ApiKeyMiddleware).
/// </summary>
internal static class HealthCheckResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static Task WriteLiveness(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = new
        {
            status = "alive",
            time = DateTimeOffset.UtcNow
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }

    public static Task WriteReadiness(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.StatusCode = report.Status == HealthStatus.Healthy
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable;

        var payload = new
        {
            status = report.Status.ToString().ToLowerInvariant(),
            totalDurationMs = (long)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(kvp => new
            {
                name = kvp.Key,
                status = kvp.Value.Status.ToString().ToLowerInvariant(),
                durationMs = (long)kvp.Value.Duration.TotalMilliseconds,
                description = kvp.Value.Description,
                error = kvp.Value.Exception?.Message,
                data = kvp.Value.Data.Count > 0 ? kvp.Value.Data : null
            }).ToList()
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }

    public static Task WriteDetail(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.StatusCode = report.Status == HealthStatus.Healthy
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable;

        var payload = new
        {
            status = report.Status.ToString().ToLowerInvariant(),
            totalDurationMs = (long)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(kvp => new
            {
                name = kvp.Key,
                status = kvp.Value.Status.ToString().ToLowerInvariant(),
                durationMs = (long)kvp.Value.Duration.TotalMilliseconds,
                description = kvp.Value.Description,
                tags = kvp.Value.Tags,
                data = kvp.Value.Data.Count > 0 ? kvp.Value.Data : null,
                exception = kvp.Value.Exception?.ToString()
            }).ToList()
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
