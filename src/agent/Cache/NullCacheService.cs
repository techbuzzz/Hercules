namespace Hercules.Cache;

/// <summary>
///     No-op cache service used when ICacheService is not injected (e.g. in tests or when disabled).
/// </summary>
public sealed class NullCacheService : ICacheService
{
    public static NullCacheService Instance { get; } = new();

    private NullCacheService() { }

    public Task<T?> GetOrSetAsync<T>(CacheClass cacheClass, string key, Func<Task<T?>> factory, CancellationToken ct = default)
        where T : class? => factory.Invoke();

    public void Invalidate(CacheClass cacheClass, string key) { }
    public void InvalidateClass(CacheClass cacheClass) { }
    public void InvalidatePattern(CacheClass cacheClass, string pattern) { }
    public void InvalidateAll() { }
    public IReadOnlyDictionary<CacheClass, CacheStats> GetStats() => new Dictionary<CacheClass, CacheStats>();
}
