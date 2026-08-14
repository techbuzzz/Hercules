using Hercules.Mesh;
using Hercules.Mesh.Abstractions;
using Hercules.Mesh.Backends.Nats;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Mesh.Backends;

/// <summary>
///     Unit tests for <see cref="NatsMeshBus"/> (task_068).
///     Tests: BackendKind, IsHealthy, local subscribe/unsubscribe/publish, config defaults.
///     NATS-connection-dependent tests (PublishAsync remote, RequestReplyAsync, IsHealthy ping)
///     require a live NATS server and are covered by integration tests.
/// </summary>
public class NatsMeshBusTests : IDisposable
{
    private readonly NatsMeshBus _bus;

    public NatsMeshBusTests()
    {
        // Use a config pointing to localhost (will not connect — local fallback tested)
        var config = new NatsMeshConfig
        {
            Enabled = true,
            Servers = "nats://localhost:4222",
            Name = "test-hercules-bus",
            SubjectPrefix = "test.mesh.",
            StreamPrefix = "test"
        };

        var loggerMock = new Mock<ILogger<NatsMeshBus>>();
        _bus = new NatsMeshBus(config, loggerMock.Object);
    }

    [Fact]
    public void BackendKind_ReturnsNats()
    {
        Assert.Equal("nats", _bus.BackendKind);
    }

    [Fact]
    public async Task IsHealthyAsync_ReturnsFalse_WhenDisposed()
    {
        _bus.Dispose();
        var result = await _bus.IsHealthyAsync();
        Assert.False(result);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsDisposable()
    {
        var sub = await _bus.SubscribeAsync("local/topic", (env, _) => Task.CompletedTask);
        Assert.NotNull(sub);
        sub.Dispose(); // Should not throw
    }

    [Fact]
    public async Task SubscribeAsync_RemovesHandler_OnDispose()
    {
        var callCount = 0;
        var sub = await _bus.SubscribeAsync("remove/topic", (env, _) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.CompletedTask;
        });

        sub.Dispose();

        // Publish to the same topic — since no NATS connection, falls back to local publish
        var envelope = new IntentEnvelope { RequestId = "r", Intent = "remove" };
        await _bus.PublishAsync("remove/topic", envelope);
        await Task.Delay(200);

        Assert.Equal(0, callCount);
    }

    [Fact]
    public async Task PublishAsync_DeliversLocally_WhenNoConnection()
    {
        var received = new List<IntentEnvelope>();
        var sub = await _bus.SubscribeAsync("local/deliver", (env, _) =>
        {
            lock (received) { received.Add(env); }
            return Task.CompletedTask;
        });

        var envelope = new IntentEnvelope
        {
            RequestId = "local-req-1",
            Intent = "local.deliver",
            Payload = "{\"msg\":\"hello\"}"
        };

        await _bus.PublishAsync("local/deliver", envelope);
        await Task.Delay(200);

        Assert.Single(received);
        Assert.Equal("local-req-1", received[0].RequestId);
        Assert.Equal("local.deliver", received[0].Intent);

        sub.Dispose();
    }

    [Fact]
    public async Task PublishAsync_ThrowsObjectDisposed_WhenDisposed()
    {
        _bus.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            _bus.PublishAsync("any/topic", new IntentEnvelope { Intent = "test" }));
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsArgumentException_WhenTopicEmpty()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _bus.SubscribeAsync("", (env, _) => Task.CompletedTask));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var config = new NatsMeshConfig { Enabled = true, Servers = "nats://localhost:4222" };
        var bus2 = new NatsMeshBus(config, new Mock<ILogger<NatsMeshBus>>().Object);
        bus2.Dispose();
        bus2.Dispose(); // Should not throw
    }

    [Fact]
    public async Task RequestReplyAsync_ReturnsTimedOut_WhenNoConnection()
    {
        // Without a real NATS server, RequestReplyAsync should return TimedOut response
        var envelope = new IntentEnvelope
        {
            RequestId = "req-timeout-1",
            Intent = "test.timeout",
            Payload = "{}"
        };

        var result = await _bus.RequestReplyAsync("any-agent", envelope, default, TimeSpan.FromMilliseconds(100));

        Assert.False(result.IsSuccess);
        Assert.Contains("timed", result.Error?.ToLowerInvariant() ?? "");
    }

    public void Dispose() => _bus.Dispose();
}
