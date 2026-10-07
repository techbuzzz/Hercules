namespace Hercules.SkillSdk;

/// <summary>
///     Aggregate context injected into file-based C# skills.
///     Combines all safe SkillSdk APIs plus read-only configuration access.
/// </summary>
public interface IHerculesSkillContext
{
    /// <summary>Safe HTTP client (domain-restricted).</summary>
    IHttpClient Http { get; }

    /// <summary>Safe MCP client.</summary>
    IMcpClient Mcp { get; }

    /// <summary>Safe LLM client.</summary>
    ILlmClient Llm { get; }

    /// <summary>Safe memory client.</summary>
    IMemoryClient Memory { get; }

    /// <summary>Structured logger.</summary>
    ISkillLogger Logger { get; }

    /// <summary>Current session info.</summary>
    ISessionContext Session { get; }

    /// <summary>
    ///     Read a configuration value by key path (e.g. "Http.TimeoutSeconds").
    ///     Returns null if the key is not found or access is denied.
    ///     Only safe, non-secret configuration keys are exposed.
    /// </summary>
    string? GetConfig(string keyPath);
}
