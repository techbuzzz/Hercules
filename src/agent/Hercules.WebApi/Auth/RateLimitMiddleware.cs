using System.Collections.Concurrent;
using Hercules.WebApi.Config;

namespace Hercules.WebApi.Auth;

/// <summary>
///     Простое rate limiting по IP-адресу для маршрутов /api/chat.
///     Использует sliding window: отслеживает количество запросов в минуту.
/// </summary>
public sealed class RateLimitMiddleware(RequestDelegate next, WebApiConfig cfg, ILogger<RateLimitMiddleware> logger)
{
    private readonly ConcurrentDictionary<string, RateLimitEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _window = TimeSpan.FromMinutes(1);

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;

        if (cfg.ChatRateLimitPerMinute <= 0 || !path.StartsWithSegments("/api/chat"))
        {
            await next(context);
            return;
        }

        var clientKey = GetClientKey(context);
        var now = DateTimeOffset.UtcNow;
        var entry = _entries.AddOrUpdate(
            clientKey,
            _ => new RateLimitEntry { Count = 1, WindowStart = now },
            (_, existing) =>
            {
                if (now - existing.WindowStart >= _window)
                {
                    return new RateLimitEntry { Count = 1, WindowStart = now };
                }
                existing.Count++;
                return existing;
            });

        if (entry.Count > cfg.ChatRateLimitPerMinute)
        {
            logger.LogWarning("Rate limit exceeded for {ClientKey}: {Count} requests in window", clientKey, entry.Count);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsJsonAsync(new { error = "Rate limit exceeded. Try again later." });
            return;
        }

        context.Response.Headers["X-RateLimit-Limit"] = cfg.ChatRateLimitPerMinute.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = Math.Max(0, cfg.ChatRateLimitPerMinute - entry.Count).ToString();

        await next(context);
    }

    private static string GetClientKey(HttpContext context)
    {
        var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwarded))
        {
            var ip = forwarded.Split(',', StringSplitOptions.TrimEntries)[0];
            if (!string.IsNullOrEmpty(ip)) return ip;
        }
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private sealed class RateLimitEntry
    {
        public int Count;
        public DateTimeOffset WindowStart;
    }
}