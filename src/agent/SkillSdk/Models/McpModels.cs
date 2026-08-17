namespace Hercules.SkillSdk;

/// <summary>
///     Request to invoke an MCP tool registered on the agent.
/// </summary>
public sealed class SkillMcpRequest
{
    public string ServerName { get; set; } = "";
    public string ToolName { get; set; } = "";
    public Dictionary<string, object>? Arguments { get; set; }
}

/// <summary>
///     Result of an MCP tool invocation.
/// </summary>
public sealed class SkillMcpResponse
{
    public bool Success { get; set; }
    public string Output { get; set; } = "";
    public string? Error { get; set; }
}
