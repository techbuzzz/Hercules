using Hercules.SkillSdk;

namespace Hercules.CodeExecution;

/// <summary>
///     Factory that creates an agent-side <see cref="IHerculesSkillContext"/> for one skill execution.
/// </summary>
public interface ISkillContextFactory
{
    /// <summary>
    ///     Create a new context scoped to a single skill run.
    /// </summary>
    IHerculesSkillContext Create(string sessionId, string? userId, string skillName, CancellationToken cancellationToken = default);
}
