using Hercules.Mcp;
using ModelContextProtocol.Protocol;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     MCP server management endpoints.
///     GET /api/mcp/servers — all servers with status and tool count
///     GET /api/mcp/servers/{name} — server info + tools
///     POST /api/mcp/servers/reload — reload from config
/// </summary>
public static class McpController
{
    public static void MapMcpEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/mcp").WithTags("MCP");

        // GET /api/mcp/servers
        group.MapGet("/servers", (McpClientService mcpService) =>
        {
            var states = mcpService.ServerStates;
            return Results.Ok(new
            {
                count = states.Count,
                servers = states.Values.Select(s => new
                {
                    name = s.Name,
                    transport = s.Transport,
                    status = s.Status.ToString(),
                    toolCount = s.ToolCount,
                    connectedAt = s.ConnectedAt,
                    error = s.LastError
                }).ToList()
            });
        }).WithName("ListMcpServers").WithTags("MCP");

        // GET /api/mcp/servers/{name}
        group.MapGet("/servers/{name}", (string name, McpClientService mcpService) =>
        {
            var states = mcpService.ServerStates;
            if (!states.TryGetValue(name, out var state))
                return Results.NotFound(new { error = $"MCP server '{name}' not found" });

            return Results.Ok(new
            {
                name = state.Name,
                transport = state.Transport,
                status = state.Status.ToString(),
                toolCount = state.ToolCount,
                serverVersion = state.ServerVersion,
                connectedAt = state.ConnectedAt,
                error = state.LastError
            });
        }).WithName("GetMcpServer").WithTags("MCP");

        // POST /api/mcp/servers/reload
        group.MapPost("/servers/reload", async (McpClientService mcpService) =>
        {
            await mcpService.ReloadAsync();
            return Results.Ok(new { message = "MCP connections reloaded" });
        }).WithName("ReloadMcpServers").WithTags("MCP");
    }
}
