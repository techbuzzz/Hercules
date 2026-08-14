namespace Hercules.Mcp;

/// <summary>
///     Health state for a connected MCP server.
/// </summary>
public enum McpServerHealthStatus
{
    Unknown,
    Healthy,
    Unhealthy,
    Disconnected
}

/// <summary>
///     Connection state and metadata for one MCP server.
/// </summary>
public sealed record McpServerState(
    string Name,
    string Transport,
    McpServerHealthStatus Status,
    DateTime? ConnectedAt,
    DateTime? LastErrorAt,
    string? LastError,
    int ToolCount,
    string? ServerVersion);
