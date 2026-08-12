using Hercules.Cache;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Cache;

public class CacheServiceTests : IDisposable
{
    private readonly CacheService _cache;
    private readonly Mock<ILogger<CacheService>> _loggerMock;

    public CacheServiceTests()
    {
        _loggerMock = new Mock<ILogger<CacheService>>();
        var config = new CacheConfig
        {
            Enabled = true,
            DefaultTtlSeconds = 60,
            MaxEntries = 100,
            CleanupIntervalSeconds = 3600,
            SlidingExpiration = true,
        };
        _cache = new CacheService(config, _loggerMock.Object);
    }

    public void Dispose() => _cache.Dispose();

    [Fact]
    public async Task GetOrSetAsync_Miss_ReturnsFactoryValue()
    {
        var result = await _cache.GetOrSetAsync(
            CacheClass.Embedding,
            "test-key",
            () => Task.FromResult<object?>(new float[] { 1f, 2f }));

        Assert.NotNull(result);
        Assert.Equal(new float[] { 1f, 2f }, result);
    }

    [Fact]
    public async Task GetOrSetAsync_Hit_ReturnsCachedValue()
    {
        await _cache.GetOrSetAsync(CacheClass.Embedding, "hit-key",
            () => Task.FromResult<object?>("original"));

        var result = await _cache.GetOrSetAsync(CacheClass.Embedding, "hit-key",
            () => Task.FromResult<object?>("should-not-be-called"));

        Assert.Equal("original", result);
    }

    [Fact]
    public async Task GetOrSetAsync_DifferentClasses_AreSeparate()
    {
        await _cache.GetOrSetAsync(CacheClass.Embedding, "key", () => Task.FromResult<object?>("emb"));
        await _cache.GetOrSetAsync(CacheClass.RoutingDecision, "key", () => Task.FromResult<object?>("route"));

        var emb = await _cache.GetOrSetAsync(CacheClass.Embedding, "key", () => Task.FromResult<object?>("X"));
        var route = await _cache.GetOrSetAsync(CacheClass.RoutingDecision, "key", () => Task.FromResult<object?>("X"));

        Assert.Equal("emb", emb);
        Assert.Equal("route", route);
    }

    [Fact]
    public async Task GetOrSetAsync_NullFactory_ReturnsNull()
    {
        var result = await _cache.GetOrSetAsync(
            CacheClass.Embedding, "null-factory",
            () => Task.FromResult<object?>(null));

        Assert.Null(result);
    }

    [Fact]
    public void Invalidate_SingleKey_RemovesEntry()
    {
        _cache.GetOrSetAsync(CacheClass.Embedding, "to-remove",
            () => Task.FromResult<object?>("val")).Wait();

        _cache.Invalidate(CacheClass.Embedding, "to-remove");

        var result = _cache.GetOrSetAsync(CacheClass.Embedding, "to-remove",
            () => Task.FromResult<object?>("factory")).Result;

        Assert.Equal("factory", result);
    }

    [Fact]
    public void Invalidate_Class_RemovesAllInClass()
    {
        _cache.GetOrSetAsync(CacheClass.Embedding, "a", () => Task.FromResult<object?>("v1")).Wait();
        _cache.GetOrSetAsync(CacheClass.Embedding, "b", () => Task.FromResult<object?>("v2")).Wait();
        _cache.GetOrSetAsync(CacheClass.RoutingDecision, "c", () => Task.FromResult<object?>("v3")).Wait();

        _cache.InvalidateClass(CacheClass.Embedding);

        var emb1 = _cache.GetOrSetAsync(CacheClass.Embedding, "a", () => Task.FromResult<object?>("factory")).Result;
        var emb2 = _cache.GetOrSetAsync(CacheClass.Embedding, "b", () => Task.FromResult<object?>("factory")).Result;
        var route = _cache.GetOrSetAsync(CacheClass.RoutingDecision, "c", () => Task.FromResult<object?>("factory")).Result;

        Assert.Equal("factory", emb1);
        Assert.Equal("factory", emb2);
        Assert.Equal("v3", route);
    }

    [Fact]
    public void InvalidatePattern_RemovesMatchingEntries()
    {
        _cache.GetOrSetAsync(CacheClass.Embedding, "prefix_1", () => Task.FromResult<object?>("v1")).Wait();
        _cache.GetOrSetAsync(CacheClass.Embedding, "prefix_2", () => Task.FromResult<object?>("v2")).Wait();
        _cache.GetOrSetAsync(CacheClass.Embedding, "other", () => Task.FromResult<object?>("v3")).Wait();

        _cache.InvalidatePattern(CacheClass.Embedding, "prefix_");

        var r1 = _cache.GetOrSetAsync(CacheClass.Embedding, "prefix_1", () => Task.FromResult<object?>("factory")).Result;
        var r2 = _cache.GetOrSetAsync(CacheClass.Embedding, "prefix_2", () => Task.FromResult<object?>("factory")).Result;
        var r3 = _cache.GetOrSetAsync(CacheClass.Embedding, "other", () => Task.FromResult<object?>("factory")).Result;

        Assert.Equal("factory", r1);
        Assert.Equal("factory", r2);
        Assert.Equal("v3", r3);
    }

    [Fact]
    public void InvalidateAll_RemovesEverything()
    {
        _cache.GetOrSetAsync(CacheClass.Embedding, "a", () => Task.FromResult<object?>("v1")).Wait();
        _cache.GetOrSetAsync(CacheClass.RoutingDecision, "b", () => Task.FromResult<object?>("v2")).Wait();

        _cache.InvalidateAll();

        var r1 = _cache.GetOrSetAsync(CacheClass.Embedding, "a", () => Task.FromResult<object?>("factory")).Result;
        var r2 = _cache.GetOrSetAsync(CacheClass.RoutingDecision, "b", () => Task.FromResult<object?>("factory")).Result;

        Assert.Equal("factory", r1);
        Assert.Equal("factory", r2);
    }

    [Fact]
    public void GetOrSetAsync_DisabledCache_CallsFactoryEveryTime()
    {
        var config = new CacheConfig { Enabled = false };
        using var disabledCache = new CacheService(config, _loggerMock.Object);

        disabledCache.GetOrSetAsync(CacheClass.Embedding, "k",
            () => Task.FromResult<object?>("v1")).Wait();

        var result = disabledCache.GetOrSetAsync(CacheClass.Embedding, "k",
            () => Task.FromResult<object?>("v2")).Result;

        Assert.Equal("v2", result);
    }

    [Fact]
    public void GetStats_ReturnsStatsPerClass()
    {
        var stats = _cache.GetStats();

        Assert.NotEmpty(stats);
        Assert.Contains(CacheClass.Embedding, stats.Keys);
        Assert.Contains(CacheClass.RoutingDecision, stats.Keys);
        Assert.Contains(CacheClass.LlmCapability, stats.Keys);
    }

    [Fact]
    public void GetStats_TracksHitsAndMisses()
    {
        _cache.GetOrSetAsync(CacheClass.Embedding, "k1", () => Task.FromResult<object?>("v1")).Wait();
        _cache.GetOrSetAsync(CacheClass.Embedding, "k1", () => Task.FromResult<object?>("v2")).Wait();
        _cache.GetOrSetAsync(CacheClass.Embedding, "k1", () => Task.FromResult<object?>("v3")).Wait();

        var stats = _cache.GetStats();
        var embStats = stats[CacheClass.Embedding];

        Assert.Equal(2, embStats.HitCount);
        Assert.Equal(1, embStats.MissCount);
    }

    [Fact]
    public async Task GetOrSetAsync_MaxEntries_EvictsOnCapacity()
    {
        var config = new CacheConfig
        {
            Enabled = true,
            DefaultTtlSeconds = 60,
            MaxEntries = 3,
            CleanupIntervalSeconds = 3600,
            SlidingExpiration = false,
        };
        using var smallCache = new CacheService(config, _loggerMock.Object);

        // Fill to capacity
        smallCache.GetOrSetAsync(CacheClass.Embedding, "k1",
            () => Task.FromResult<object?>("v1")).Wait();
        smallCache.GetOrSetAsync(CacheClass.Embedding, "k2",
            () => Task.FromResult<object?>("v2")).Wait();
        smallCache.GetOrSetAsync(CacheClass.Embedding, "k3",
            () => Task.FromResult<object?>("v3")).Wait();

        // Adding a 4th entry should evict one existing entry
        smallCache.GetOrSetAsync(CacheClass.Embedding, "k4",
            () => Task.FromResult<object?>("v4")).Wait();

        // Verify capacity is maintained (store count should be 3 after eviction)
        var stats = smallCache.GetStats();
        Assert.Equal(3, stats[CacheClass.Embedding].CurrentEntries);
    }

    [Fact]
    public void GetOrSetAsync_NullValue_DoesNotCache()
    {
        _cache.GetOrSetAsync(CacheClass.Embedding, "null-val",
            () => Task.FromResult<object?>(null)).Wait();

        var result = _cache.GetOrSetAsync(CacheClass.Embedding, "null-val",
            () => Task.FromResult<object?>("factory")).Result;

        Assert.Equal("factory", result);
    }

    [Fact]
    public void GetOrSetAsync_SlidingExpiration_RefreshesTtlOnHit()
    {
        var config = new CacheConfig
        {
            Enabled = true,
            DefaultTtlSeconds = 2,
            CleanupIntervalSeconds = 3600,
            SlidingExpiration = true,
        };
        using var slidingCache = new CacheService(config, _loggerMock.Object);

        slidingCache.GetOrSetAsync(CacheClass.Embedding, "slide-key",
            () => Task.FromResult<object?>("original")).Wait();

        // Wait 1.5s — within TTL
        Thread.Sleep(1500);

        // Access: sliding should refresh TTL
        var hit = slidingCache.GetOrSetAsync(CacheClass.Embedding, "slide-key",
            () => Task.FromResult<object?>("factory-after-touch")).Result;

        Assert.Equal("original", hit);
    }

    [Fact]
    public async Task GetOrSetAsync_TtlExpiry_ExpireAfterTtl()
    {
        // TTL = 5 seconds, sliding = false.
        // After 8 seconds, the entry should be treated as expired.
        var config = new CacheConfig
        {
            Enabled = true,
            DefaultTtlSeconds = 5,
            CleanupIntervalSeconds = 3600,
            SlidingExpiration = false,
        };
        using var shortCache = new CacheService(config, _loggerMock.Object);

        // Insert
        await shortCache.GetOrSetAsync(CacheClass.Embedding, "expiry-key",
            () => Task.FromResult<object?>("fresh"));

        // Verify one miss on insert
        Assert.Equal(1, shortCache.GetStats()[CacheClass.Embedding].MissCount);

        // Wait for TTL to definitely expire
        await Task.Delay(8000);

        // After expiry, factory should be called again
        var afterExpiry = await shortCache.GetOrSetAsync(CacheClass.Embedding, "expiry-key",
            () => Task.FromResult<object?>("factory-after-expiry"));

        // Two misses total: one on insert, one on expiry
        var stats = shortCache.GetStats();
        Assert.Equal(2, stats[CacheClass.Embedding].MissCount);
        Assert.Equal("factory-after-expiry", afterExpiry);
    }

    [Fact]
    public async Task GetOrSetAsync_SlidingExpiration_ExtendsExpiry()
    {
        // TTL = 5 seconds, sliding = true.
        // Periodic access should extend TTL so entry survives multiple TTL periods.
        var config = new CacheConfig
        {
            Enabled = true,
            DefaultTtlSeconds = 5,
            CleanupIntervalSeconds = 3600,
            SlidingExpiration = true,
        };
        using var slidingCache = new CacheService(config, _loggerMock.Object);

        // Insert
        await slidingCache.GetOrSetAsync(CacheClass.Embedding, "slide-key",
            () => Task.FromResult<object?>("original"));

        // Touch after 2s (TTL reset to 5s from now = 7s mark)
        await Task.Delay(2000);
        var hit1 = await slidingCache.GetOrSetAsync(CacheClass.Embedding, "slide-key",
            () => Task.FromResult<object?>("factory-touch1"));
        Assert.Equal("original", hit1);
        Assert.Equal(1, slidingCache.GetStats()[CacheClass.Embedding].HitCount);

        // Touch after 3s more (7s from insert, 2s from touch = 9s mark)
        await Task.Delay(3000);
        var hit2 = await slidingCache.GetOrSetAsync(CacheClass.Embedding, "slide-key",
            () => Task.FromResult<object?>("factory-touch2"));
        Assert.Equal("original", hit2);

        // After 6s more (15s from insert, 6s from last touch) — TTL was 5s, expired
        await Task.Delay(6000);
        var afterExpiry = await slidingCache.GetOrSetAsync(CacheClass.Embedding, "slide-key",
            () => Task.FromResult<object?>("factory-after-expiry"));
        Assert.Equal("factory-after-expiry", afterExpiry);
    }

    [Fact]
    public void GetOrSetAsync_SensitivityConfig_TracksPerClass()
    {
        var config = new CacheConfig
        {
            Enabled = true,
            DefaultTtlSeconds = 60,
            CleanupIntervalSeconds = 3600,
            SensitivityByClass = new Dictionary<string, string>
            {
                [nameof(CacheClass.Embedding)] = nameof(SensitivityLevel.Public),
                [nameof(CacheClass.DeterministicToolResult)] = nameof(SensitivityLevel.Sensitive),
            }
        };
        using var sensCache = new CacheService(config, _loggerMock.Object);

        sensCache.GetOrSetAsync(CacheClass.DeterministicToolResult, "sensitive-key",
            () => Task.FromResult<object?>("sensitive-data")).Wait();

        var stats = sensCache.GetStats();
        Assert.True(stats.ContainsKey(CacheClass.DeterministicToolResult));
        // First call is a miss (no entry yet)
        Assert.Equal(1, stats[CacheClass.DeterministicToolResult].MissCount);
        // Embedding was never used → 0
        Assert.Equal(0, stats[CacheClass.Embedding].MissCount);
    }
}
