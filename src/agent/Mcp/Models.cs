namespace Hercules.Mcp;

/// <summary>
///     Health state for a connected MCP server.
/// </summary>
public enum McpServerHealthStatus
{
    Unknown,

    /// <summary>
    ///     Server is present in config but <c>Enabled = false</c>, so
    ///     <see cref="McpClientService"/> holds no connection for it. Reported
    ///     explicitly rather than as <see cref="Unknown"/> so Studio can tell
    ///     "configured off" apart from "configured on but never connected".
    /// </summary>
    Disabled,

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
