namespace Hercules.SkillSdk;

/// <summary>
///     Safe MCP client for file-based skills.
///     Only MCP tools registered on the agent may be invoked.
/// </summary>
public interface IMcpClient
{
    /// <summary>
    ///     List tool names available on a registered MCP server.
    ///     Returns empty list if the server is not registered.
    /// </summary>
    Task<IReadOnlyList<string>> ListToolsAsync(string serverName, CancellationToken ct = default);

    /// <summary>
    ///     Invoke an MCP tool registered on the agent.
    /// </summary>
    Task<SkillMcpResponse> InvokeAsync(SkillMcpRequest request, CancellationToken ct = default);
}
