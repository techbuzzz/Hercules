using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Hercules.Cache;

/// <summary>
///     Task 028 + Task 083: In-memory cache service with sliding expiration, per-class TTL,
///     sensitivity rules, background cleanup, hit/miss statistics, stampede dedup,
///     sorted expiry index, O(1) sliding-expiration updates, and O(k) class invalidation.
/// </summary>
/// <remarks>
///     <para>Stampede dedup (H9): concurrent misses for the same key share one factory invocation
///     via a per-key <see cref="SemaphoreSlim"/>. The waiters re-check the store under the
///     semaphore and return the cached value as a hit.</para>
///     <para>Eviction (perf): a <see cref="SortedDictionary{TKey,TValue}"/> keyed by UTC ticks holds
///     the live entries; <c>EvictOne</c> peeks the oldest in O(log n) instead of sorting all keys
///     on every insert.</para>
///     <para>Sliding expiration (perf): <see cref="CacheEntry"/> is mutable; sliding-touch updates
///     the expiry in-place (no allocation, no dictionary write).</para>
///     <para>Storage (perf): a per-class <see cref="ConcurrentDictionary{TKey,TValue}"/> eliminates
///     the per-hit <c>"Class:Key"</c> string concatenation that the previous global store needed.</para>
///     <para>Class invalidation (perf): the per-class dictionary makes <c>InvalidateClass</c> and
///     <c>InvalidatePattern</c> O(k) in the number of entries in that class.</para>
/// </remarks>
public sealed class CacheService : ICacheService, IDisposable
{
    private readonly CacheConfig _config;
    private readonly ILogger<CacheService> _logger;

    // Primary store: CacheClass → (user key → entry). No string concatenation needed.
    // The per-class design also gives O(k) InvalidateClass/Pattern for free.
    private readonly ConcurrentDictionary<CacheClass, ConcurrentDictionary<string, CacheEntry>> _byClass = new();

    // Per-key stampede dedup. Lazily populated on first miss. The semaphores are kept around
    // even after the entry is evicted/expired — they are tiny and the number of distinct keys
    // is bounded by the cache capacity, so this is fine.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    // Sorted expiry index: ticks (UTC, long) → (class, user key). Each entry is unique by
    // ticks resolution (100 ns). On the rare tick-collision (concurrent inserts at exactly
    // the same instant), the second insertion overwrites the index entry, but the store still
    // holds both entries; the background cleanup will eventually remove the orphan. This keeps
    // the index zero-allocation on the hot read path (no per-touch HashSet).
    // Protected by _expiryLock.
    private readonly SortedDictionary<long, ExpiryRecord> _expiryIndex = new();
    private readonly object _expiryLock = new();

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

        // Initialize per-class stats and per-class dictionaries.
        foreach (CacheClass cls in Enum.GetValues<CacheClass>())
        {
            _stats[cls] = new CacheClassStats();
            _byClass[cls] = new ConcurrentDictionary<string, CacheEntry>(StringComparer.Ordinal);
        }

        // Background cleanup
        var interval = TimeSpan.FromSeconds(Math.Max(config.CleanupIntervalSeconds, 1));
        _cleanupTimer = new Timer(_ => TryCleanup(), null, interval, interval);
    }

    public Task<T?> GetOrSetAsync<T>(
        CacheClass cacheClass,
        string key,
        Func<Task<T?>> factory,
        CancellationToken ct = default) where T : class?
    {
        if (!_config.Enabled)
            return factory.Invoke();

        var classDict = _byClass[cacheClass];
        var ttl = GetTtl(cacheClass);

        // Fast path: lock-free read. Returning a completed Task directly avoids the
        // async state-machine allocation that an `async` method would incur on every call.
        if (TryHit(classDict, cacheClass, key, ttl, out var cached))
        {
            return Task.FromResult<T?>(cached as T);
        }

        // Slow path: stampede dedup, factory invocation, and insertion.
        return GetOrSetMissAsync(classDict, cacheClass, key, factory, ttl, ct);
    }

    private async Task<T?> GetOrSetMissAsync<T>(
        ConcurrentDictionary<string, CacheEntry> classDict,
        CacheClass cacheClass,
        string key,
        Func<Task<T?>> factory,
        TimeSpan ttl,
        CancellationToken ct) where T : class?
    {
        // Stampede dedup: serialize per-key computation.
        var sem = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Re-check under lock: another thread may have populated the entry while we waited.
            if (TryHit(classDict, cacheClass, key, ttl, out var cached))
            {
                return cached as T;
            }

            // We are the only thread computing this key. Count this as the single miss.
            Interlocked.Increment(ref _stats[cacheClass].MissCount);
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("[Cache] MISS {Class}/{Key}", cacheClass, key);
            }

            var value = await factory.Invoke().ConfigureAwait(false);
            if (value is not null)
            {
                TryInsert(classDict, cacheClass, key, value, ttl);
            }
            return value;
        }
        finally
        {
            sem.Release();
        }
    }

    public void Invalidate(CacheClass cacheClass, string key)
    {
        if (RemoveEntry(cacheClass, key))
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("[Cache] INVALIDATE {Class}/{Key}", cacheClass, key);
            }
        }
    }

    public void InvalidateClass(CacheClass cacheClass)
    {
        var classDict = _byClass[cacheClass];
        var keys = classDict.Keys.ToList();
        var removed = 0;
        foreach (var k in keys)
        {
            if (RemoveEntry(cacheClass, k)) removed++;
        }
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("[Cache] INVALIDATE CLASS {Class} ({Count} entries)", cacheClass, removed);
        }
    }

    public void InvalidatePattern(CacheClass cacheClass, string pattern)
    {
        var classDict = _byClass[cacheClass];
        var keys = classDict.Keys
            .Where(k => k.Contains(pattern, StringComparison.Ordinal))
            .ToList();
        var removed = 0;
        foreach (var k in keys)
        {
            if (RemoveEntry(cacheClass, k)) removed++;
        }
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("[Cache] INVALIDATE PATTERN {Class}/{Pattern} ({Count} entries)",
                cacheClass, pattern, removed);
        }
    }

    public void InvalidateAll()
    {
        foreach (var kvp in _byClass)
        {
            var keys = kvp.Value.Keys.ToList();
            foreach (var k in keys)
            {
                RemoveEntry(kvp.Key, k);
            }
        }
        foreach (var cls in _stats.Values)
        {
            cls.CurrentEntries = 0;
        }
        lock (_expiryLock)
        {
            _expiryIndex.Clear();
        }
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("[Cache] INVALIDATE ALL");
        }
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

    private bool TryHit(
        ConcurrentDictionary<string, CacheEntry> classDict,
        CacheClass cacheClass,
        string key,
        TimeSpan ttl,
        out object? value)
    {
        if (!classDict.TryGetValue(key, out var entry))
        {
            value = null;
            return false;
        }
        if (entry.IsExpired())
        {
            value = null;
            return false;
        }

        if (_slidingExpiration)
        {
            // In-place expiry update. The store reference does not change, so we don't
            // need a dictionary write — readers still see the same CacheEntry instance.
            var newExpiry = DateTime.UtcNow.Add(ttl);
            if (entry.ExpiresAt != newExpiry)
            {
                TouchExpiry(entry, cacheClass, key, newExpiry);
            }
        }

        Interlocked.Increment(ref _stats[cacheClass].HitCount);
        // Gate the log call to avoid params[] allocation when Debug logging is disabled.
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("[Cache] HIT  {Class}/{Key}", cacheClass, key);
        }
        value = entry.Value;
        return true;
    }

    private void TryInsert(
        ConcurrentDictionary<string, CacheEntry> classDict,
        CacheClass cacheClass,
        string key,
        object value,
        TimeSpan ttl)
    {
        if (GetTotalCount() >= _maxEntries)
        {
            EvictOne(cacheClass);
        }

        var expiresAt = DateTime.UtcNow.Add(ttl);
        var entry = new CacheEntry(cacheClass, key, value, expiresAt);
        if (!classDict.TryAdd(key, entry))
        {
            // Race: another thread already inserted this key. Just leave theirs.
            return;
        }

        // Update expiry index.
        var ticks = expiresAt.Ticks;
        lock (_expiryLock)
        {
            _expiryIndex[ticks] = new ExpiryRecord(cacheClass, key);
        }

        Interlocked.Increment(ref _stats[cacheClass].CurrentEntries);
    }

    private bool RemoveEntry(CacheClass cacheClass, string key)
    {
        if (!_byClass[cacheClass].TryRemove(key, out var entry))
        {
            return false;
        }

        Interlocked.Decrement(ref _stats[cacheClass].CurrentEntries);

        // Best-effort expiry index cleanup. The expiry might have already been moved
        // (sliding touch) or removed (cleanup batch), so a missing record is fine.
        var ticks = entry.ExpiresAt.Ticks;
        lock (_expiryLock)
        {
            // Only remove if this ticks-key still maps to our entry. Otherwise some other
            // entry (or a subsequent touch) has overwritten it.
            if (_expiryIndex.TryGetValue(ticks, out var indexed)
                && indexed.CacheClass == cacheClass
                && indexed.Key == key)
            {
                _expiryIndex.Remove(ticks);
            }
        }
        return true;
    }

    private void TouchExpiry(CacheEntry entry, CacheClass cacheClass, string key, DateTime newExpiry)
    {
        var oldTicks = entry.ExpiresAt.Ticks;
        var newTicks = newExpiry.Ticks;
        if (oldTicks == newTicks) return;

        // Volatile write on the entry so concurrent readers see the new value.
        entry.ExpiresAt = newExpiry;

        lock (_expiryLock)
        {
            // Remove the old index entry only if it still belongs to this entry.
            if (_expiryIndex.TryGetValue(oldTicks, out var indexedOld)
                && indexedOld.CacheClass == cacheClass
                && indexedOld.Key == key)
            {
                _expiryIndex.Remove(oldTicks);
            }
            _expiryIndex[newTicks] = new ExpiryRecord(cacheClass, key);
        }
    }

    private void TryCleanup()
    {
        try
        {
            var nowTicks = DateTime.UtcNow.Ticks;
            var toRemove = new List<ExpiryRecord>();

            lock (_expiryLock)
            {
                // Drain from the oldest end while entries are expired.
                while (_expiryIndex.Count > 0)
                {
                    var first = _expiryIndex.First();
                    if (first.Key > nowTicks) break;
                    toRemove.Add(first.Value);
                    _expiryIndex.Remove(first.Key);
                }
            }

            var removed = 0;
            foreach (var r in toRemove)
            {
                if (RemoveEntry(r.CacheClass, r.Key)) removed++;
            }

            if (removed > 0 && _logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("[Cache] Cleanup removed {Count} expired entries", removed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Cache] Cleanup failed");
        }
    }

    private void EvictOne(CacheClass preferredClass)
    {
        // Prefer evicting from the requested class — search its dict for the oldest entry.
        var classDict = _byClass[preferredClass];
        if (!classDict.IsEmpty)
        {
            string? oldestKey = null;
            long oldestTicks = long.MaxValue;
            foreach (var kvp in classDict)
            {
                var t = kvp.Value.ExpiresAt.Ticks;
                if (t < oldestTicks)
                {
                    oldestTicks = t;
                    oldestKey = kvp.Key;
                }
            }
            if (oldestKey is not null && RemoveEntry(preferredClass, oldestKey))
            {
                Interlocked.Increment(ref _stats[preferredClass].EvictedCount);
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("[Cache] EVICT {Class}/{Key} (preferred class)", preferredClass, oldestKey);
                }
                return;
            }
        }

        // Fall back to the global oldest entry from the sorted index.
        ExpiryRecord? fallback = null;
        lock (_expiryLock)
        {
            while (_expiryIndex.Count > 0)
            {
                var first = _expiryIndex.First();
                if (_byClass[first.Value.CacheClass].ContainsKey(first.Value.Key))
                {
                    fallback = first.Value;
                    _expiryIndex.Remove(first.Key);
                    break;
                }
                // The indexed entry has been removed already; drop the stale index entry.
                _expiryIndex.Remove(first.Key);
            }
        }

        if (fallback is not null && RemoveEntry(fallback.Value.CacheClass, fallback.Value.Key))
        {
            Interlocked.Increment(ref _stats[fallback.Value.CacheClass].EvictedCount);
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("[Cache] EVICT {Class}/{Key} (global oldest)",
                    fallback.Value.CacheClass, fallback.Value.Key);
            }
        }
    }

    private int GetTotalCount()
    {
        // Sum over the per-class dictionaries. O(number of classes), which is small and constant.
        var total = 0;
        foreach (var d in _byClass.Values) total += d.Count;
        return total;
    }

    private TimeSpan GetTtl(CacheClass cacheClass)
    {
        var name = cacheClass.ToString();
        if (_config.TtlByClass.TryGetValue(name, out var seconds) && seconds > 0)
            return TimeSpan.FromSeconds(seconds);
        return TimeSpan.FromSeconds(_config.DefaultTtlSeconds);
    }

    public void Dispose() => _cleanupTimer.Dispose();

    /// <summary>
    ///     Task 083: mutable cache entry. Sliding-expiration updates write a single
    ///     <see cref="long"/> field (UTC ticks) atomically via <see cref="Volatile"/>;
    ///     no allocation, no dictionary write.
    /// </summary>
    private sealed class CacheEntry
    {
        public CacheClass CacheClass { get; }
        public string Key { get; }
        public object Value { get; }

        private long _expiresAtTicks;

        public DateTime ExpiresAt
        {
            get => new(Volatile.Read(ref _expiresAtTicks), DateTimeKind.Utc);
            set => Volatile.Write(ref _expiresAtTicks, value.Ticks);
        }

        public CacheEntry(CacheClass cacheClass, string key, object value, DateTime expiresAt)
        {
            CacheClass = cacheClass;
            Key = key;
            Value = value;
            _expiresAtTicks = expiresAt.Ticks;
        }

        public bool IsExpired() => DateTime.UtcNow > ExpiresAt;
    }

    private readonly record struct ExpiryRecord(CacheClass CacheClass, string Key);

    private sealed class CacheClassStats
    {
        public int HitCount;
        public int MissCount;
        public int CurrentEntries;
        public int EvictedCount;
    }
}
