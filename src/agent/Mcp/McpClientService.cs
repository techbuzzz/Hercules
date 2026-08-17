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
///
///     [task_100] Hot-reload: implements <see cref="IConfigReload"/> so changes to
///     <c>AppConfig.Mcp.Servers</c> via <c>PATCH /api/config</c> are applied without
///     process restart. The service reads live config from
///     <see cref="RuntimeConfigStore.Current"/> rather than holding a snapshot reference.
/// </summary>
public sealed class McpClientService : IConfigReload, IAsyncDisposable
{
    private readonly RuntimeConfigStore _store;
    private readonly ToolRegistryService? _toolRegistryService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<McpClientService> _logger;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private readonly ConcurrentDictionary<string, McpServerConnection> _connections = new(StringComparer.OrdinalIgnoreCase);
    private bool _initialized;

    public McpClientService(
        RuntimeConfigStore store,
        ILoggerFactory loggerFactory,
        ILogger<McpClientService> logger,
        ToolRegistryService? toolRegistryService = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
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

        var config = _store.Current.Mcp;
        if (config.Servers.Count == 0)
        {
            _logger.LogInformation("No MCP servers configured.");
            return;
        }

        await ReloadFromConfigAsync(config, ct);
    }

    /// <summary>
    ///     IConfigReload callback (sync). Triggers a background <see cref="ReloadFromConfigAsync"/>
    ///     using the supplied <see cref="AppConfig"/>. Errors are logged but do not propagate
    ///     — the reactor contract is fire-and-forget.
    /// </summary>
    public void Reload(AppConfig config)
    {
        _logger.LogInformation("MCP hot-reload triggered via IConfigReload");

        _ = Task.Run(async () =>
        {
            try
            {
                await ReloadFromConfigAsync(config.Mcp, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MCP hot-reload failed");
            }
        });
    }

    /// <summary>
    ///     Public reload entry point used by <c>POST /api/mcp/servers/reload</c>.
    ///     Reads live <c>McpConfig</c> from <see cref="RuntimeConfigStore.Current"/> and
    ///     reconnects to match it. Awaitable so the controller can return when done.
    /// </summary>
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("MCP manual reload requested via /api/mcp/servers/reload");
        await ReloadFromConfigAsync(_store.Current.Mcp, ct);
    }

    /// <summary>
    ///     Diff-driven reload:
    ///     - servers in <paramref name="newConfig"/> but not connected → connect;
    ///     - servers connected but not in <paramref name="newConfig"/> → disconnect + unregister tools;
    ///     - servers present in both but with changed config → disconnect old, unregister tools, reconnect.
    ///     All access to <see cref="_connections"/> and the tool registry is serialised through
    ///     <see cref="_reloadLock"/> so concurrent reloads (e.g. PATCH + manual) don't race.
    /// </summary>
    private async Task ReloadFromConfigAsync(McpConfig newConfig, CancellationToken ct)
    {
        await _reloadLock.WaitAsync(ct);
        try
        {
            // Build lookup of new desired servers keyed by name.
            var desiredByName = new Dictionary<string, McpServerConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (var server in newConfig.Servers)
            {
                if (string.IsNullOrWhiteSpace(server.Name))
                {
                    _logger.LogWarning("Skipping MCP server with empty name in reload target");
                    continue;
                }

                desiredByName[server.Name] = server;
            }

            // 1) Remove connections whose server is no longer in the new config.
            var toRemove = _connections.Keys
                .Where(name => !desiredByName.ContainsKey(name))
                .ToList();
            foreach (var name in toRemove)
            {
                if (_connections.TryRemove(name, out var conn))
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

                UnregisterServerTools(name);
                _logger.LogInformation("MCP server '{Name}' disconnected (removed from config)", name);
            }

            // 2) Add or update.
            foreach (var desired in desiredByName.Values)
            {
                if (_connections.TryGetValue(desired.Name, out var existing))
                {
                    if (ServerConfigChanged(existing.Config, desired))
                    {
                        _logger.LogInformation("MCP server '{Name}' config changed — reconnecting", desired.Name);
                        try
                        {
                            await existing.DisposeAsync();
                        }
                        catch
                        {
                            /* best effort */
                        }

                        _connections.TryRemove(desired.Name, out _);
                        UnregisterServerTools(desired.Name);
                        await ConnectServerAsync(desired, ct);
                    }
                    else
                    {
                        _logger.LogDebug("MCP server '{Name}' unchanged — keeping current connection", desired.Name);
                    }
                }
                else
                {
                    await ConnectServerAsync(desired, ct);
                }
            }

            _initialized = true;
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    /// <summary>Unregister every tool entry whose name starts with <c>mcp.{serverName}.</c>.</summary>
    private void UnregisterServerTools(string serverName)
    {
        if (_toolRegistryService == null)
            return;

        var prefix = $"mcp.{serverName}.";
        var toRemove = _toolRegistryService.GetAllEntries()
            .Where(e => e.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Name)
            .ToList();

        foreach (var toolName in toRemove)
        {
            _toolRegistryService.UnregisterEntry(toolName);
        }
    }

    /// <summary>
    ///     Compare two <see cref="McpServerConfig"/> for changes that warrant a reconnect.
    ///     We compare only fields that affect the live connection — name is implicit
    ///     (we matched on it), and the rest are treated as the connection identity.
    /// </summary>
    private static bool ServerConfigChanged(McpServerConfig a, McpServerConfig b)
    {
        if (!string.Equals(a.Transport, b.Transport, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.Equals(a.Command, b.Command, StringComparison.Ordinal))
            return true;
        if (!string.Equals(a.Endpoint, b.Endpoint, StringComparison.OrdinalIgnoreCase))
            return true;
        if (a.Enabled != b.Enabled)
            return true;
        if (a.HealthCheckEnabled != b.HealthCheckEnabled)
            return true;
        if (a.TimeoutSeconds != b.TimeoutSeconds)
            return true;
        if (!ArgsEqual(a.Args, b.Args))
            return true;
        return false;
    }

    private static bool ArgsEqual(List<string> a, List<string> b)
    {
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                return false;
        }
        return true;
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

            _connections[cfg.Name] = new McpServerConnection(cfg.Name, cfg, client, state);
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
            _connections[cfg.Name] = new McpServerConnection(cfg.Name, cfg, null, state);
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

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var conn in _connections.Values)
        {
            await conn.DisposeAsync();
        }

        _connections.Clear();
        _reloadLock.Dispose();
    }
}

/// <summary>
///     Holds a connected MCP client, the originating config (for diff-based hot-reload),
///     and its state.
/// </summary>
internal sealed class McpServerConnection : IAsyncDisposable
{
    public McpServerConnection(string name, McpServerConfig config, McpClient? client, McpServerState state)
    {
        Name = name;
        Config = config;
        Client = client;
        State = state;
    }

    public string Name { get; }
    public McpServerConfig Config { get; }
    public McpClient? Client { get; }
    public McpServerState State { get; }

    public async ValueTask DisposeAsync()
    {
        if (Client != null)
            await Client.DisposeAsync();
    }
}
