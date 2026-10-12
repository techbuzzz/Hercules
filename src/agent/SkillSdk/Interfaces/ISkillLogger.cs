namespace Hercules.SkillSdk;

/// <summary>
///     Structured logger for file-based skills.
///     Logs are forwarded to the agent's logger and are visible in Hercules Studio.
/// </summary>
public interface ISkillLogger
{
    /// <summary>Write a diagnostic message (verbose troubleshooting detail).</summary>
    void Debug(string message);

    /// <summary>Write an informational message (normal lifecycle events).</summary>
    void Info(string message);

    /// <summary>Write a warning (recoverable problem the skill worked around).</summary>
    void Warning(string message);

    /// <summary>Write an error (the skill could not complete part of its work).</summary>
    void Error(string message);
}
