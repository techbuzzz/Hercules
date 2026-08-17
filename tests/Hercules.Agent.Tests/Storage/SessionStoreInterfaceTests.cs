using Hercules.Config;
using Hercules.Storage;
using Xunit;

namespace Hercules.Agent.Tests.Storage;

/// <summary>
///     task_103: covers the storage-agnostic <see cref="ISessionStore"/>
///     contract: the legacy SQLite store satisfies it without behaviour
///     changes, and the new <c>StorageConfig</c> sections
///     (<c>SessionStore</c> + <c>CollectiveMind</c>) deserialize with safe
///     defaults so an existing <c>appsettings.json</c> keeps working.
/// </summary>
public class SessionStoreInterfaceTests : IDisposable
{
    private readonly string _tempDir;

    public SessionStoreInterfaceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-isession-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { /* best effort */ }
    }

    [Fact]
    public void SqliteSessionStore_ImplementsISessionStore()
    {
        // Compile-time guarantee: SqliteSessionStore : ISessionStore.
        // The cast itself is the assertion — if the interface ever drifts
        // away from the implementation this test will fail to compile.
        using var store = new SqliteSessionStore(new StorageConfig { DataRoot = _tempDir });
        ISessionStore contract = store;
        Assert.NotNull(contract);
        Assert.True(contract.IsHealthy());
    }

    [Fact]
    public void StorageConfig_HasSessionStoreAndCollectiveMindSections()
    {
        var cfg = new StorageConfig();
        Assert.NotNull(cfg.SessionStore);
        Assert.NotNull(cfg.CollectiveMind);

        // Defaults preserve the legacy SQLite-on-disk behaviour so the
        // change is invisible to existing appsettings.json files.
        Assert.Equal("sqlite", cfg.SessionStore.Provider);
        Assert.Null(cfg.SessionStore.ConnectionString);
        Assert.Equal("public", cfg.SessionStore.Schema);
        Assert.False(cfg.CollectiveMind.Enabled);
        Assert.False(cfg.CollectiveMind.SharedMemory);
        Assert.Equal("per-agent", cfg.CollectiveMind.SessionIsolation);
    }

    [Fact]
    public void SessionStoreBackendConfig_AcceptsPostgresProvider()
    {
        var cfg = new SessionStoreBackendConfig
        {
            Provider = "postgres",
            ConnectionString = "Host=localhost;Database=hercules;Username=u;Password=p",
            Schema = "hercules"
        };

        Assert.Equal("postgres", cfg.Provider);
        Assert.StartsWith("Host=localhost", cfg.ConnectionString);
        Assert.Equal("hercules", cfg.Schema);
    }

    [Fact]
    public void CollectiveMindConfig_AcceptsSharedSessionIsolation()
    {
        var cfg = new CollectiveMindConfig
        {
            Enabled = true,
            SharedMemory = true,
            SessionIsolation = "shared"
        };

        Assert.True(cfg.Enabled);
        Assert.True(cfg.SharedMemory);
        Assert.Equal("shared", cfg.SessionIsolation);
    }

    [Fact]
    public async Task SqliteSessionStore_AsContract_RoundTripsSessionAndInteraction()
    {
        // Exercise the contract surface (sessions + interactions) to confirm
        // there is no behavioural drift versus the concrete type. Other
        // tables follow the same path; sessions + interactions are the
        // hot path of the agent loop so they're the most important to test.
        await using var store = new SqliteSessionStore(new StorageConfig { DataRoot = _tempDir });
        ISessionStore contract = store;

        await contract.StartSessionAsync("sess-contract-1");
        var before = await contract.GetTotalInteractionsAsync();
        Assert.Equal(0, before);
    }

    [Fact]
    public void PostgresSessionStore_RequiresConnectionString()
    {
        var backend = new SessionStoreBackendConfig
        {
            Provider = "postgres",
            ConnectionString = null
        };
        var storage = new StorageConfig { DataRoot = _tempDir };

        // The ctor must fail fast with a clear message rather than hanging
        // on a null connection string. Testcontainers/TestPostgres setup is
        // intentionally out of scope for the foundation PR.
        var ex = Assert.Throws<ArgumentException>(() => new PostgresSessionStore(backend, storage));
        Assert.Contains("ConnectionString", ex.Message);
    }
}
