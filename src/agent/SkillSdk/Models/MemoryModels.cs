namespace Hercules.SkillSdk;

/// <summary>
///     Memory lifetime scope for skill writes.
/// </summary>
public enum SkillMemoryScope
{
    /// <summary>
    ///     Transient memory scoped to the current skill execution only.
    ///     Not visible after the skill run ends.
    /// </summary>
    Execution,

    /// <summary>
    ///     Memory scoped to the current user session.
    ///     Cleared when the session ends.
    /// </summary>
    Session,

    /// <summary>
    ///     Durable long-term fact stored across sessions.
    /// </summary>
    Durable,

    /// <summary>
    ///     Append-only episodic memory (session summaries).
    /// </summary>
    Episodic
}

/// <summary>
///     One memory entry returned to a file-based skill.
/// </summary>
public sealed class SkillMemoryEntry
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public SkillMemoryScope Scope { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<string> Tags { get; set; } = new();
}
