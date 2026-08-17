using System.Text.Json;
using Hercules.SkillSdk;
using Microsoft.Extensions.Logging;

namespace Hercules.CodeExecution.SkillSdkAdapters;

/// <summary>
///     Agent-side implementation of <see cref="IMcpClient"/> for file-based skills.
///     Routes through the agent's ToolRegistry to invoke registered tools.
///     In the future this will delegate to the real ModelContextProtocol client SDK.
/// </summary>
public sealed class McpClientAdapter : IMcpClient
{
    private readonly Tools.ToolRegistry _registry;
    private readonly ILogger _logger;

    public McpClientAdapter(Tools.ToolRegistry registry, ILogger logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public Task<IReadOnlyList<string>> ListToolsAsync(string serverName, CancellationToken ct = default)
    {
        var prefix = $"mcp:{serverName}:";
        var names = _registry.All
            .Where(t => t.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Name[prefix.Length..])
            .ToList();

        _logger.LogInformation("SkillSdk MCP list tools for '{ServerName}': {Count} found", serverName, names.Count);
        return Task.FromResult<IReadOnlyList<string>>(names);
    }

    public async Task<SkillMcpResponse> InvokeAsync(SkillMcpRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var toolName = string.IsNullOrWhiteSpace(request.ServerName)
            ? request.ToolName
            : $"mcp:{request.ServerName}:{request.ToolName}";

        var tool = _registry.All.FirstOrDefault(t =>
            string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));

        if (tool is null)
        {
            _logger.LogWarning("SkillSdk MCP tool '{ToolName}' not registered", toolName);
            return new SkillMcpResponse
            {
                Success = false,
                Error = $"MCP tool '{toolName}' is not registered on this agent"
            };
        }

        var argsJson = request.Arguments is null
            ? "{}"
            : JsonSerializer.Serialize(request.Arguments);

        try
        {
            var result = await tool.ExecuteAsync(argsJson, ct);
            _logger.LogInformation("SkillSdk MCP invoked '{ToolName}': success={Success}", toolName, result.Success);

            return new SkillMcpResponse
            {
                Success = result.Success,
                Output = result.Output,
                Error = result.Error
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SkillSdk MCP invocation failed for '{ToolName}'", toolName);
            return new SkillMcpResponse
            {
                Success = false,
                Error = $"MCP invocation error: {ex.GetType().Name}: {ex.Message}"
            };
        }
    }
}
