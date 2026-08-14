using System.Text.Json;
using Hercules.Tools;
using Microsoft.Extensions.Logging;
using McpClient = ModelContextProtocol.Client.McpClient;
using McpClientTool = ModelContextProtocol.Client.McpClientTool;
using CallToolResult = ModelContextProtocol.Protocol.CallToolResult;

namespace Hercules.Mcp;

/// <summary>
///     Wraps an MCP client tool as a Hercules <see cref="ITool"/>.
///     Tool name is scoped to the server to avoid collisions:
///     <c>mcp.{serverName}.{toolName}</c>.
/// </summary>
public sealed class McpToolAdapter : ITool
{
    private readonly IMcpClientTool _tool;
    private readonly string _serverName;
    private readonly ILogger<McpToolAdapter> _logger;

    /// <summary>
    ///     Creates an adapter from an <see cref="IMcpClientTool"/> (testable path).
    /// </summary>
    public McpToolAdapter(IMcpClientTool tool, string serverName, ILogger<McpToolAdapter> logger)
    {
        _tool = tool ?? throw new ArgumentNullException(nameof(tool));
        _serverName = serverName ?? throw new ArgumentNullException(nameof(serverName));
        _logger = logger;
    }

    /// <summary>
    ///     Creates an adapter from a real <c>McpClientTool</c> (production path).
    /// </summary>
    public McpToolAdapter(McpClient client, McpClientTool tool, string serverName, ILogger<McpToolAdapter> logger)
        : this(new McpClientToolAdapter(client, tool), serverName, logger)
    {
    }

    /// <inheritdoc />
    public string Name => $"mcp.{_serverName}.{_tool.Name}";

    /// <inheritdoc />
    public string Description => _tool.Description ?? _tool.Name;

    /// <inheritdoc />
    public string? ParametersSchema
    {
        get
        {
            if (_tool.JsonSchema.ValueKind == JsonValueKind.Undefined)
                return null;

            try
            {
                return _tool.JsonSchema.GetRawText();
            }
            catch
            {
                return null;
            }
        }
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        Dictionary<string, object>? arguments = null;

        if (!string.IsNullOrWhiteSpace(argumentsJson))
        {
            try
            {
                arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(argumentsJson);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning("MCP tool '{Name}': failed to parse arguments JSON: {Error}", Name, ex.Message);
                return ToolResult.Fail($"Invalid arguments JSON: {ex.Message}");
            }
        }

        try
        {
            _logger.LogDebug("MCP tool '{Name}': calling with {ArgCount} arguments", Name, arguments?.Count ?? 0);

            var result = await _tool.CallAsync(
                arguments != null ? new Dictionary<string, object?>(arguments) : null,
                ct);

            var output = ExtractText(result);
            return result.IsError == true
                ? ToolResult.Fail(output)
                : ToolResult.Ok(output);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return ToolResult.Fail("Tool call cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP tool '{Name}': call failed", Name);
            return ToolResult.Fail($"MCP tool call failed: {ex.Message}");
        }
    }

    private static string ExtractText(CallToolResult result)
    {
        if (result.Content == null || result.Content.Count == 0)
            return "(no output)";

        var parts = new List<string>();
        foreach (var block in result.Content)
        {
            if (block is ModelContextProtocol.Protocol.TextContentBlock textBlock)
                parts.Add(textBlock.Text ?? "");
        }

        return parts.Count > 0 ? string.Join("\n", parts) : "(no text output)";
    }
}

/// <summary>
///     Production adapter: bridges a real <c>McpClientTool</c> to <see cref="IMcpClientTool"/>.
/// </summary>
internal sealed class McpClientToolAdapter : IMcpClientTool
{
    private readonly McpClient _client;
    private readonly McpClientTool _tool;

    public McpClientToolAdapter(McpClient client, McpClientTool tool)
    {
        _client = client;
        _tool = tool;
    }

    public string Name => _tool.Name;
    public string? Description => _tool.Description;
    public JsonElement JsonSchema => _tool.JsonSchema;

    public async ValueTask<CallToolResult> CallAsync(
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        return await _tool.CallAsync(arguments, null, null, cancellationToken);
    }
}
