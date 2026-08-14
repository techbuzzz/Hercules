using Hercules.Mesh.Abstractions;
using Hercules.Mesh.Backends.Postgres;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Hercules.Agent.Tests.Mesh.Backends;

/// <summary>
///     Unit tests for <see cref="PostgresMeshStateStore"/> (task_069).
///     Tests BackendKind, IsHealthy (unreachable host), Dispose, constructor validation.
///     Happy paths require a live PostgreSQL instance and are exercised in integration suites.
/// </summary>
public class PostgresMeshStateStoreTests : IDisposable
{
    private readonly PostgresMeshConfig _config;
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgresMeshStateStore> _logger;
    private readonly PostgresMeshStateStore _store;

    public PostgresMeshStateStoreTests()
    {
        _config = new PostgresMeshConfig
        {
            Enabled = true,
            // Point at a port that should never accept connections to exercise the catch path.
            ConnectionString = "Host=127.0.0.1;Port=1;Username=postgres;Password=postgres;Database=postgres;Timeout=1;Command Timeout=1",
            AutoCreateSchema = false,
            UseListenNotify = false
        };
        _dataSource = new NpgsqlDataSourceBuilder(_config.ConnectionString).Build();
        _logger = NullLogger<PostgresMeshStateStore>.Instance;
        _store = new PostgresMeshStateStore(_dataSource, _config, _logger);
    }

    [Fact]
    public void BackendKind_ReturnsPostgres()
    {
        Assert.Equal("postgres", _store.BackendKind);
    }

    [Fact]
    public void Constructor_NullDataSource_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgresMeshStateStore(null!, _config, _logger));
    }

    [Fact]
    public void Constructor_NullConfig_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgresMeshStateStore(_dataSource, null!, _logger));
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgresMeshStateStore(_dataSource, _config, null!));
    }

    [Fact]
    public async Task IsHealthyAsync_ReturnsFalse_WhenDbUnreachable()
    {
        var result = await _store.IsHealthyAsync();
        Assert.False(result);
    }

    [Fact]
    public async Task GetAsync_PropagatesConnectionError_WhenDbUnreachable()
    {
        // Connection error surfaces as thrown exception (caller decides degradation policy)
        await Assert.ThrowsAnyAsync<Exception>(async () => await _store.GetAsync("anykey"));
    }

    [Fact]
    public async Task GetAsync_NullOrEmptyKey_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(async () => await _store.GetAsync(""));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await _store.GetAsync(null!));
    }

    [Fact]
    public void QualifiedStateTable_QuotesSchemaAndTable()
    {
        var cfg = new PostgresMeshConfig { Schema = "my schema\"with-quotes", StateTable = "st" };
        var ds = new NpgsqlDataSourceBuilder("Host=localhost").Build();
        var logger = NullLogger<PostgresMeshStateStore>.Instance;
        using var store = new PostgresMeshStateStore(ds, cfg, logger);
        // Identifier quoting must escape embedded double quotes.
        Assert.Equal("\"my schema\"\"with-quotes\".\"st\"", store.QualifiedStateTable);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        _store.Dispose();
        _store.Dispose(); // second call must not throw
    }

    [Fact]
    public async Task DisposedStore_AccessThrows()
    {
        _store.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await _store.GetAsync("x"));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await _store.SetAsync("x", new StoredValue
        {
            Data = "d",
            Version = "v",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        }));
    }

    public void Dispose()
    {
        _store.Dispose();
        _dataSource.Dispose();
    }
}
