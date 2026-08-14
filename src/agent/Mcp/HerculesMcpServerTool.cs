using System.Text.Json;
using Hercules.Tools;
using ModelContextProtocol.Protocol;
using Tool = ModelContextProtocol.Protocol.Tool;

namespace Hercules.Mcp;

/// <summary>
///     Adapts a Hercules <see cref="ITool"/> to an MCP <see cref="Tool"/> for use in
///     <c>McpServerHost</c>'s <c>ListToolsHandler</c>.
///     Converts the tool's description and <c>ParametersSchema</c> into the JSON schema
///     expected by the MCP protocol.
/// </summary>
public sealed class HerculesMcpServerTool
{
    private readonly ITool _tool;
    private readonly JsonSerializerOptions _jsonOpts;

    public HerculesMcpServerTool(ITool tool, JsonSerializerOptions? jsonOpts = null)
    {
        _tool = tool ?? throw new ArgumentNullException(nameof(tool));
        _jsonOpts = jsonOpts ?? new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    }

    /// <summary>Creates an MCP <see cref="Tool"/> from the wrapped <paramref name="tool"/>.</summary>
    public Tool ToMcpTool()
    {
        return new Tool
        {
            Name = _tool.Name,
            Description = _tool.Description ?? _tool.Name,
            InputSchema = BuildInputSchema()
        };
    }

    private JsonElement BuildInputSchema()
    {
        if (!string.IsNullOrWhiteSpace(_tool.ParametersSchema))
        {
            try
            {
                using var doc = JsonDocument.Parse(_tool.ParametersSchema);
                return doc.RootElement.Clone();
            }
            catch
            {
                // Fall through to default
            }
        }

        // Default minimal schema — tool accepts any object arguments
        const string defaultSchema = """{"type":"object"}""";
        return JsonSerializer.Deserialize<JsonElement>(defaultSchema, _jsonOpts);
    }
}
