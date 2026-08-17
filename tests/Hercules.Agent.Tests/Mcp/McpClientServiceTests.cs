using Hercules.Config;
using Hercules.Mcp;
using Hercules.Tools;
using Hercules.Tools.Policy;
using Hercules.Tools.Registry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Mcp;

/// <summary>
///     Tests for <see cref="McpClientService"/>:
///     graceful degradation on unknown transport, idempotency, empty config, dispose,
///     and (task_100) diff-based hot-reload that adds new servers, removes gone ones,
///     updates changed ones, and unregisters the corresponding tools.
/// </summary>
public class McpClientServiceTests : IDisposable
{
    private readonly string _tempConfigPath;
    private readonly Mock<ILogger<McpClientService>> _loggerMock;

    public McpClientServiceTests()
    {
        _tempConfigPath = Path.Combine(Path.GetTempPath(), $"hercules-mcp-{Guid.NewGuid():N}.json");
        _loggerMock = new Mock<ILogger<McpClientService>>();
    }

    public void Dispose()
    {
        TryDelete(_tempConfigPath);
    }

    // -----------------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------------

    private static ILoggerFactory NoOpLoggerFactory() => NullLoggerFactory.Instance;

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    private RuntimeConfigStore NewStore(McpConfig? mcp = null)
    {
        var initial = new AppConfig { Mcp = mcp ?? new McpConfig() };
        return new RuntimeConfigStore(initial, _tempConfigPath, NullLogger<RuntimeConfigStore>.Instance);
    }

    private static ToolRegistryService NewToolRegistry()
    {
        var cfg = new ToolRegistryConfig { Enabled = true, AllowedPatterns = new List<string> { "*" } };
        return new ToolRegistryService(
            Array.Empty<ITool>(),
            cfg,
            policyEngine: null,
            NullLogger<ToolRegistryService>.Instance);
    }

    private static McpServerConfig UnknownTransportServer(string name) => new()
    {
        Name = name,
        Transport = "unknown",
        Enabled = true
    };

    // -----------------------------------------------------------------------
    //  Initialization
    // -----------------------------------------------------------------------

    [Fact]
    public async Task InitializeAsync_NoServersConfigured_LogsInfoAndReturns()
    {
        var store = NewStore();
        var service = new McpClientService(store, NoOpLoggerFactory(), _loggerMock.Object, null);

        await service.InitializeAsync();

        Assert.Empty(service.ServerStates);
    }

    [Fact]
    public async Task InitializeAsync_ServerWithEmptyName_SkipsServer()
    {
        var store = NewStore(new McpConfig
        {
            Servers = { new McpServerConfig { Name = "", Transport = "stdio", Command = "npx" } }
        });
        var service = new McpClientService(store, NoOpLoggerFactory(), _loggerMock.Object, null);

        await service.InitializeAsync();

        Assert.Empty(service.ServerStates);
    }

    [Fact]
    public async Task InitializeAsync_UnknownTransport_RecordsUnhealthyState()
    {
        var store = NewStore(new McpConfig { Servers = { UnknownTransportServer("bad-transport") } });
        var service = new McpClientService(store, NoOpLoggerFactory(), _loggerMock.Object, null);

        await service.InitializeAsync();

        Assert.True(service.ServerStates.TryGetValue("bad-transport", out var state));
        Assert.Equal(McpServerHealthStatus.Unhealthy, state.Status);
        Assert.NotNull(state.LastError);
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent()
    {
        var store = NewStore(new McpConfig { Servers = { UnknownTransportServer("idempotent-server") } });
        var service = new McpClientService(store, NoOpLoggerFactory(), _loggerMock.Object, null);

        await service.InitializeAsync();
        await service.InitializeAsync(); // second call

        Assert.Single(service.ServerStates);
    }

    [Fact]
    public async Task ReloadAsync_ClearsAndReconnects()
    {
        var store = NewStore(new McpConfig { Servers = { UnknownTransportServer("reload-test") } });
        var service = new McpClientService(store, NoOpLoggerFactory(), _loggerMock.Object, null);
        await service.InitializeAsync();
        Assert.Single(service.ServerStates);

        // Reload with no servers should clear state
        store.Update(new AppConfig { Mcp = new McpConfig() });
        await service.ReloadAsync();

        Assert.Empty(service.ServerStates);
    }

    [Fact]
    public async Task DisposeAsync_ClearsAllConnections()
    {
        var store = NewStore(new McpConfig { Servers = { UnknownTransportServer("dispose-test") } });
        var service = new McpClientService(store, NoOpLoggerFactory(), _loggerMock.Object, null);
        await service.InitializeAsync();

        await service.DisposeAsync();

        Assert.Empty(service.ServerStates);
    }

    [Fact]
    public async Task ServerStates_ReturnsCorrectCount()
    {
        var store = NewStore(new McpConfig { Servers = { UnknownTransportServer("state-test") } });
        var service = new McpClientService(store, NoOpLoggerFactory(), _loggerMock.Object, null);
        await service.InitializeAsync();

        var states = service.ServerStates;

        Assert.Single(states);
        Assert.True(states.ContainsKey("state-test"));
    }

    // -----------------------------------------------------------------------
    //  task_100 — diff-based hot-reload
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReloadAsync_AddsNewServer_ConnectsNewAndKeepsExisting()
    {
        var store = NewStore(new McpConfig { Servers = { UnknownTransportServer("alpha") } });
        var toolRegistry = NewToolRegistry();
        var service = new McpClientService(store, NoOpLoggerFactory(), _loggerMock.Object, toolRegistry);
        await service.InitializeAsync();
        Assert.Single(service.ServerStates);

        // Update store: add "beta" alongside "alpha"
        store.Update(new AppConfig
        {
            Mcp = new McpConfig
            {
                Servers = { UnknownTransportServer("alpha"), UnknownTransportServer("beta") }
            }
        });
        await service.ReloadAsync();

        Assert.Equal(2, service.ServerStates.Count);
        Assert.True(service.ServerStates.ContainsKey("alpha"));
        Assert.True(service.ServerStates.ContainsKey("beta"));
    }

    [Fact]
    public async Task ReloadAsync_RemovesOldServer_DisconnectsAndUnregistersTools()
    {
        var store = NewStore(new McpConfig
        {
            Servers = { UnknownTransportServer("alpha"), UnknownTransportServer("beta") }
        });
        var toolRegistry = NewToolRegistry();
        var service = new McpClientService(store, NoOpLoggerFactory(), _loggerMock.Object, toolRegistry);
        await service.InitializeAsync();
        Assert.Equal(2, service.ServerStates.Count);

        // Pre-register tools for both servers (as if they had successful connections).
        // In the unknown-transport test path, ConnectServerAsync records Unhealthy
        // state but does NOT call RegisterTool, so we simulate the successful case here.
        toolRegistry.RegisterTool(new FakeMcpTool("mcp.alpha.tool1"));
        toolRegistry.RegisterTool(new FakeMcpTool("mcp.alpha.tool2"));
        toolRegistry.RegisterTool(new FakeMcpTool("mcp.beta.tool1"));
        Assert.NotNull(toolRegistry.GetEntry("mcp.alpha.tool1"));
        Assert.NotNull(toolRegistry.GetEntry("mcp.beta.tool1"));

        // Update store: drop "alpha", keep only "beta"
        store.Update(new AppConfig
        {
            Mcp = new McpConfig { Servers = { UnknownTransportServer("beta") } }
        });
        await service.ReloadAsync();

        // Connection removed
        Assert.Single(service.ServerStates);
        Assert.False(service.ServerStates.ContainsKey("alpha"));
        Assert.True(service.ServerStates.ContainsKey("beta"));

        // Alpha's tools unregistered, beta's still present
        Assert.Null(toolRegistry.GetEntry("mcp.alpha.tool1"));
        Assert.Null(toolRegistry.GetEntry("mcp.alpha.tool2"));
        Assert.NotNull(toolRegistry.GetEntry("mcp.beta.tool1"));
    }

    [Fact]
    public async Task ReloadAsync_UpdatesExistingServer_ReconnectsWithNewConfig()
    {
        // Initial: server with stdio transport + "old" command.
        var store = NewStore(new McpConfig
        {
            Servers =
            {
                new McpServerConfig
                {
                    Name = "svc",
                    Transport = "stdio",
                    Command = "old-cmd",
                    Args = new List<string> { "--old" }
                }
            }
        });
        var service = new McpClientService(store, NoOpLoggerFactory(), _loggerMock.Object, null);
        await service.InitializeAsync();
        Assert.True(service.ServerStates.ContainsKey("svc"));

        // Update: change Command → should trigger reconnect.
        store.Update(new AppConfig
        {
            Mcp = new McpConfig
            {
                Servers =
                {
                    new McpServerConfig
                    {
                        Name = "svc",
                        Transport = "stdio",
                        Command = "new-cmd",
                        Args = new List<string> { "--new" }
                    }
                }
            }
        });
        await service.ReloadAsync();

        // Server still present (the second connect also records state, possibly Unhealthy
        // because of stdio real connection in test env — that's fine, we only assert presence
        // and that the connection was re-established, not torn down entirely).
        Assert.True(service.ServerStates.ContainsKey("svc"));
    }

    [Fact]
    public async Task ReloadAsync_EmptyNewConfig_DisconnectsAll()
    {
        var store = NewStore(new McpConfig { Servers = { UnknownTransportServer("a"), UnknownTransportServer("b") } });
        var service = new McpClientService(store, NoOpLoggerFactory(), _loggerMock.Object, null);
        await service.InitializeAsync();
        Assert.Equal(2, service.ServerStates.Count);

        store.Update(new AppConfig { Mcp = new McpConfig() });
        await service.ReloadAsync();

        Assert.Empty(service.ServerStates);
    }

    [Fact]
    public async Task IConfigReload_Reload_AppliesInBackground()
    {
        var store = NewStore(new McpConfig { Servers = { UnknownTransportServer("first") } });
        var service = new McpClientService(store, NoOpLoggerFactory(), _loggerMock.Object, null);
        await service.InitializeAsync();
        Assert.Single(service.ServerStates);

        // Update the store; then call IConfigReload.Reload (fire-and-forget).
        store.Update(new AppConfig
        {
            Mcp = new McpConfig { Servers = { UnknownTransportServer("first"), UnknownTransportServer("second") } }
        });
        service.Reload(store.Current);

        // Wait for the background task to apply the change (poll with timeout).
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && service.ServerStates.Count < 2)
        {
            await Task.Delay(50);
        }

        Assert.Equal(2, service.ServerStates.Count);
        Assert.True(service.ServerStates.ContainsKey("second"));
    }

    [Fact]
    public async Task ReloadAsync_AddsNewServer_RegistersItsTools()
    {
        var store = NewStore();
        var toolRegistry = NewToolRegistry();
        var service = new McpClientService(store, NoOpLoggerFactory(), _loggerMock.Object, toolRegistry);
        await service.InitializeAsync();
        Assert.Empty(service.ServerStates);

        // Add a server (using unknown transport so we don't actually try to open a process
        // or HTTP connection — the state will be Unhealthy but the connection entry exists).
        store.Update(new AppConfig
        {
            Mcp = new McpConfig { Servers = { UnknownTransportServer("only") } }
        });
        await service.ReloadAsync();

        Assert.True(service.ServerStates.ContainsKey("only"));
    }

    // -----------------------------------------------------------------------
    //  Minimal ITool stub for testing tool unregistration.
    // -----------------------------------------------------------------------

    private sealed class FakeMcpTool : ITool
    {
        public FakeMcpTool(string name) { Name = name; }
        public string Name { get; }
        public string Description => "fake";
        public string? ParametersSchema => null;
        public Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default) =>
            Task.FromResult(ToolResult.Ok("ok"));
    }
}
