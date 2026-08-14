using Hercules.Mesh.Abstractions;
using Hercules.Mesh.Backends.Postgres;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Hercules.Agent.Tests.Mesh.Backends;

/// <summary>
///     Unit tests for <see cref="PostgresTaskQueue"/> (task_069).
///     Tests BackendKind, IsHealthy (unreachable host), Dispose, constructor validation,
///     payload validation, disposed-state checks.
///     Happy paths require a live PostgreSQL instance and are exercised in integration suites.
/// </summary>
public class PostgresTaskQueueTests : IDisposable
{
    private readonly PostgresMeshConfig _config;
    private readonly NpgsqlDataSource _dataSource;
    private readonly PostgresTaskQueue _queue;

    public PostgresTaskQueueTests()
    {
        _config = new PostgresMeshConfig
        {
            Enabled = true,
            ConnectionString = "Host=127.0.0.1;Port=1;Username=postgres;Password=postgres;Database=postgres;Timeout=1;Command Timeout=1",
            AutoCreateSchema = false
        };
        _dataSource = new NpgsqlDataSourceBuilder(_config.ConnectionString).Build();
        _queue = new PostgresTaskQueue(_dataSource, _config, NullLogger<PostgresTaskQueue>.Instance);
    }

    [Fact]
    public void BackendKind_ReturnsPostgres()
    {
        Assert.Equal("postgres", _queue.BackendKind);
    }

    [Fact]
    public void Constructor_NullDataSource_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgresTaskQueue(null!, _config, NullLogger<PostgresTaskQueue>.Instance));
    }

    [Fact]
    public void Constructor_NullConfig_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgresTaskQueue(_dataSource, null!, NullLogger<PostgresTaskQueue>.Instance));
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgresTaskQueue(_dataSource, _config, null!));
    }

    [Fact]
    public async Task IsHealthyAsync_ReturnsFalse_WhenDbUnreachable()
    {
        var result = await _queue.IsHealthyAsync();
        Assert.False(result);
    }

    [Fact]
    public async Task EnqueueAsync_NullTask_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await _queue.EnqueueAsync(null!));
    }

    [Fact]
    public async Task EnqueueAsync_PropagatesConnectionError_WhenDbUnreachable()
    {
        var task = new MeshTask { Intent = "test.intent", Payload = "{}" };
        await Assert.ThrowsAnyAsync<Exception>(async () => await _queue.EnqueueAsync(task));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        _queue.Dispose();
        _queue.Dispose();
    }

    [Fact]
    public async Task DisposedQueue_AccessThrows()
    {
        _queue.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await _queue.EnqueueAsync(new MeshTask { Intent = "x" }));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await _queue.AckAsync("x"));
    }

    public void Dispose()
    {
        _queue.Dispose();
        _dataSource.Dispose();
    }
}
