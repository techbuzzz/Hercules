using Hercules.Config;
using Hercules.Mcp;
using Hercules.Tools.Registry;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Mcp;

/// <summary>
///     Tests for <see cref="McpClientService"/>:
///     graceful degradation on unknown transport, idempotency, empty config, dispose.
/// </summary>
public class McpClientServiceTests : IDisposable
{
    private readonly McpConfig _config;
    private readonly Mock<ILogger<McpClientService>> _loggerMock;

    public McpClientServiceTests()
    {
        _config = new McpConfig();
        _loggerMock = new Mock<ILogger<McpClientService>>();
    }

    public void Dispose() { }

    [Fact]
    public async Task InitializeAsync_NoServersConfigured_LogsInfoAndReturns()
    {
        var service = new McpClientService(_config, NoOpLoggerFactory(), _loggerMock.Object, null);

        await service.InitializeAsync();

        Assert.Empty(service.ServerStates);
    }

    [Fact]
    public async Task InitializeAsync_ServerWithEmptyName_SkipsServer()
    {
        _config.Servers.Add(new McpServerConfig { Name = "", Transport = "stdio", Command = "npx" });
        var service = new McpClientService(_config, NoOpLoggerFactory(), _loggerMock.Object, null);

        await service.InitializeAsync();

        Assert.Empty(service.ServerStates);
    }

    [Fact]
    public async Task InitializeAsync_UnknownTransport_RecordsUnhealthyState()
    {
        _config.Servers.Add(new McpServerConfig
        {
            Name = "bad-transport",
            Transport = "unknown-transport",
            Enabled = true
        });
        var service = new McpClientService(_config, NoOpLoggerFactory(), _loggerMock.Object, null);

        await service.InitializeAsync();

        Assert.True(service.ServerStates.TryGetValue("bad-transport", out var state));
        Assert.Equal(McpServerHealthStatus.Unhealthy, state.Status);
        Assert.NotNull(state.LastError);
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent()
    {
        _config.Servers.Add(new McpServerConfig
        {
            Name = "idempotent-server",
            Transport = "unknown",
            Enabled = true
        });
        var service = new McpClientService(_config, NoOpLoggerFactory(), _loggerMock.Object, null);

        await service.InitializeAsync();
        await service.InitializeAsync(); // second call

        Assert.Single(service.ServerStates);
    }

    [Fact]
    public async Task ReloadAsync_ClearsAndReconnects()
    {
        _config.Servers.Add(new McpServerConfig
        {
            Name = "reload-test",
            Transport = "unknown",
            Enabled = true
        });
        var service = new McpClientService(_config, NoOpLoggerFactory(), _loggerMock.Object, null);
        await service.InitializeAsync();
        Assert.Single(service.ServerStates);

        // Reload with no servers should clear state
        _config.Servers.Clear();
        await service.ReloadAsync();

        Assert.Empty(service.ServerStates);
    }

    [Fact]
    public async Task DisposeAsync_ClearsAllConnections()
    {
        _config.Servers.Add(new McpServerConfig
        {
            Name = "dispose-test",
            Transport = "unknown",
            Enabled = true
        });
        var service = new McpClientService(_config, NoOpLoggerFactory(), _loggerMock.Object, null);
        await service.InitializeAsync();

        await service.DisposeAsync();

        Assert.Empty(service.ServerStates);
    }

    [Fact]
    public async Task ServerStates_ReturnsCorrectCount()
    {
        _config.Servers.Add(new McpServerConfig
        {
            Name = "state-test",
            Transport = "unknown",
            Enabled = true
        });
        var service = new McpClientService(_config, NoOpLoggerFactory(), _loggerMock.Object, null);
        await service.InitializeAsync();

        var states = service.ServerStates;

        Assert.Single(states);
        Assert.True(states.ContainsKey("state-test"));
    }

    // --- Helpers ---

    private static ILoggerFactory NoOpLoggerFactory()
        => Mock.Of<ILoggerFactory>();
}
