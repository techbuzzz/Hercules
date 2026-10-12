using Hercules.SkillSdk;
using Microsoft.Extensions.Logging;

namespace Hercules.CodeExecution.SkillSdkAdapters;

/// <summary>
///     Agent-side implementation of <see cref="ISkillLogger"/> for file-based skills.
///     Forwards structured logs to the agent's ILogger.
/// </summary>
public sealed class SkillLoggerAdapter : ISkillLogger
{
    private readonly ILogger _logger;
    private readonly string _skillName;

    public SkillLoggerAdapter(ILogger logger, string skillName)
    {
        _logger = logger;
        _skillName = skillName;
    }

    public void Debug(string message)
    {
        _logger.LogDebug("[{SkillName}] {Message}", _skillName, message);
    }

    public void Info(string message)
    {
        _logger.LogInformation("[{SkillName}] {Message}", _skillName, message);
    }

    public void Warning(string message)
    {
        _logger.LogWarning("[{SkillName}] {Message}", _skillName, message);
    }

    public void Error(string message)
    {
        _logger.LogError("[{SkillName}] {Message}", _skillName, message);
    }
}
