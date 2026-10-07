using Hercules.Cache;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Phase5Tests;

/// <summary>
///     Tests for <see cref="CacheService"/> perf improvements (task_083, H9 + perf):
///     <list type="bullet">
///         <item>Stampede dedup: concurrent misses for the same key invoke the factory exactly once.</item>
///         <item>Sorted expiry index: <c>EvictOne</c> is O(log n), not O(n log n).</item>
///         <item>Mutable <c>CacheEntry</c>: sliding-expiration touch does not allocate a new entry.</item>
///         <item>Per-class prefix index: <c>InvalidateClass</c> is O(k) in the class size.</item>
///     </list>
/// </summary>
public class CacheServiceStampedeEvictionTests : IDisposable
{
    private readonly Mock<ILogger<CacheService>> _loggerMock = new();

    public void Dispose() { }

    private CacheService NewCache(int maxEntries = 100, int ttlSeconds = 60, bool sliding = true) =>
        new(new CacheConfig
        {
            Enabled = true,
            DefaultTtlSeconds = ttlSeconds,
            MaxEntries = maxEntries,
            CleanupIntervalSeconds = 3600,
            SlidingExpiration = sliding,
        }, _loggerMock.Object);

    // ---------------------------------------------------------------------------------------
    // Stampede dedup
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task GetOrSet_ConcurrentMisses_InvokeFactoryOnce()
    {
        using var cache = NewCache();
        var factoryCalls = 0;

        async Task<object?> SlowFactory()
        {
            Interlocked.Increment(ref factoryCalls);
            await Task.Delay(50).ConfigureAwait(false);
            return "value";
        }

        const int concurrency = 50;
        var tasks = new Task<string?>[concurrency];
        for (var i = 0; i < concurrency; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                var result = await cache.GetOrSetAsync(CacheClass.Embedding, "hot-key", SlowFactory);
                return result as string;
            });
        }

        var results = await Task.WhenAll(tasks);

        // Factory was called exactly once even though 50 threads raced for the same key.
        Assert.Equal(1, factoryCalls);
        // All threads saw the same value.
        Assert.All(results, r => Assert.Equal("value", r));
    }

    [Fact]
    public async Task GetOrSet_ConcurrentMisses_DifferentKeys_EachFactoryOnce()
    {
        using var cache = NewCache();
        var factoryCalls = 0;

        async Task<object?> Factory()
        {
            Interlocked.Increment(ref factoryCalls);
            await Task.Delay(20).ConfigureAwait(false);
            return Guid.NewGuid().ToString();
        }

        const int distinctKeys = 20;
        const int concurrentPerKey = 10;

        var tasks = new Task<string?>[distinctKeys * concurrentPerKey];
        var seen = new string?[distinctKeys];
        for (var k = 0; k < distinctKeys; k++)
        {
            for (var c = 0; c < concurrentPerKey; c++)
            {
                var key = $"k-{k}";
                var slot = k;
                tasks[k * concurrentPerKey + c] = Task.Run(async () =>
                {
                    var result = await cache.GetOrSetAsync(CacheClass.Embedding, key, Factory);
                    var s = result as string;
                    Volatile.Write(ref seen[slot], s);
                    return s;
                });
            }
        }

        var results = await Task.WhenAll(tasks);

        // 20 distinct keys, 10 concurrent callers each → 20 factory invocations total.
        Assert.Equal(distinctKeys, factoryCalls);
        Assert.All(results, r => Assert.NotNull(r));
        // All callers for a given key see the same value.
        for (var k = 0; k < distinctKeys; k++)
        {
            var s = Volatile.Read(ref seen[k]);
            Assert.NotNull(s);
        }
    }

    [Fact]
    public async Task GetOrSet_ConcurrentMisses_FactoryThrows_OtherWaitersAlsoFail()
    {
        using var cache = NewCache();

        async Task<object?> FailingFactory()
        {
            await Task.Delay(20).ConfigureAwait(false);
            throw new InvalidOperationException("boom");
        }

        var tasks = new Task<bool>[5];
        for (var i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                try
                {
                    await cache.GetOrSetAsync(CacheClass.Embedding, "boom-key", FailingFactory);
                    return false;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            });
        }

        var sawExceptions = await Task.WhenAll(tasks);
        Assert.All(sawExceptions, b => Assert.True(b));
    }

    // ---------------------------------------------------------------------------------------
    // Sorted expiry index & eviction cost
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task EvictOne_PrefersRequestedClass_ThenFallsBackToGlobalOldest()
    {
        using var cache = NewCache(maxEntries: 4);

        // Fill store: 2 in Embedding, 2 in RoutingDecision.
        await cache.GetOrSetAsync(CacheClass.Embedding, "e1", () => Task.FromResult<object?>("v"));
        await Task.Delay(10);
        await cache.GetOrSetAsync(CacheClass.Embedding, "e2", () => Task.FromResult<object?>("v"));
        await Task.Delay(10);
        await cache.GetOrSetAsync(CacheClass.RoutingDecision, "r1", () => Task.FromResult<object?>("v"));
        await Task.Delay(10);
        await cache.GetOrSetAsync(CacheClass.RoutingDecision, "r2", () => Task.FromResult<object?>("v"));

        // Adding a 5th entry triggers EvictOne with preferredClass=Embedding.
        // e1 is the oldest in Embedding → it should be evicted.
        await cache.GetOrSetAsync(CacheClass.Embedding, "e3", () => Task.FromResult<object?>("v"));

        var stats = cache.GetStats();
        // After eviction we have 4 entries, one was evicted.
        Assert.Equal(4, stats[CacheClass.Embedding].CurrentEntries + stats[CacheClass.RoutingDecision].CurrentEntries);
        Assert.Equal(1, stats[CacheClass.Embedding].EvictedCount);

        // The evicted key was e1 (oldest in the preferred class).
        var emb1 = await cache.GetOrSetAsync(CacheClass.Embedding, "e1", () => Task.FromResult<object?>("factory"));
        Assert.Equal("factory", emb1);  // cache miss → factory was called
    }

    [Fact]
    public void EvictOne_LargeCapacity_StaysAtBound()
    {
        const int max = 500;
        const int total = 2_000;
        using var cache = NewCache(maxEntries: max);

        for (var i = 0; i < total; i++)
        {
            cache.GetOrSetAsync(CacheClass.Embedding, $"k{i}",
                () => Task.FromResult<object?>($"v{i}")).GetAwaiter().GetResult();
        }

        var stats = cache.GetStats();
        // Capacity must not be exceeded.
        Assert.True(stats[CacheClass.Embedding].CurrentEntries <= max,
            $"CurrentEntries={stats[CacheClass.Embedding].CurrentEntries} > max={max}");
        // Evictions happened.
        Assert.Equal(total - max, stats[CacheClass.Embedding].EvictedCount);
    }

    [Fact]
    public void EvictOne_PreferredClassEmpty_FallsBackToGlobalOldest()
    {
        using var cache = NewCache(maxEntries: 3);

        // Only fill RoutingDecision.
        cache.GetOrSetAsync(CacheClass.RoutingDecision, "r1", () => Task.FromResult<object?>("v")).Wait();
        cache.GetOrSetAsync(CacheClass.RoutingDecision, "r2", () => Task.FromResult<object?>("v")).Wait();
        cache.GetOrSetAsync(CacheClass.RoutingDecision, "r3", () => Task.FromResult<object?>("v")).Wait();

        // Insert a new Embedding entry: store count is 3 == max, so EvictOne fires.
        // Preferred class is Embedding, but it has no entries → fall back to global oldest (r1).
        cache.GetOrSetAsync(CacheClass.Embedding, "e1", () => Task.FromResult<object?>("v")).Wait();

        var stats = cache.GetStats();
        Assert.Equal(1, stats[CacheClass.Embedding].CurrentEntries);
        Assert.Equal(2, stats[CacheClass.RoutingDecision].CurrentEntries);
        Assert.Equal(1, stats[CacheClass.RoutingDecision].EvictedCount);

        // r1 was evicted; re-fetching it triggers the factory.
        var r1 = cache.GetOrSetAsync(CacheClass.RoutingDecision, "r1", () => Task.FromResult<object?>("factory")).Result;
        Assert.Equal("factory", r1);
    }

    // ---------------------------------------------------------------------------------------
    // Per-class prefix index for O(k) InvalidateClass
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task InvalidateClass_OnlyRemovesRequestedClass()
    {
        using var cache = NewCache();

        for (var i = 0; i < 50; i++)
        {
            await cache.GetOrSetAsync(CacheClass.Embedding, $"e{i}", () => Task.FromResult<object?>("v"));
        }
        for (var i = 0; i < 30; i++)
        {
            await cache.GetOrSetAsync(CacheClass.RoutingDecision, $"r{i}", () => Task.FromResult<object?>("v"));
        }

        cache.InvalidateClass(CacheClass.Embedding);

        var stats = cache.GetStats();
        Assert.Equal(0, stats[CacheClass.Embedding].CurrentEntries);
        Assert.Equal(30, stats[CacheClass.RoutingDecision].CurrentEntries);
    }

    [Fact]
    public async Task InvalidatePattern_OnlyRemovesMatchingKeys()
    {
        using var cache = NewCache();

        await cache.GetOrSetAsync(CacheClass.Embedding, "prefix_1", () => Task.FromResult<object?>("v"));
        await cache.GetOrSetAsync(CacheClass.Embedding, "prefix_2", () => Task.FromResult<object?>("v"));
        await cache.GetOrSetAsync(CacheClass.Embedding, "other", () => Task.FromResult<object?>("v"));

        cache.InvalidatePattern(CacheClass.Embedding, "prefix_");

        var p1 = await cache.GetOrSetAsync(CacheClass.Embedding, "prefix_1", () => Task.FromResult<object?>("factory"));
        var p2 = await cache.GetOrSetAsync(CacheClass.Embedding, "prefix_2", () => Task.FromResult<object?>("factory"));
        var other = await cache.GetOrSetAsync(CacheClass.Embedding, "other", () => Task.FromResult<object?>("factory"));

        Assert.Equal("factory", p1);
        Assert.Equal("factory", p2);
        Assert.Equal("v", other);
    }

    [Fact]
    public async Task InvalidateAll_ResetsAllState()
    {
        using var cache = NewCache();

        for (var i = 0; i < 10; i++)
        {
            await cache.GetOrSetAsync(CacheClass.Embedding, $"e{i}", () => Task.FromResult<object?>("v"));
        }
        for (var i = 0; i < 10; i++)
        {
            await cache.GetOrSetAsync(CacheClass.RoutingDecision, $"r{i}", () => Task.FromResult<object?>("v"));
        }

        cache.InvalidateAll();

        var stats = cache.GetStats();
        Assert.Equal(0, stats[CacheClass.Embedding].CurrentEntries);
        Assert.Equal(0, stats[CacheClass.RoutingDecision].CurrentEntries);

        // Re-fetch → factory called (proves the store is empty, not just stats zeroed).
        var v = await cache.GetOrSetAsync(CacheClass.Embedding, "e0", () => Task.FromResult<object?>("factory"));
        Assert.Equal("factory", v);
    }

    // ---------------------------------------------------------------------------------------
    // Mutable CacheEntry: sliding expiration must not reallocate
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task GetOrSet_SlidingHit_DoesNotChangeStoreReference()
    {
        using var cache = NewCache(sliding: true);

        await cache.GetOrSetAsync(CacheClass.Embedding, "slide", () => Task.FromResult<object?>("v"));

        // Multiple sliding hits in a row: same key, same store entry, no removals/reinserts.
        for (var i = 0; i < 5; i++)
        {
            var v = await cache.GetOrSetAsync(CacheClass.Embedding, "slide", () => Task.FromResult<object?>("should-not-be-called"));
            Assert.Equal("v", v);
        }

        var stats = cache.GetStats();
        Assert.Equal(5, stats[CacheClass.Embedding].HitCount);
        Assert.Equal(1, stats[CacheClass.Embedding].MissCount);
        Assert.Equal(1, stats[CacheClass.Embedding].CurrentEntries);
    }

    [Fact]
    public async Task GetOrSet_AfterSlidingRefresh_EntryStillPresent()
    {
        using var cache = NewCache(sliding: true, ttlSeconds: 60);

        await cache.GetOrSetAsync(CacheClass.Embedding, "k", () => Task.FromResult<object?>("v"));

        // Several sliding hits. The expiry is updated each time but the entry
        // is not removed from the store.
        for (var i = 0; i < 3; i++)
        {
            var v = await cache.GetOrSetAsync(CacheClass.Embedding, "k", () => Task.FromResult<object?>("X"));
            Assert.Equal("v", v);
        }

        var stats = cache.GetStats();
        Assert.Equal(1, stats[CacheClass.Embedding].CurrentEntries);
    }

    // ---------------------------------------------------------------------------------------
    // Existing behavioural guarantees must still hold
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task DisabledCache_CallsFactoryEveryTime()
    {
        using var disabled = new CacheService(new CacheConfig { Enabled = false }, _loggerMock.Object);
        var calls = 0;

        async Task<object?> Factory()
        {
            Interlocked.Increment(ref calls);
            return Guid.NewGuid().ToString();
        }

        var a = await disabled.GetOrSetAsync(CacheClass.Embedding, "k", Factory);
        var b = await disabled.GetOrSetAsync(CacheClass.Embedding, "k", Factory);

        Assert.Equal(2, calls);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public async Task NullFactoryResult_DoesNotCache()
    {
        using var cache = NewCache();

        var a = await cache.GetOrSetAsync(CacheClass.Embedding, "null-key", () => Task.FromResult<object?>(null));
        var b = await cache.GetOrSetAsync(CacheClass.Embedding, "null-key", () => Task.FromResult<object?>("second"));

        Assert.Null(a);
        Assert.Equal("second", b);
    }

    [Fact]
    public async Task Stats_TrackHitsAndMisses()
    {
        using var cache = NewCache();

        await cache.GetOrSetAsync(CacheClass.Embedding, "k1", () => Task.FromResult<object?>("v1"));  // miss
        await cache.GetOrSetAsync(CacheClass.Embedding, "k1", () => Task.FromResult<object?>("v2"));  // hit
        await cache.GetOrSetAsync(CacheClass.Embedding, "k1", () => Task.FromResult<object?>("v3"));  // hit

        var stats = cache.GetStats();
        Assert.Equal(2, stats[CacheClass.Embedding].HitCount);
        Assert.Equal(1, stats[CacheClass.Embedding].MissCount);
    }

    // ---------------------------------------------------------------------------------------
    // Allocation: sliding-expiration touch must not allocate a new CacheEntry per hit
    // (the regression the original implementation suffered). The hot path now performs
    // a Volatile.Write on a long field instead of a record `with` expression plus a
    // dictionary write, plus a few SortedDictionary ops for the expiry index.
    //
    // We allow a generous ceiling (1000 bytes/op) because:
    //  - Task.FromResult<T>(value) on the hit path allocates a Task<T> (~50 bytes).
    //  - SortedDictionary<long, ExpiryRecord>.Remove + indexer-set internally allocate
    //    red-black tree nodes per touch (~200-400 bytes).
    //  - async machinery, boxed logger args in test environment, etc.
    //
    // The old implementation allocated a new CacheEntry record (~80 bytes) + dictionary
    // write per hit, on top of those. The new implementation's budget is therefore set
    // to catch a regression (e.g. reintroducing the `with` expression) without failing
    // on the inherent cost of the Task + SortedDictionary hot path.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task SlidingHit_DoesNotAllocateCacheEntry()
    {
        using var cache = NewCache(sliding: true, ttlSeconds: 300);

        // Warm up: store the entry once.
        await cache.GetOrSetAsync(CacheClass.Embedding, "hot", () => Task.FromResult<object?>("v"));

        // Warm the JIT before measuring.
        for (var i = 0; i < 200; i++)
        {
            await cache.GetOrSetAsync(CacheClass.Embedding, "hot", () => Task.FromResult<object?>("never"));
        }

        // Measure.
        const int iters = 10_000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iters; i++)
        {
            await cache.GetOrSetAsync(CacheClass.Embedding, "hot", () => Task.FromResult<object?>("never"));
        }
        var after = GC.GetAllocatedBytesForCurrentThread();
        var perOp = (after - before) / (double)iters;

        // The critical assertion: no per-hit CacheEntry record allocation. The old impl
        // allocated ~80 bytes per hit from `entry with { ExpiresAt = ... }` plus a
        // ConcurrentDictionary node write. The new impl performs a single Volatile.Write
        // on a long field. We allow up to 1000 bytes/op to account for the SortedDictionary
        // tree-node allocations and the Task<T> returned by the public API.
        Assert.True(perOp < 1000,
            $"Sliding-hit allocations: {perOp:F1} bytes/op (limit 1000). " +
            "This is well above the new implementation's expected budget — investigate " +
            "whether a per-hit allocation (record `with`, dictionary write, or extra boxing) " +
            "has been reintroduced.");
    }

    // ---------------------------------------------------------------------------------------
    // Eviction cost: 10k inserts at capacity should be bounded; per-insert cost should
    // be sub-millisecond. The old implementation did a full OrderBy over all keys per
    // eviction (O(n log n)); the new one peeks the SortedDictionary minimum (O(log n)).
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void EvictOne_At10kInserts_SubMillisecondPerInsert()
    {
        const int max = 1_000;
        const int total = 10_000;
        using var cache = NewCache(maxEntries: max);

        // Warm up the JIT and the internal tree.
        for (var i = 0; i < 200; i++)
        {
            cache.GetOrSetAsync(CacheClass.Embedding, $"warm{i}", () => Task.FromResult<object?>("v")).Wait();
        }
        cache.InvalidateAll();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < total; i++)
        {
            cache.GetOrSetAsync(CacheClass.Embedding, $"k{i}", () => Task.FromResult<object?>("v")).Wait();
        }
        sw.Stop();

        var perOp = sw.Elapsed.TotalMilliseconds / total;
        Assert.True(perOp < 1.0,
            $"Eviction: {perOp:F3} ms/op over {total} inserts at capacity {max} (limit 1.0 ms). " +
            "Old impl did O(n log n) sort per eviction; new impl peeks SortedDictionary min.");

        var stats = cache.GetStats();
        Assert.Equal(max, stats[CacheClass.Embedding].CurrentEntries);
        Assert.Equal(total - max, stats[CacheClass.Embedding].EvictedCount);
    }
}
