using System.Text.Json;
using ModelContextProtocol.Protocol;
using CallToolResult = ModelContextProtocol.Protocol.CallToolResult;

namespace Hercules.Mcp;

/// <summary>
///     Abstraction over an MCP client tool for testability.
///     Wraps <c>ModelContextProtocol.Client.McpClientTool</c> in production code.
/// </summary>
public interface IMcpClientTool
{
    string Name { get; }
    string? Description { get; }
    JsonElement JsonSchema { get; }

    ValueTask<CallToolResult> CallAsync(
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken);
}
