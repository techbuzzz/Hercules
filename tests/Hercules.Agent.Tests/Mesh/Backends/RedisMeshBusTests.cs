using System.Net;
using Hercules.Mesh;
using Hercules.Mesh.Abstractions;
using Hercules.Mesh.Backends.Redis;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace Hercules.Agent.Tests.Mesh.Backends;

/// <summary>
///     Unit tests for <see cref="RedisMeshBus"/> (task_067).
///     Uses Moq to mock IConnectionMultiplexer and ISubscriber.
/// </summary>
public class RedisMeshBusTests : IDisposable
{
    private readonly Mock<IConnectionMultiplexer> _redisMock;
    private readonly Mock<ISubscriber> _subscriberMock;
    private readonly Mock<IDatabase> _dbMock;
    private readonly RedisMeshConfig _config;
    private readonly RedisMeshBus _bus;

    public RedisMeshBusTests()
    {
        _redisMock = new Mock<IConnectionMultiplexer>();
        _subscriberMock = new Mock<ISubscriber>();
        _dbMock = new Mock<IDatabase>();

        _redisMock.Setup(r => r.GetSubscriber(It.IsAny<object>())).Returns(_subscriberMock.Object);
        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_dbMock.Object);
        _redisMock.Setup(r => r.IsConnected).Returns(true);
        _redisMock.Setup(r => r.GetEndPoints(It.IsAny<bool>())).Returns(new EndPoint[] { new DnsEndPoint("localhost", 6379) });

        _config = new RedisMeshConfig { Enabled = true, KeyPrefix = "test:", ChannelPrefix = "test:bus:" };

        var loggerMock = new Mock<ILogger<RedisMeshBus>>();
        _bus = new RedisMeshBus(_redisMock.Object, _config, loggerMock.Object);
    }

    [Fact]
    public void BackendKind_ReturnsRedis()
    {
        Assert.Equal("redis", _bus.BackendKind);
    }

    [Fact]
    public async Task IsHealthyAsync_ReturnsTrue_WhenPingSucceeds()
    {
        _dbMock.Setup(d => d.PingAsync(It.IsAny<CommandFlags>()))
            .Returns(Task.FromResult(TimeSpan.FromMilliseconds(1)));

        var result = await _bus.IsHealthyAsync();
        Assert.True(result);
    }

    [Fact]
    public async Task IsHealthyAsync_ReturnsFalse_WhenDisposed()
    {
        _bus.Dispose();
        var result = await _bus.IsHealthyAsync();
        Assert.False(result);
    }

    [Fact]
    public async Task PublishAsync_PublishesToRedisChannel()
    {
        RedisChannel? capturedChannel = null;
        RedisValue? capturedValue = null;

        _subscriberMock
            .Setup(s => s.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((ch, val, _) =>
            {
                capturedChannel = ch;
                capturedValue = val;
            })
            .Returns(Task.FromResult(1L));

        var envelope = new IntentEnvelope
        {
            RequestId = "req-pub-1",
            Intent = "test.publish",
            Payload = "{\"msg\":\"hello\"}"
        };

        await _bus.PublishAsync("intent/test", envelope);

        Assert.NotNull(capturedChannel);
        Assert.Equal("test:bus:intent/test", capturedChannel.ToString());
        Assert.NotNull(capturedValue);
        Assert.Contains("req-pub-1", capturedValue.ToString());
    }

    [Fact]
    public async Task PublishAsync_FallsBackToLocal_WhenRedisUnavailable()
    {
        _subscriberMock
            .Setup(s => s.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "simulated"));

        var received = new List<IntentEnvelope>();
        var sub = await _bus.SubscribeAsync("fallback/topic", async (env, _) =>
        {
            lock (received) { received.Add(env); }
            await Task.CompletedTask;
        });

        var envelope = new IntentEnvelope
        {
            RequestId = "req-fb-1",
            Intent = "fallback.test",
            Payload = "fallback-payload"
        };

        await _bus.PublishAsync("fallback/topic", envelope);
        await Task.Delay(100);

        Assert.Single(received);
        Assert.Equal("req-fb-1", received[0].RequestId);

        sub.Dispose();
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsDisposable()
    {
        var sub = await _bus.SubscribeAsync("sub/topic", (env, _) => Task.CompletedTask);
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

        var envelope = new IntentEnvelope { RequestId = "r", Intent = "remove" };
        await _bus.PublishAsync("remove/topic", envelope);
        await Task.Delay(100);

        Assert.Equal(0, callCount);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var bus2 = new RedisMeshBus(_redisMock.Object, _config,
            new Mock<ILogger<RedisMeshBus>>().Object);
        bus2.Dispose();
        bus2.Dispose(); // Should not throw
    }

    public void Dispose() => _bus.Dispose();
}
