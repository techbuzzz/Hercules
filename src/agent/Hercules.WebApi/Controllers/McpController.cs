using Hercules.Config;
using Hercules.Mcp;
using Hercules.WebApi.Contracts;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     MCP server management endpoints.
///     GET /api/mcp/servers — all servers with status, tool count and live config
///     GET /api/mcp/servers/{name} — server info + tools + live config
///     POST /api/mcp/servers/reload — reconnect from config
///
///     [Stage 5b] There is deliberately no add/remove endpoint. The write surface is
///     <c>PATCH /api/config</c>, which replaces <c>mcp.servers</c> wholesale and makes
///     <see cref="McpClientService"/> (an <c>IConfigReload</c>) diff the list and
///     connect/disconnect accordingly. The read endpoints therefore return the
///     configured definition alongside runtime state so the editor can round-trip a
///     server through that single patch.
/// </summary>
public static class McpController
{
    public static void MapMcpEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/mcp").WithTags("MCP");

        // GET /api/mcp/servers
        group.MapGet("/servers", (McpClientService mcpService, RuntimeConfigStore store) =>
        {
            // Config is the source of truth. Connection states are joined in by name
            // for status only — listing states as separate rows would let a client
            // round-trip a phantom entry back into config, because a state carries
            // no editable definition.
            var states = mcpService.ServerStates;

            var servers = store.Current.Mcp.Servers
                .Where(s => !string.IsNullOrWhiteSpace(s.Name))
                .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    states.TryGetValue(g.Key, out var state);
                    return Summarize(g.Key, g.First(), state);
                })
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Results.Ok(new McpServersListResponseDto
            {
                Count = servers.Count,
                Servers = servers
            });
        }).WithName("ListMcpServers").WithTags("MCP").Produces<McpServersListResponseDto>(200);

        // GET /api/mcp/servers/{name}
        group.MapGet("/servers/{name}", (string name, McpClientService mcpService, RuntimeConfigStore store) =>
        {
            var states = mcpService.ServerStates;
            states.TryGetValue(name, out var state);

            var config = store.Current.Mcp.Servers.FirstOrDefault(
                s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

            if (config == null && state == null)
            {
                return Results.NotFound(new { error = $"MCP server '{name}' not found" });
            }

            return Results.Ok(new McpServerDetailDto
            {
                Name = name,
                Transport = config?.Transport ?? state!.Transport,
                Status = StatusFor(config, state),
                ToolCount = state?.ToolCount ?? 0,
                ServerVersion = state?.ServerVersion,
                ConnectedAt = state?.ConnectedAt,
                Error = state?.LastError,
                Config = Definition(name, config)
            });
        }).WithName("GetMcpServer").WithTags("MCP").Produces<McpServerDetailDto>(200);

        // POST /api/mcp/servers/reload
        group.MapPost("/servers/reload", async (McpClientService mcpService) =>
        {
            await mcpService.ReloadAsync();
            return Results.Ok(new McpReloadResponseDto { Message = "MCP connections reloaded" });
        }).WithName("ReloadMcpServers").WithTags("MCP").Produces<McpReloadResponseDto>(200);
    }

    private static McpServerSummaryDto Summarize(string name, McpServerConfig? config, McpServerState? state)
    {
        return new McpServerSummaryDto
        {
            Name = name,
            Transport = config?.Transport ?? state?.Transport ?? "stdio",
            Status = StatusFor(config, state),
            ToolCount = state?.ToolCount ?? 0,
            ConnectedAt = state?.ConnectedAt,
            Error = state?.LastError,
            Config = Definition(name, config)
        };
    }

    /// <summary>
    ///     A configured server with no connection is <c>Disabled</c> when it is switched
    ///     off, and <c>Unknown</c> when it is on but has not been connected yet (first
    ///     start before initialisation, or a reload still in flight).
    /// </summary>
    private static string StatusFor(McpServerConfig? config, McpServerState? state)
    {
        if (state == null)
        {
            return (config?.Enabled ?? false)
                ? McpServerHealthStatus.Unknown.ToString()
                : McpServerHealthStatus.Disabled.ToString();
        }

        return state.Status.ToString();
    }

    private static McpServerDefinitionDto Definition(string name, McpServerConfig? config)
    {
        return new McpServerDefinitionDto
        {
            Name = config?.Name ?? name,
            Transport = config?.Transport ?? "stdio",
            Command = config?.Command,
            Args = config?.Args ?? new List<string>(),
            Endpoint = config?.Endpoint,
            Enabled = config?.Enabled ?? false,
            HealthCheckEnabled = config?.HealthCheckEnabled ?? true,
            TimeoutSeconds = config?.TimeoutSeconds ?? 30
        };
    }
}