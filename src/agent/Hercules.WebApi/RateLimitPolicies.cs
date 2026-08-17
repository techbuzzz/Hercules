using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace Hercules.WebApi;

/// <summary>
///     Stable policy names for the framework <see cref="RateLimiter" />.
///     Controllers reference these via <c>.RequireRateLimiting(RateLimitPolicies.Chat)</c>
///     so the policy names stay in one place and tests can target them by string.
///     Specification: task_081.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Per-IP fixed window for <c>POST /api/chat</c>.</summary>
    public const string Chat = "chat";

    /// <summary>Global concurrency cap for expensive endpoints (reflection, eval, SLO).</summary>
    public const string Expensive = "expensive";

    /// <summary>Resolve a partition key for a request — prefers <c>X-Forwarded-For</c> when present.</summary>
    public static string GetClientKey(HttpContext context)
    {
        var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (string.IsNullOrEmpty(forwarded))
        {
            return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
        }

        var ip = forwarded.Split(',', StringSplitOptions.TrimEntries)[0];
        return !string.IsNullOrEmpty(ip)
            ? $"ip:{ip}"
            : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }
}

/// <summary>
///     Named output-cache policies (task_081). Apply via <c>.CacheOutput(OutputCachePolicies.Skills)</c>
///     on a route group or individual endpoint.
/// </summary>
public static class OutputCachePolicies
{
    /// <summary>5-minute cache for skill listings / read-only skill metadata.</summary>
    public const string Skills = "Skills";

    /// <summary>30-second cache for read-only config endpoint.</summary>
    public const string Config = "Config";
}
