using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Hercules.Cache;

/// <summary>
///     Task 028: In-memory cache service with sliding expiration, per-class TTL,
///     sensitivity rules, background cleanup, and hit/miss statistics.
/// </summary>
public sealed class CacheService : ICacheService, IDisposable
{
    private readonly CacheConfig _config;
    private readonly ILogger<CacheService> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _store = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<CacheClass, CacheClassStats> _stats = new();
    private readonly Timer _cleanupTimer;
    private readonly int _maxEntries;
    private readonly bool _slidingExpiration;

    public CacheService(CacheConfig config, ILogger<CacheService> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxEntries = config.MaxEntries;
        _slidingExpiration = config.SlidingExpiration;

        // Initialize per-class stats
        foreach (CacheClass cls in Enum.GetValues<CacheClass>())
            _stats[cls] = new CacheClassStats();

        // Background cleanup
        var interval = TimeSpan.FromSeconds(Math.Max(config.CleanupIntervalSeconds, 1));
        _cleanupTimer = new Timer(_ => TryCleanup(), null, interval, interval);
    }

    public async Task<T?> GetOrSetAsync<T>(
        CacheClass cacheClass,
        string key,
        Func<Task<T?>> factory,
        CancellationToken ct = default) where T : class?
    {
        if (!_config.Enabled)
            return await factory.Invoke().ConfigureAwait(false);

        var fullKey = MakeKey(cacheClass, key);
        var ttl = GetTtl(cacheClass);

        // Try read
        if (_store.TryGetValue(fullKey, out var entry) && !entry.IsExpired())
        {
            if (_slidingExpiration)
            {
                // Touch: replace with updated expiry
                _store[fullKey] = entry with { ExpiresAt = DateTime.UtcNow.Add(ttl) };
            }

            Interlocked.Increment(ref _stats[cacheClass].HitCount);
            _logger.LogDebug("[Cache] HIT  {Class}/{Key}", cacheClass, key);
            return entry.Value as T;
        }

        Interlocked.Increment(ref _stats[cacheClass].MissCount);
        _logger.LogDebug("[Cache] MISS {Class}/{Key}", cacheClass, key);

        var value = await factory.Invoke().ConfigureAwait(false);
        if (value is null) return null;

        // Evict if over capacity
        if (_store.Count >= _maxEntries)
            EvictOne(cacheClass);

        var newEntry = new CacheEntry(value, DateTime.UtcNow.Add(ttl));
        _store[fullKey] = newEntry;
        Interlocked.Increment(ref _stats[cacheClass].CurrentEntries);

        return value;
    }

    public void Invalidate(CacheClass cacheClass, string key)
    {
        var fullKey = MakeKey(cacheClass, key);
        if (_store.TryRemove(fullKey, out _))
        {
            Interlocked.Decrement(ref _stats[cacheClass].CurrentEntries);
            _logger.LogDebug("[Cache] INVALIDATE {Class}/{Key}", cacheClass, key);
        }
    }

    public void InvalidateClass(CacheClass cacheClass)
    {
        var prefix = $"{cacheClass}:";
        foreach (var k in _store.Keys.Where(k => k.StartsWith(prefix)).ToList())
        {
            if (_store.TryRemove(k, out _))
                Interlocked.Decrement(ref _stats[cacheClass].CurrentEntries);
        }
        _logger.LogDebug("[Cache] INVALIDATE CLASS {Class}", cacheClass);
    }

    public void InvalidatePattern(CacheClass cacheClass, string pattern)
    {
        var prefix = $"{cacheClass}:";
        foreach (var k in _store.Keys
            .Where(k => k.StartsWith(prefix) && k.Contains(pattern, StringComparison.Ordinal))
            .ToList())
        {
            if (_store.TryRemove(k, out _))
                Interlocked.Decrement(ref _stats[cacheClass].CurrentEntries);
        }
        _logger.LogDebug("[Cache] INVALIDATE PATTERN {Class}/{Pattern}", cacheClass, pattern);
    }

    public void InvalidateAll()
    {
        _store.Clear();
        foreach (var cls in _stats.Values)
        {
            cls.CurrentEntries = 0;
        }
        _logger.LogDebug("[Cache] INVALIDATE ALL");
    }

    public IReadOnlyDictionary<CacheClass, CacheStats> GetStats()
    {
        return _stats.ToDictionary(
            kvp => kvp.Key,
            kvp => new CacheStats
            {
                HitCount = kvp.Value.HitCount,
                MissCount = kvp.Value.MissCount,
                CurrentEntries = kvp.Value.CurrentEntries,
                EvictedCount = kvp.Value.EvictedCount,
            });
    }

    private void TryCleanup()
    {
        try
        {
            var now = DateTime.UtcNow;
            var expired = _store
                .Where(kvp => kvp.Value.IsExpired())
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expired)
            {
                if (_store.TryRemove(key, out var entry))
                {
                    if (Enum.TryParse<CacheClass>(ExtractClass(key), out var cls))
                        Interlocked.Decrement(ref _stats[cls].CurrentEntries);
                }
            }

            if (expired.Count > 0)
                _logger.LogDebug("[Cache] Cleanup removed {Count} expired entries", expired.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Cache] Cleanup failed");
        }
    }

    private void EvictOne(CacheClass preferredClass)
    {
        // Try to evict from the preferred class first
        var preferredPrefix = $"{preferredClass}:";
        var candidates = _store.Keys
            .Where(k => k.StartsWith(preferredPrefix))
            .ToList();

        // Fall back to oldest entry across all classes
        if (candidates.Count == 0)
        {
            candidates = _store.Keys.ToList();
        }

        var oldest = candidates
            .Select(k => (Key: k, Entry: _store[k]))
            .OrderBy(x => x.Entry.ExpiresAt)
            .FirstOrDefault();

        if (oldest.Key is not null && _store.TryRemove(oldest.Key, out _))
        {
            if (Enum.TryParse<CacheClass>(ExtractClass(oldest.Key), out var cls))
            {
                Interlocked.Decrement(ref _stats[cls].CurrentEntries);
                Interlocked.Increment(ref _stats[cls].EvictedCount);
            }
            _logger.LogDebug("[Cache] EVICT {Key}", oldest.Key);
        }
    }

    private TimeSpan GetTtl(CacheClass cacheClass)
    {
        var name = cacheClass.ToString();
        if (_config.TtlByClass.TryGetValue(name, out var seconds) && seconds > 0)
            return TimeSpan.FromSeconds(seconds);
        return TimeSpan.FromSeconds(_config.DefaultTtlSeconds);
    }

    private static string MakeKey(CacheClass cls, string key) => $"{cls}:{key}";

    private static string ExtractClass(string fullKey)
    {
        var colon = fullKey.IndexOf(':');
        return colon > 0 ? fullKey[..colon] : fullKey;
    }

    public void Dispose() => _cleanupTimer.Dispose();

    private sealed record CacheEntry(object Value, DateTime ExpiresAt)
    {
        public bool IsExpired() => DateTime.UtcNow > ExpiresAt;
    }

    private sealed class CacheClassStats
    {
        public int HitCount;
        public int MissCount;
        public int CurrentEntries;
        public int EvictedCount;
    }
}
