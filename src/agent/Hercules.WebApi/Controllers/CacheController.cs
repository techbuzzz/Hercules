using Hercules.Cache;
using Hercules.Config;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Эндпоинты unified cache: статистика, инвалидация.
///     Task 028 — Кэширование.
/// </summary>
public static class CacheController
{
    public static void MapCache(this IEndpointRouteBuilder app)
    {
        // GET /api/cache/stats — статистика по всем cache-классам
        app.MapGet("/api/cache/stats", (ICacheService? cache, CacheConfig? config) =>
        {
            if (cache is null)
                return Results.NotFound(new { error = "Cache not available (caching disabled)" });

            var stats = cache.GetStats();
            var totalHits = stats.Values.Sum(s => s.HitCount);
            var totalMisses = stats.Values.Sum(s => s.MissCount);
            var totalEntries = stats.Values.Sum(s => s.CurrentEntries);
            var totalEvicted = stats.Values.Sum(s => s.EvictedCount);
            var hitRate = totalHits + totalMisses > 0
                ? Math.Round((double)totalHits / (totalHits + totalMisses) * 100, 2)
                : 0.0;

            return Results.Ok(new
            {
                enabled = config?.Enabled ?? true,
                classes = stats.ToDictionary(
                    kvp => kvp.Key.ToString(),
                    kvp => new
                    {
                        hits = kvp.Value.HitCount,
                        misses = kvp.Value.MissCount,
                        currentEntries = kvp.Value.CurrentEntries,
                        evicted = kvp.Value.EvictedCount,
                        hitRatePct = kvp.Value.HitCount + kvp.Value.MissCount > 0
                            ? Math.Round((double)kvp.Value.HitCount / (kvp.Value.HitCount + kvp.Value.MissCount) * 100, 2)
                            : 0.0
                    }),
                totals = new
                {
                    totalHits,
                    totalMisses,
                    totalEntries,
                    totalEvicted,
                    overallHitRatePct = hitRate
                }
            });
        }).WithName("CacheStats");

        // POST /api/cache/invalidate — инвалидация cache
        app.MapPost("/api/cache/invalidate", (CacheInvalidateRequest request, ICacheService? cache) =>
        {
            if (cache is null)
                return Results.NotFound(new { error = "Cache not available (caching disabled)" });

            if (request.All)
            {
                cache.InvalidateAll();
                return Results.Ok(new { invalidated = "all", count = "all classes" });
            }

            if (!string.IsNullOrWhiteSpace(request.Class) &&
                Enum.TryParse<CacheClass>(request.Class, ignoreCase: true, out var cls))
            {
                if (!string.IsNullOrWhiteSpace(request.Key))
                {
                    cache.Invalidate(cls, request.Key);
                    return Results.Ok(new { invalidated = "key", cls = cls.ToString(), key = request.Key });
                }

                if (!string.IsNullOrWhiteSpace(request.Pattern))
                {
                    cache.InvalidatePattern(cls, request.Pattern);
                    return Results.Ok(new { invalidated = "pattern", cls = cls.ToString(), pattern = request.Pattern });
                }

                cache.InvalidateClass(cls);
                return Results.Ok(new { invalidated = "class", cls = cls.ToString() });
            }

            return Results.BadRequest(new
            {
                error = "Invalid request. Provide 'all=true' or 'class' with optional 'key' or 'pattern'."
            });
        }).WithName("CacheInvalidate");
    }
}

/// <summary>
///     Request body for cache invalidation.
/// </summary>
public sealed class CacheInvalidateRequest
{
    /// <summary>Invalidate all cache classes.</summary>
    public bool All { get; set; }

    /// <summary>Cache class to invalidate (e.g. "Embedding", "RoutingDecision").</summary>
    public string? Class { get; set; }

    /// <summary>Specific key to invalidate (requires 'class').</summary>
    public string? Key { get; set; }

    /// <summary>Key pattern to invalidate (requires 'class').</summary>
    public string? Pattern { get; set; }
}
