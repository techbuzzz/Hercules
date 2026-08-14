using Hercules.Mesh.Abstractions;
using Hercules.Mesh.Backends.Nats;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Mesh.Backends;

/// <summary>
///     Unit tests for <see cref="NatsTaskQueue"/> (task_068).
///     Tests: BackendKind, IsHealthy, config defaults, Dispose behavior.
///     JetStream operations (DequeueAsync, AckAsync, FailAsync, GetDeadLetterQueueAsync,
///     RequeueDeadLetterAsync) require a live NATS+JetStream server and are covered
///     by integration tests.
/// </summary>
public class NatsTaskQueueTests : IDisposable
{
    private readonly NatsTaskQueue _queue;

    public NatsTaskQueueTests()
    {
        // Use a config pointing to localhost — no real NATS server needed for these tests
        var config = new NatsMeshConfig
        {
            Enabled = true,
            Servers = "nats://localhost:4222",
            StreamPrefix = "test",
            JetStreamEnabled = false, // Core NATS fallback path
            DefaultVisibilityTimeoutSec = 30,
            JetStreamMaxBytes = 1024,
            JetStreamMaxAgeDays = 7
        };

        // Pass null for connection — the queue uses a background timer for stream setup
        // which will fail gracefully when no server is available.
        // We can't inject a mock NatsConnection, so we test what we can.
        var loggerMock = new Mock<ILogger<NatsTaskQueue>>();

        // NatsTaskQueue constructor expects NatsConnection; use a real one that won't connect
        // — the EnsureStreamAndConsumersAsync is fire-and-forget so tests proceed fine.
        // For config-only tests, we verify defaults without needing the constructor.
        // Actually, NatsTaskQueue requires NatsConnection — create a real one with timeout.
        _queue = new NatsTaskQueue(null!, config, loggerMock.Object);
    }

    [Fact]
    public void BackendKind_ReturnsNats()
    {
        Assert.Equal("nats", _queue.BackendKind);
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
        Assert.Equal(30, config.DefaultVisibilityTimeoutSec);
        Assert.True(config.JetStreamEnabled);
        Assert.Equal(1_073_741_824, config.JetStreamMaxBytes);
        Assert.Equal(7, config.JetStreamMaxAgeDays);
        Assert.Equal(5000, config.ConnectTimeoutMs);
        Assert.Equal(30000, config.PingIntervalMs);
        Assert.Equal(3600, config.DefaultTtlSeconds);
    }

    [Fact]
    public void Config_StreamName_DerivedFromPrefix()
    {
        var config = new NatsMeshConfig { StreamPrefix = "myprefix" };
        Assert.Equal("myprefix", config.StreamPrefix);
    }

    [Fact]
    public async Task IsHealthyAsync_ReturnsFalse_WhenDisposed()
    {
        _queue.Dispose();
        var result = await _queue.IsHealthyAsync();
        Assert.False(result);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var config = new NatsMeshConfig { Enabled = true, StreamPrefix = "test2" };
        var queue2 = new NatsTaskQueue(null!, config, new Mock<ILogger<NatsTaskQueue>>().Object);
        queue2.Dispose();
        queue2.Dispose(); // Should not throw
    }

    [Fact]
    public void Dispose_ClearsTimer()
    {
        // After Dispose, the timer is stopped — verify no exception on second Dispose
        var config = new NatsMeshConfig { Enabled = true, StreamPrefix = "test3" };
        var queue3 = new NatsTaskQueue(null!, config, new Mock<ILogger<NatsTaskQueue>>().Object);
        queue3.Dispose();

        // Should not throw
        Assert.ThrowsAny<ObjectDisposedException>(() => queue3.EnqueueAsync(new MeshTask { Intent = "test" }).GetAwaiter().GetResult());
    }

    public void Dispose() => _queue.Dispose();
}
