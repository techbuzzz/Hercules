namespace Hercules.SkillSdk;

/// <summary>
///     Base implementation of <see cref="IHerculesSkillContext"/>.
///     File-based skills receive an instance created by the agent runtime.
///     This base class is safe to use directly in unit tests.
/// </summary>
public abstract class SkillContextBase : IHerculesSkillContext
{
    public abstract IHttpClient Http { get; }
    public abstract IMcpClient Mcp { get; }
    public abstract ILlmClient Llm { get; }
    public abstract IMemoryClient Memory { get; }
    public abstract ISkillLogger Logger { get; }
    public abstract ISessionContext Session { get; }

    public virtual string? GetConfig(string keyPath)
    {
        // Default: no configuration access. Concrete agent implementation overrides.
        return null;
    }
}
