using System.Collections.Concurrent;
using Hercules.Config;
using Hercules.Tools.Registry;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using McpClient = ModelContextProtocol.Client.McpClient;

namespace Hercules.Mcp;

/// <summary>
///     Manages connections to configured MCP servers, wraps their tools as Hercules <see cref="ITool"/>
///     and registers them with the <see cref="ToolRegistry"/>.
///     Supports stdio and HTTP/SSE transports.
/// </summary>
public sealed class McpClientService : IAsyncDisposable
{
    private readonly McpConfig _config;
    private readonly ToolRegistryService? _toolRegistryService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<McpClientService> _logger;
    private readonly ConcurrentDictionary<string, McpServerConnection> _connections = new(StringComparer.OrdinalIgnoreCase);
    private bool _initialized;

    public McpClientService(
        McpConfig config,
        ILoggerFactory loggerFactory,
        ILogger<McpClientService> logger,
        ToolRegistryService? toolRegistryService = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = logger;
        _toolRegistryService = toolRegistryService;
    }

    /// <summary>All connected MCP server states.</summary>
    public IReadOnlyDictionary<string, McpServerState> ServerStates => _connections.ToDictionary(
        kvp => kvp.Key, kvp => kvp.Value.State, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Connect to all configured MCP servers and register their tools.
    ///     Safe to call multiple times (idempotent).
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized)
            return;

        _initialized = true;

        if (_config.Servers.Count == 0)
        {
            _logger.LogInformation("No MCP servers configured.");
            return;
        }

        foreach (McpServerConfig serverCfg in _config.Servers)
        {
            if (string.IsNullOrWhiteSpace(serverCfg.Name))
            {
                _logger.LogWarning("Skipping MCP server with empty name");
                continue;
            }

            await ConnectServerAsync(serverCfg, ct);
        }
    }

    private async Task ConnectServerAsync(McpServerConfig cfg, CancellationToken ct)
    {
        var logger = _loggerFactory.CreateLogger<McpClientService>();

        try
        {
            IClientTransport transport = CreateTransport(cfg);
            var options = new McpClientOptions
            {
                ClientInfo = new ModelContextProtocol.Protocol.Implementation
                {
                    Name = "hercules",
                    Version = "1.0"
                }
            };

            McpClient client = await McpClient.CreateAsync(transport, options, _loggerFactory, ct);

            var tools = await client.ListToolsAsync(cancellationToken: ct);

            foreach (McpClientTool tool in tools)
            {
                var adapter = new McpToolAdapter(client, tool, cfg.Name, _loggerFactory.CreateLogger<McpToolAdapter>());
                try
                {
                    _toolRegistryService?.RegisterTool(adapter);
                    _toolRegistryService?.UpdateHealthState(adapter.Name, new ToolHealthState(
                        ToolHealthStatus.Healthy, DateTime.UtcNow, null, 0));
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning("MCP tool '{Tool}' skipped (duplicate name): {Error}", adapter.Name, ex.Message);
                }
            }

            var state = new McpServerState(
                cfg.Name, cfg.Transport, McpServerHealthStatus.Healthy,
                DateTime.UtcNow, null, null, tools.Count,
                client.ServerInfo?.Version);

            _connections[cfg.Name] = new McpServerConnection(cfg.Name, client, state);
            _logger.LogInformation(
                "Connected to MCP server '{Name}' ({Transport}) — {ToolCount} tools registered",
                cfg.Name, cfg.Transport, tools.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to MCP server '{Name}': {Error}", cfg.Name, ex.Message);
            var state = new McpServerState(
                cfg.Name, cfg.Transport, McpServerHealthStatus.Unhealthy,
                null, DateTime.UtcNow, ex.Message, 0, null);
            _connections[cfg.Name] = new McpServerConnection(cfg.Name, null, state);
        }
    }

    private IClientTransport CreateTransport(McpServerConfig cfg)
    {
        return cfg.Transport?.ToLowerInvariant() switch
        {
            "stdio" => CreateStdioTransport(cfg),
            "http" or "sse" => CreateHttpTransport(cfg),
            _ => throw new InvalidOperationException(
                $"Unknown MCP transport '{cfg.Transport}' for server '{cfg.Name}'. " +
                "Supported: stdio, http, sse")
        };
    }

    private IClientTransport CreateStdioTransport(McpServerConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.Command))
        {
            throw new InvalidOperationException(
                $"MCP server '{cfg.Name}' uses stdio transport but Command is not set.");
        }

        var options = new StdioClientTransportOptions
        {
            Command = cfg.Command,
            Arguments = cfg.Args ?? new List<string>()
        };

        return new StdioClientTransport(options, _loggerFactory);
    }

    private IClientTransport CreateHttpTransport(McpServerConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.Endpoint))
        {
            throw new InvalidOperationException(
                $"MCP server '{cfg.Name}' uses HTTP transport but Endpoint is not set.");
        }

        var options = new HttpClientTransportOptions
        {
            Endpoint = new Uri(cfg.Endpoint)
        };

        return new HttpClientTransport(options, _loggerFactory);
    }

    /// <summary>Reload connections from current config.</summary>
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Reloading MCP connections...");

        // Disconnect existing
        foreach (var conn in _connections.Values)
        {
            try
            {
                await conn.DisposeAsync();
            }
            catch
            {
                /* best effort */
            }
        }

        _connections.Clear();
        _initialized = false;
        await InitializeAsync(ct);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var conn in _connections.Values)
        {
            await conn.DisposeAsync();
        }

        _connections.Clear();
    }
}

/// <summary>
///     Holds a connected MCP client and its state.
/// </summary>
internal sealed class McpServerConnection : IAsyncDisposable
{
    public McpServerConnection(string name, McpClient? client, McpServerState state)
    {
        Name = name;
        Client = client;
        State = state;
    }

    public string Name { get; }
    public McpClient? Client { get; }
    public McpServerState State { get; }

    public async ValueTask DisposeAsync()
    {
        if (Client != null)
            await Client.DisposeAsync();
    }
}
