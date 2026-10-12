using System.Net;
using Hercules.Mesh.Abstractions;
using Hercules.Mesh.Backends.Redis;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace Hercules.Agent.Tests.Phase5Tests;

/// <summary>
///     CAS / optimistic-locking tests for <see cref="RedisMeshStateStore.CompareAndSetAsync"/>
///     (task_082, H14). Verifies that the implementation now uses real
///     <c>WATCH / MULTI / EXEC</c> semantics via <c>ITransaction.AddCondition</c>
///     and that the retry loop terminates correctly on persistent contention.
/// </summary>
public class RedisMeshStateStoreCasTests : IDisposable
{
    private readonly Mock<IConnectionMultiplexer> _redisMock;
    private readonly Mock<IDatabase> _dbMock;
    private readonly Mock<ITransaction> _tranMock;
    private readonly RedisMeshConfig _config;
    private readonly RedisMeshStateStore _store;

    public RedisMeshStateStoreCasTests()
    {
        _redisMock = new Mock<IConnectionMultiplexer>();
        _dbMock = new Mock<IDatabase>();
        _tranMock = new Mock<ITransaction>();

        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_dbMock.Object);
        _redisMock.Setup(r => r.IsConnected).Returns(true);
        _redisMock.Setup(r => r.GetEndPoints(It.IsAny<bool>())).Returns(new EndPoint[] { new DnsEndPoint("localhost", 6379) });

        _dbMock.Setup(d => d.CreateTransaction(It.IsAny<object>())).Returns(_tranMock.Object);

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

    private static HashEntry[] BuildHash(string version, DateTimeOffset createdAt, string data = "{\"value\":1}")
    {
        return new HashEntry[]
        {
            new("data", data),
            new("version", version),
            new("createdAt", createdAt.ToUnixTimeMilliseconds().ToString()),
            new("updatedAt", createdAt.ToUnixTimeMilliseconds().ToString()),
            new("expiresAt", "0"),
            new("lastWriterAgentId", ""),
        };
    }

    private static StoredValue NewValue(string data = "x") => new()
    {
        Data = data,
        Version = "ignored-by-store",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task CompareAndSet_KeyMissing_ReturnsFalse_NoTransactionCreated()
    {
        _dbMock.Setup(d => d.HashGetAllAsync(
                It.Is<RedisKey>(k => k.ToString() == "test:mesh:missing"),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<HashEntry>());

        var result = await _store.CompareAndSetAsync("missing", NewValue(), "expected-v1");

        Assert.False(result);
        _dbMock.Verify(d => d.CreateTransaction(It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public async Task CompareAndSet_VersionMismatchFastPath_ReturnsFalse_NoTransactionCreated()
    {
        _dbMock.Setup(d => d.HashGetAllAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(BuildHash("actual-v1", DateTimeOffset.UtcNow));

        var result = await _store.CompareAndSetAsync("key", NewValue(), "expected-v1");

        Assert.False(result);
        // The fast path is hit; no transaction is opened because we know
        // the version doesn't match without a WATCH round-trip.
        _dbMock.Verify(d => d.CreateTransaction(It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public async Task CompareAndSet_FirstAttemptCommits_AddsWatchConditionAndReturnsTrue()
    {
        var now = DateTimeOffset.UtcNow;
        _dbMock.Setup(d => d.HashGetAllAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(BuildHash("expected-v1", now));

        _tranMock.Setup(t => t.AddCondition(It.IsAny<Condition>())).Returns(default(ConditionResult));
        _tranMock.Setup(t => t.HashSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<HashEntry[]>(),
                It.IsAny<CommandFlags>()))
            .Returns(Task.CompletedTask);
        _tranMock.Setup(t => t.KeyExpireAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _tranMock.Setup(t => t.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var result = await _store.CompareAndSetAsync("key", NewValue("new"), "expected-v1");

        Assert.True(result);
        // Real WATCH-style condition: AddCondition must be called once.
        _tranMock.Verify(t => t.AddCondition(It.IsAny<Condition>()), Times.Once);
        // Transaction ExecuteAsync must be called exactly once (no retry).
        _tranMock.Verify(t => t.ExecuteAsync(It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task CompareAndSet_FirstAttemptFails_SecondCommits_RetriesWithBackoff()
    {
        var now = DateTimeOffset.UtcNow;
        _dbMock.Setup(d => d.HashGetAllAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(BuildHash("expected-v1", now));

        _tranMock.Setup(t => t.AddCondition(It.IsAny<Condition>())).Returns(default(ConditionResult));
        _tranMock.Setup(t => t.HashSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<HashEntry[]>(),
                It.IsAny<CommandFlags>()))
            .Returns(Task.CompletedTask);
        _tranMock.Setup(t => t.KeyExpireAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // First ExecuteAsync returns false (CAS lost), second returns true.
        var calls = 0;
        _tranMock.Setup(t => t.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(() => ++calls == 2);

        var result = await _store.CompareAndSetAsync("key", NewValue("new"), "expected-v1");

        Assert.True(result);
        // Two attempts, two AddCondition calls, two ExecuteAsync calls.
        _tranMock.Verify(t => t.AddCondition(It.IsAny<Condition>()), Times.Exactly(2));
        _tranMock.Verify(t => t.ExecuteAsync(It.IsAny<CommandFlags>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CompareAndSet_AllAttemptsFail_ReturnsFalseAfterMaxRetries()
    {
        var now = DateTimeOffset.UtcNow;
        _dbMock.Setup(d => d.HashGetAllAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(BuildHash("expected-v1", now));

        _tranMock.Setup(t => t.AddCondition(It.IsAny<Condition>())).Returns(default(ConditionResult));
        _tranMock.Setup(t => t.HashSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<HashEntry[]>(),
                It.IsAny<CommandFlags>()))
            .Returns(Task.CompletedTask);
        _tranMock.Setup(t => t.KeyExpireAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _tranMock.Setup(t => t.ExecuteAsync(It.IsAny<CommandFlags>())).ReturnsAsync(false);

        var result = await _store.CompareAndSetAsync("key", NewValue("new"), "expected-v1");

        Assert.False(result);
        // MaxCasAttempts (5) attempts must be made before giving up.
        _tranMock.Verify(t => t.AddCondition(It.IsAny<Condition>()), Times.Exactly(5));
        _tranMock.Verify(t => t.ExecuteAsync(It.IsAny<CommandFlags>()), Times.Exactly(5));
    }

    [Fact]
    public async Task CompareAndSet_ExpectedVersionNull_UsesCreateIfNotExists_NotTransaction()
    {
        _dbMock.Setup(d => d.HashGetAllAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<HashEntry>());

        _dbMock.Setup(d => d.HashSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<HashEntry[]>(),
                It.IsAny<CommandFlags>()))
            .Returns(Task.CompletedTask);

        var result = await _store.CompareAndSetAsync("newkey", NewValue(), expectedVersion: null);

        Assert.True(result);
        // The create-if-not-exists path must not open a Redis transaction.
        _dbMock.Verify(d => d.CreateTransaction(It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public async Task CompareAndSet_KeyExistsAndExpectedVersionNull_ReturnsFalse()
    {
        _dbMock.Setup(d => d.HashGetAllAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(BuildHash("v1", DateTimeOffset.UtcNow));

        var result = await _store.CompareAndSetAsync("key", NewValue(), expectedVersion: null);

        Assert.False(result);
        _dbMock.Verify(d => d.HashSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<HashEntry[]>(),
            It.IsAny<CommandFlags>()), Times.Never);
    }

    public void Dispose() => _store.Dispose();
}
