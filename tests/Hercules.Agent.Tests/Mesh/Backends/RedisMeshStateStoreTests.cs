using System.Net;
using Hercules.Mesh.Abstractions;
using Hercules.Mesh.Backends.Redis;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace Hercules.Agent.Tests.Mesh.Backends;

/// <summary>
///     Unit tests for <see cref="RedisMeshStateStore"/> (task_067).
///     Uses Moq to mock IConnectionMultiplexer and IDatabase.
/// </summary>
public class RedisMeshStateStoreTests : IDisposable
{
    private readonly Mock<IConnectionMultiplexer> _redisMock;
    private readonly Mock<IDatabase> _dbMock;
    private readonly RedisMeshConfig _config;
    private readonly RedisMeshStateStore _store;

    public RedisMeshStateStoreTests()
    {
        _redisMock = new Mock<IConnectionMultiplexer>();
        _dbMock = new Mock<IDatabase>();

        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_dbMock.Object);
        _redisMock.Setup(r => r.IsConnected).Returns(true);
        _redisMock.Setup(r => r.GetEndPoints(It.IsAny<bool>())).Returns(new EndPoint[] { new DnsEndPoint("localhost", 6379) });

        _config = new RedisMeshConfig
        {
            Enabled = true,
            KeyPrefix = "test:mesh:",
            DefaultTtlSeconds = 3600,
            WatchPollingIntervalMs = 100
        };

        var loggerMock = new Mock<ILogger<RedisMeshStateStore>>();
        _store = new RedisMeshStateStore(_redisMock.Object, _config, loggerMock.Object);
    }

    [Fact]
    public void BackendKind_ReturnsRedis()
    {
        Assert.Equal("redis", _store.BackendKind);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenKeyNotFound()
    {
        _dbMock.Setup(d => d.HashGetAllAsync(
                It.Is<RedisKey>(k => k.ToString() == "test:mesh:mykey"),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<HashEntry>());

        var result = await _store.GetAsync("mykey");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_ReturnsStoredValue_WhenKeyExists()
    {
        var hashEntries = new HashEntry[]
        {
            new("data", "{\"value\":42}"),
            new("version", "v123"),
            new("createdAt", "1752500000000"),
            new("updatedAt", "1752500001000"),
            new("expiresAt", "0"),
            new("lastWriterAgentId", ""),
        };

        _dbMock.Setup(d => d.HashGetAllAsync(
                It.Is<RedisKey>(k => k.ToString() == "test:mesh:statekey"),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(hashEntries);

        var result = await _store.GetAsync("statekey");
        Assert.NotNull(result);
        Assert.Equal("{\"value\":42}", result.Data);
        Assert.Equal("v123", result.Version);
    }

    [Fact]
    public async Task SetAsync_StoresValueWithVersionAndTtl()
    {
        HashEntry[]? capturedEntries = null;

        _dbMock.Setup(d => d.HashSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<HashEntry[]>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisKey, HashEntry[], CommandFlags>((_, entries, _) => capturedEntries = entries)
            .Returns(Task.CompletedTask);

        var value = new StoredValue
        {
            Data = "test-data",
            Version = "v1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _store.SetAsync("setkey", value, TimeSpan.FromSeconds(60));

        Assert.NotNull(capturedEntries);
        Assert.Contains(capturedEntries, e => e.Name == "data" && e.Value == "test-data");
        Assert.Contains(capturedEntries, e => e.Name == "version");
        Assert.Contains(capturedEntries, e => e.Name == "createdAt");
        Assert.Contains(capturedEntries, e => e.Name == "updatedAt");
        Assert.Contains(capturedEntries, e => e.Name == "expiresAt");
    }

    [Fact]
    public async Task DeleteAsync_ReturnsTrue_WhenKeyDeleted()
    {
        _dbMock.Setup(d => d.KeyDeleteAsync(
                It.Is<RedisKey>(k => k.ToString() == "test:mesh:delkey"),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var result = await _store.DeleteAsync("delkey");
        Assert.True(result);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrue_WhenKeyExists()
    {
        _dbMock.Setup(d => d.KeyExistsAsync(
                It.Is<RedisKey>(k => k.ToString() == "test:mesh:existskey"),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var result = await _store.ExistsAsync("existskey");
        Assert.True(result);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenKeyDoesNotExist()
    {
        _dbMock.Setup(d => d.KeyExistsAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        var result = await _store.ExistsAsync("nonexistent");
        Assert.False(result);
    }

    [Fact]
    public async Task IncrementAsync_ReturnsIncrementedValue()
    {
        _dbMock.Setup(d => d.StringIncrementAsync(
                It.Is<RedisKey>(k => k.ToString() == "test:mesh:counter"),
                1,
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(5);

        _dbMock.Setup(d => d.HashGetAllAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(new HashEntry[]
            {
                new("data", "5"),
                new("version", "v1"),
                new("createdAt", "1752500000000"),
                new("updatedAt", "1752500001000"),
                new("expiresAt", "0"),
                new("lastWriterAgentId", ""),
            });

        var result = await _store.IncrementAsync("counter");
        Assert.Equal(5, result);
    }

    [Fact]
    public async Task WatchAsync_ReturnsDisposable()
    {
        var sub = await _store.WatchAsync("watchkey", (v, _) => Task.CompletedTask);
        Assert.NotNull(sub);
        sub.Dispose();
    }

    [Fact]
    public async Task IsHealthyAsync_ReturnsTrue_WhenPingSucceeds()
    {
        _dbMock.Setup(d => d.PingAsync(It.IsAny<CommandFlags>()))
            .Returns(Task.FromResult(TimeSpan.FromMilliseconds(1)));

        var result = await _store.IsHealthyAsync();
        Assert.True(result);
    }

    [Fact]
    public async Task IsHealthyAsync_ReturnsFalse_WhenDisposed()
    {
        _store.Dispose();
        var result = await _store.IsHealthyAsync();
        Assert.False(result);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var store2 = new RedisMeshStateStore(_redisMock.Object, _config,
            new Mock<ILogger<RedisMeshStateStore>>().Object);
        store2.Dispose();
        store2.Dispose(); // Should not throw
    }

    public void Dispose() => _store.Dispose();
}
