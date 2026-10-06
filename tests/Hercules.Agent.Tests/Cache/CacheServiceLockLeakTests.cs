using Hercules.Cache;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Cache;

/// <summary>
///     R9 regression test: <c>CacheService._locks</c> is a
///     <c>ConcurrentDictionary&lt;string, SemaphoreSlim&gt;</c> populated on every cache
///     miss. It previously had no removal path at all, so every distinct key ever missed
///     leaked a semaphore for the lifetime of the process — an unbounded leak in a
///     long-running agent.
/// </summary>
public class CacheServiceLockLeakTests
{
    private static CacheService Create(int maxEntries = 8)
    {
        var config = new CacheConfig
        {
            MaxEntries = maxEntries,
            CleanupIntervalSeconds = 3600, // keep the background sweep out of the way
            SlidingExpiration = false,
        };

        return new CacheService(config, NullLogger<CacheService>.Instance);
    }

    [Fact]
    public async Task WarmKey_CreatesALock()
    {
        using var cache = Create();

        // Warm one key so the map is non-empty to begin with.
        await cache.GetOrSetAsync(CacheClass.Embedding, "warm", () => Task.FromResult<object?>("v"));
        var baseline = cache.LockCount;
        Assert.True(baseline >= 1);
    }

        [Fact]
    public async Task LockMap_DoesNotGrowWithoutBound()
    {
        using var cache = Create();

        // Warm one key so the map is non-empty to begin with.
        await cache.GetOrSetAsync(CacheClass.Embedding, "warm", () => Task.FromResult<object?>("v"));
        var baseline = cache.LockCount;
        Assert.True(baseline >= 1);

        // 500 distinct keys — far more than MaxEntries, because capacity bounds *entries*,
        // not the number of keys that ever missed.
        for (var i = 0; i < 500; i++)
        {
            await cache.GetOrSetAsync(CacheClass.Embedding, $"key-{i}", () => Task.FromResult<object?>($"v{i}"));
        }

        Assert.Equal(501, cache.LockCount);

        // Sweep with a zero threshold so every lock qualifies as idle.
        var trimmed = cache.TrimIdleLocks(TimeSpan.FromTicks(1));

        Assert.Equal(501, trimmed);
        Assert.Equal(0, cache.LockCount);
    }

    [Fact]
    public async Task TrimIdleLocks_KeepsRecentlyUsedLocks()
    {
        using var cache = Create();

        await cache.GetOrSetAsync(CacheClass.Embedding, "hot", () => Task.FromResult<object?>("v"));
        Assert.Equal(1, cache.LockCount);

        // A generous threshold means the just-used lock is NOT idle yet.
        var trimmed = cache.TrimIdleLocks(TimeSpan.FromHours(1));

        Assert.Equal(0, trimmed);
        Assert.Equal(1, cache.LockCount);
    }

    [Fact]
    public async Task TrimIdleLocks_NonPositiveThreshold_IsNoOp()
    {
        using var cache = Create();

        await cache.GetOrSetAsync(CacheClass.Embedding, "k", () => Task.FromResult<object?>("v"));

        Assert.Equal(0, cache.TrimIdleLocks(TimeSpan.Zero));
        Assert.Equal(0, cache.TrimIdleLocks(TimeSpan.FromSeconds(-1)));
        Assert.Equal(1, cache.LockCount);
    }

    [Fact]
    public async Task CacheStillFunctionsAfterTrim()
    {
        using var cache = Create();

        await cache.GetOrSetAsync(CacheClass.Embedding, "a", () => Task.FromResult<object?>("1"));
        cache.TrimIdleLocks(TimeSpan.FromTicks(1));
        Assert.Equal(0, cache.LockCount);

        // A cache HIT does not take a stampede lock, so no new entry appears.
        var hit = await cache.GetOrSetAsync(CacheClass.Embedding, "a", () => Task.FromResult<object?>("2"));
        Assert.Equal("1", hit);
        Assert.Equal(0, cache.LockCount);

        // A MISS must transparently recreate the lock and store the new value.
        var miss = await cache.GetOrSetAsync(CacheClass.Embedding, "b", () => Task.FromResult<object?>("9"));
        Assert.Equal("9", miss);
        Assert.Equal(1, cache.LockCount);
    }
}