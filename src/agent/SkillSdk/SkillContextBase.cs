namespace Hercules.SkillSdk;

/// <summary>
///     Base implementation of <see cref="IHerculesSkillContext"/>.
///     File-based skills receive an instance created by the agent runtime.
///     This base class is safe to use directly in unit tests.
/// </summary>
public abstract class SkillContextBase : IHerculesSkillContext
{
    /// <inheritdoc cref="IHerculesSkillContext.Http"/>
    public abstract IHttpClient Http { get; }

    /// <inheritdoc cref="IHerculesSkillContext.Mcp"/>
    public abstract IMcpClient Mcp { get; }

    /// <inheritdoc cref="IHerculesSkillContext.Llm"/>
    public abstract ILlmClient Llm { get; }

    /// <inheritdoc cref="IHerculesSkillContext.Memory"/>
    public abstract IMemoryClient Memory { get; }

    /// <inheritdoc cref="IHerculesSkillContext.Logger"/>
    public abstract ISkillLogger Logger { get; }

    /// <inheritdoc cref="IHerculesSkillContext.Session"/>
    public abstract ISessionContext Session { get; }

    /// <inheritdoc cref="IHerculesSkillContext.GetConfig"/>
    public virtual string? GetConfig(string keyPath)
    {
        // Default: no configuration access. Concrete agent implementation overrides.
        return null;
    }
}
