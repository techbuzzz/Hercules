namespace Hercules.SkillSdk;

/// <summary>
///     Structured logger for file-based skills.
///     Logs are forwarded to the agent's logger and are visible in Hercules Studio.
/// </summary>
public interface ISkillLogger
{
    void Debug(string message);
    void Info(string message);
    void Warning(string message);
    void Error(string message);
}
