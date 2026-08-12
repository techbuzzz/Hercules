using System.Text.Json;
using Hercules.Config;
using Hercules.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using CallToolResult = ModelContextProtocol.Protocol.CallToolResult;
using ListToolsResult = ModelContextProtocol.Protocol.ListToolsResult;
using JsonOptions = System.Text.Json.JsonSerializerOptions;

namespace Hercules.Mcp;

/// <summary>
///     Hosts Hercules built-in tools as an MCP server over stdio.
///     Runs as a <see cref="BackgroundService"/> so it doesn't block startup.
///     External MCP clients can connect and call Hercules tools via the MCP protocol.
/// </summary>
public sealed class McpServerHost : BackgroundService
{
    private readonly McpConfig _config;
    private readonly IEnumerable<ITool> _tools;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<McpServerHost> _logger;
    private readonly IHostApplicationLifetime _appLifetime;

    public McpServerHost(
        McpConfig config,
        IEnumerable<ITool> tools,
        ILoggerFactory loggerFactory,
        ILogger<McpServerHost> logger,
        IHostApplicationLifetime appLifetime)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _loggerFactory = loggerFactory;
        _logger = logger;
        _appLifetime = appLifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabledServers = _config.Servers
            .Where(s => s.Enabled)
            .ToList();

        if (enabledServers.Count == 0)
        {
            _logger.LogInformation("No MCP servers enabled");
            return;
        }

        // For Hercules, we expose built-in tools as a single stdio MCP server.
        // Only the first enabled server config is used for the stdio transport.
        var cfg = enabledServers[0];
        var toolList = _tools.ToList();

        _logger.LogInformation(
            "MCP server '{Name}' starting on stdio — exposing {ToolCount} tools",
            cfg.Name, toolList.Count);

        await RunServerAsync(cfg, toolList, stoppingToken);
    }

    private async Task RunServerAsync(McpServerConfig cfg, IReadOnlyList<ITool> toolList, CancellationToken ct)
    {
        var jsonOpts = new JsonOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var options = new McpServerOptions
        {
            ServerInfo = new Implementation
            {
                Name = cfg.Name ?? "hercules",
                Version = "1.0"
            },
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (request, _) =>
                {
                    var tools = toolList
                        .Select(t => new HerculesMcpServerTool(t, jsonOpts).ToMcpTool())
                        .ToList();

                    return ValueTask.FromResult(new ListToolsResult { Tools = tools });
                },

                CallToolHandler = async (request, callCt) =>
                {
                    var toolName = request.Params?.Name;
                    if (string.IsNullOrEmpty(toolName))
                    {
                        return new CallToolResult
                        {
                            IsError = true,
                            Content = [new TextContentBlock { Text = "Missing tool name." }]
                        };
                    }

                    var tool = toolList.FirstOrDefault(t =>
                        string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));

                    if (tool == null)
                    {
                        return new CallToolResult
                        {
                            IsError = true,
                            Content = [new TextContentBlock { Text = $"Unknown tool: {toolName}" }]
                        };
                    }

                    string argsJson;
                    if (request.Params?.Arguments != null && request.Params.Arguments.Count > 0)
                    {
                        argsJson = JsonSerializer.Serialize(request.Params.Arguments, jsonOpts);
                    }
                    else
                    {
                        argsJson = "{}";
                    }

                    var result = await tool.ExecuteAsync(argsJson, callCt);

                    return new CallToolResult
                    {
                        IsError = !result.Success,
                        Content = [new TextContentBlock
                        {
                            Text = result.Success
                                ? result.Output
                                : $"Error: {result.Error}\n{result.Output}"
                        }]
                    };
                }
            }
        };

        await using var server = McpServer.Create(new StdioServerTransport(cfg.Name ?? "hercules", _loggerFactory), options);

        try
        {
            await server.RunAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP server '{Name}' failed: {Error}", cfg.Name, ex.Message);
        }
        finally
        {
            // Signal app shutdown when stdio server exits (expected for stdio transport)
            _appLifetime.StopApplication();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("MCP server host stopping...");
        await base.StopAsync(cancellationToken);
    }
}
