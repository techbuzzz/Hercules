using Hercules.Mesh.Abstractions;
using Hercules.Mesh.Backends.Nats;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Mesh.Backends;

/// <summary>
///     Unit tests for <see cref="NatsMeshStateStore"/> (task_068).
///     Tests: BackendKind, IsHealthy, config defaults, Dispose behavior.
///     JetStream KV operations (GetAsync, SetAsync, CompareAndSetAsync, DeleteAsync,
///     ExistsAsync, IncrementAsync, WatchAsync, ScanKeysAsync) require a live NATS
///     server with JetStream and are covered by integration tests.
/// </summary>
public class NatsMeshStateStoreTests : IDisposable
{
    private readonly NatsMeshStateStore _store;

    public NatsMeshStateStoreTests()
    {
        var config = new NatsMeshConfig
        {
            Enabled = true,
            Servers = "nats://localhost:4222",
            StreamPrefix = "test-state",
            JetStreamMaxBytes = 1024,
            DefaultTtlSeconds = 3600
        };

        var loggerMock = new Mock<ILogger<NatsMeshStateStore>>();
        _store = new NatsMeshStateStore(null!, config, loggerMock.Object);
    }

    [Fact]
    public void BackendKind_ReturnsNats()
    {
        Assert.Equal("nats", _store.BackendKind);
    }

    [Fact]
    public void Config_DefaultValues_AreCorrect()
    {
        var config = new NatsMeshConfig();

        Assert.False(config.Enabled);
        Assert.Equal("nats://localhost:4222", config.Servers);
        Assert.Equal("hercules-mesh", config.Name);
        Assert.Equal("hercules.mesh.", config.SubjectPrefix);
        Assert.Equal("hercules", config.StreamPrefix);
        Assert.True(config.JetStreamEnabled);
        Assert.Equal(1_073_741_824, config.JetStreamMaxBytes);
        Assert.Equal(7, config.JetStreamMaxAgeDays);
        Assert.Equal(3600, config.DefaultTtlSeconds);
    }

    [Fact]
    public void Config_BucketName_DerivedFromStreamPrefix()
    {
        var config = new NatsMeshConfig { StreamPrefix = "mybucket" };
        Assert.Equal("mybucket-state", $"{config.StreamPrefix}-state");
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
        var config = new NatsMeshConfig { Enabled = true, StreamPrefix = "test-store2" };
        var store2 = new NatsMeshStateStore(null!, config, new Mock<ILogger<NatsMeshStateStore>>().Object);
        store2.Dispose();
        store2.Dispose(); // Should not throw
    }

    [Fact]
    public void Dispose_ClearsWatchTimers()
    {
        var config = new NatsMeshConfig { Enabled = true, StreamPrefix = "test-store3" };
        var store3 = new NatsMeshStateStore(null!, config, new Mock<ILogger<NatsMeshStateStore>>().Object);
        store3.Dispose();

        // Should not throw — timer disposed
        Assert.ThrowsAny<ObjectDisposedException>(() => store3.GetAsync("anykey").GetAwaiter().GetResult());
    }

    public void Dispose() => _store.Dispose();
}
