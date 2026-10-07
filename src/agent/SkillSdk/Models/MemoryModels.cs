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
    /// <summary>Key the value is stored under within its scope.</summary>
    public string Key { get; set; } = "";

    /// <summary>Stored value.</summary>
    public string Value { get; set; } = "";

    /// <summary>Memory layer this entry belongs to.</summary>
    public SkillMemoryScope Scope { get; set; }

    /// <summary>UTC timestamp of when the entry was written.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Tags used to find this entry via <c>SearchAsync</c>.</summary>
    public List<string> Tags { get; set; } = new();
}
