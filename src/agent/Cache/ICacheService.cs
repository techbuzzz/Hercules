namespace Hercules.Cache;

/// <summary>
///     Task 028: Unified caching service.
///     Provides typed get-or-set with TTL, sliding expiration, sensitivity rules, and invalidation.
/// </summary>
public interface ICacheService
{
    /// <summary>
    ///     Get cached value or compute and store it.
    ///     Uses sliding expiration (touches entry on access) when configured.
    /// </summary>
    /// <typeparam name="T">Cached value type.</typeparam>
    /// <param name="cacheClass">Class of the cached item (controls TTL and sensitivity).</param>
    /// <param name="key">Unique key within the class.</param>
    /// <param name="factory">Async factory called only on cache miss.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<T?> GetOrSetAsync<T>(
        CacheClass cacheClass,
        string key,
        Func<Task<T?>> factory,
        CancellationToken ct = default) where T : class?;

    /// <summary>
    ///     Invalidate a single entry.
    /// </summary>
    void Invalidate(CacheClass cacheClass, string key);

    /// <summary>
    ///     Invalidate all entries for a given cache class.
    /// </summary>
    void InvalidateClass(CacheClass cacheClass);

    /// <summary>
    ///     Invalidate all entries whose key contains the given pattern.
    /// </summary>
    void InvalidatePattern(CacheClass cacheClass, string pattern);

    /// <summary>
    ///     Invalidate all entries regardless of class.
    /// </summary>
    void InvalidateAll();

    /// <summary>
    ///     Get statistics for all cache classes.
    /// </summary>
    IReadOnlyDictionary<CacheClass, CacheStats> GetStats();
}

/// <summary>
///     Read-only statistics for one cache class.
/// </summary>
public sealed class CacheStats
{
    public int HitCount { get; init; }
    public int MissCount { get; init; }
    public int CurrentEntries { get; init; }
    public int EvictedCount { get; init; }
}
