using Hercules.Mesh;
using Hercules.Mesh.Backends.Postgres;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Hercules.Agent.Tests.Mesh.Backends;

/// <summary>
///     Unit tests for <see cref="PostgresMeshBus"/> (task_069).
///     Tests BackendKind, IsHealthy (unreachable host), Dispose, constructor validation,
///     disposed-state checks.
///     Happy paths require a live PostgreSQL instance and are exercised in integration suites.
/// </summary>
public class PostgresMeshBusTests : IDisposable
{
    private readonly PostgresMeshConfig _config;
    private readonly NpgsqlDataSource _dataSource;
    private readonly PostgresMeshBus _bus;

    public PostgresMeshBusTests()
    {
        _config = new PostgresMeshConfig
        {
            Enabled = true,
            ConnectionString = "Host=127.0.0.1;Port=1;Username=postgres;Password=postgres;Database=postgres;Timeout=1;Command Timeout=1",
            AutoCreateSchema = false
        };
        _dataSource = new NpgsqlDataSourceBuilder(_config.ConnectionString).Build();
        _bus = new PostgresMeshBus(_dataSource, _config, NullLogger<PostgresMeshBus>.Instance);
    }

    [Fact]
    public void BackendKind_ReturnsPostgres()
    {
        Assert.Equal("postgres", _bus.BackendKind);
    }

    [Fact]
    public void Constructor_NullDataSource_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgresMeshBus(null!, _config, NullLogger<PostgresMeshBus>.Instance));
    }

    [Fact]
    public void Constructor_NullConfig_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgresMeshBus(_dataSource, null!, NullLogger<PostgresMeshBus>.Instance));
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgresMeshBus(_dataSource, _config, null!));
    }

    [Fact]
    public async Task IsHealthyAsync_ReturnsFalse_WhenDbUnreachable()
    {
        var result = await _bus.IsHealthyAsync();
        Assert.False(result);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        _bus.Dispose();
        _bus.Dispose();
    }

    [Fact]
    public async Task DisposedBus_PublishThrows()
    {
        _bus.Dispose();
        var envelope = new IntentEnvelope();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await _bus.PublishAsync("topic", envelope));
    }

    public void Dispose()
    {
        _bus.Dispose();
        _dataSource.Dispose();
    }
}
