namespace Hercules.SkillSdk;

/// <summary>
///     Request to invoke an MCP tool registered on the agent.
/// </summary>
public sealed class SkillMcpRequest
{
    /// <summary>Name of the MCP server registered on the agent.</summary>
    public string ServerName { get; set; } = "";

    /// <summary>Name of the tool to invoke on that server.</summary>
    public string ToolName { get; set; } = "";

    /// <summary>Tool arguments. Shape is defined by the target tool's input schema.</summary>
    public Dictionary<string, object>? Arguments { get; set; }
}

/// <summary>
///     Result of an MCP tool invocation.
/// </summary>
public sealed class SkillMcpResponse
{
    /// <summary><c>true</c> when the tool completed without error.</summary>
    public bool Success { get; set; }

    /// <summary>Tool output rendered as text.</summary>
    public string Output { get; set; } = "";

    /// <summary>Failure description, or <c>null</c> on success.</summary>
    public string? Error { get; set; }
}