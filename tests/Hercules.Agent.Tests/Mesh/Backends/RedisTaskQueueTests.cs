using System.Net;
using Hercules.Mesh.Abstractions;
using Hercules.Mesh.Backends.Redis;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace Hercules.Agent.Tests.Mesh.Backends;

/// <summary>
///     Unit tests for <see cref="RedisTaskQueue"/> (task_067).
///     Uses Moq to mock IConnectionMultiplexer and IDatabase.
/// </summary>
public class RedisTaskQueueTests : IDisposable
{
    private readonly Mock<IConnectionMultiplexer> _redisMock;
    private readonly Mock<IDatabase> _dbMock;
    private readonly RedisMeshConfig _config;
    private readonly RedisTaskQueue _queue;

    public RedisTaskQueueTests()
    {
        _redisMock = new Mock<IConnectionMultiplexer>();
        _dbMock = new Mock<IDatabase>();

        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_dbMock.Object);
        _redisMock.Setup(r => r.IsConnected).Returns(true);
        _redisMock.Setup(r => r.GetEndPoints(It.IsAny<bool>())).Returns(new EndPoint[] { new DnsEndPoint("localhost", 6379) });

        _config = new RedisMeshConfig
        {
            Enabled = true,
            KeyPrefix = "test:",
            DefaultVisibilityTimeoutSec = 30
        };

        var loggerMock = new Mock<ILogger<RedisTaskQueue>>();
        _queue = new RedisTaskQueue(_redisMock.Object, _config, loggerMock.Object);
    }

    [Fact]
    public void BackendKind_ReturnsRedis()
    {
        Assert.Equal("redis", _queue.BackendKind);
    }

    [Fact]
    public async Task EnqueueAsync_StoresTaskMetadata()
    {
        string? capturedReceiptKey = null;
        string? capturedJson = null;

        _dbMock.Setup(d => d.StringSetAsync(
                It.Is<RedisKey>(k => k.ToString().StartsWith("hercules:receipt:")),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, TimeSpan?, bool, When, CommandFlags>(
                (k, v, _, _, _, _) =>
                {
                    capturedReceiptKey = k.ToString();
                    capturedJson = v.ToString();
                })
            .ReturnsAsync(true);

        _dbMock.Setup(d => d.ListRightPushAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        var task = new MeshTask
        {
            QueueName = "test-queue",
            Intent = "test.intent",
            Payload = "{\"param\":1}",
            MaxRetries = 3
        };

        var result = await _queue.EnqueueAsync(task);

        Assert.NotNull(result);
        Assert.NotEmpty(result.ReceiptHandle);
        Assert.Equal("test-queue", result.Task.QueueName);
        Assert.NotEmpty(result.Id);
        Assert.Equal(1, result.DeliveryCount);
        Assert.Equal(0, result.RetryCount);
        Assert.NotNull(capturedReceiptKey);
        Assert.Contains("hercules:receipt:", capturedReceiptKey);
        Assert.NotNull(capturedJson);
        Assert.Contains("test.intent", capturedJson);
    }

    [Fact]
    public async Task EnqueueAsync_GeneratesId_WhenEmpty()
    {
        _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _dbMock.Setup(d => d.ListRightPushAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        var task = new MeshTask { Intent = "generate-id-test" };
        var result = await _queue.EnqueueAsync(task);
        Assert.NotEmpty(result.Id);
    }

    [Fact]
    public async Task GetDeadLetterQueueAsync_ReturnsEmptyList_WhenQueueEmpty()
    {
        _dbMock.Setup(d => d.ListRangeAsync(
                It.IsAny<RedisKey>(),
                0,
                99,
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<RedisValue>());

        var result = await _queue.GetDeadLetterQueueAsync("dlq-test");
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task IsHealthyAsync_ReturnsTrue_WhenPingSucceeds()
    {
        _dbMock.Setup(d => d.PingAsync(It.IsAny<CommandFlags>()))
            .Returns(Task.FromResult(TimeSpan.FromMilliseconds(1)));

        var result = await _queue.IsHealthyAsync();
        Assert.True(result);
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
        var queue2 = new RedisTaskQueue(_redisMock.Object, _config,
            new Mock<ILogger<RedisTaskQueue>>().Object);
        queue2.Dispose();
        queue2.Dispose(); // Should not throw
    }

    public void Dispose() => _queue.Dispose();
}
