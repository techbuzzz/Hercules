namespace Hercules.Memory.Layers;

/// <summary>
///     Metadata attached to every memory write.
///     Enables filtering, redaction, TTL, and source attribution.
/// </summary>
public sealed record MemoryEntry
{
    /// <summary>Who/what wrote this entry: "session_extract", "user", "agent", "skill:{id}", etc.</summary>
    public string Source { get; init; } = "unknown";

    /// <summary>Confidence level of the stored information.</summary>
    public MemoryConfidence Confidence { get; init; } = MemoryConfidence.Medium;

    /// <summary>Time-to-live in minutes. 0 = permanent.</summary>
    public int TtlMinutes { get; init; } = 0;

    /// <summary>Sensitivity level for redaction and access control.</summary>
    public MemorySensitivity Sensitivity { get; init; } = MemorySensitivity.Internal;

    /// <summary>UTC timestamp of creation. Defaults to DateTime.UtcNow for new entries.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Arbitrary tags for filtering (e.g. "user_fact", "project_alpha").</summary>
    public List<string> Tags { get; init; } = new();

    /// <summary>
    ///     True if the entry has expired based on its TTL.
    /// </summary>
    public bool IsExpired => TtlMinutes > 0 && DateTime.UtcNow > CreatedAt.AddMinutes(TtlMinutes);

    /// <summary>
    ///     True if this entry should be excluded from LLM context based on sensitivity.
    /// </summary>
    public bool ShouldRedact(bool redactionEnabled = true)
        => redactionEnabled && Sensitivity is MemorySensitivity.Sensitive or MemorySensitivity.Restricted;

    /// <summary>Default constructor (required for deserialization).</summary>
    public MemoryEntry() { }

    /// <summary>
    ///     Full constructor for programmatic creation.
    /// </summary>
    public MemoryEntry(
        string source,
        MemoryConfidence confidence,
        int ttlMinutes,
        MemorySensitivity sensitivity,
        DateTime createdAt,
        List<string> tags)
    {
        Source = source;
        Confidence = confidence;
        TtlMinutes = ttlMinutes;
        Sensitivity = sensitivity;
        CreatedAt = createdAt == default ? DateTime.UtcNow : createdAt;
        Tags = tags ?? new List<string>();
    }

    /// <summary>
    ///     Convenience constructor with sensible defaults.
    /// </summary>
    public MemoryEntry(string source, MemoryConfidence confidence = MemoryConfidence.Medium)
    {
        Source = source;
        Confidence = confidence;
    }
}

public enum MemoryConfidence
{
    High,
    Medium,
    Low
}

public enum MemorySensitivity
{
    /// <summary>Can appear in LLM context without restriction.</summary>
    Public,

    /// <summary>Default; internal business context.</summary>
    Internal,

    /// <summary>Requires redaction from logs and LLM context unless explicitly needed.</summary>
    Sensitive,

    /// <summary>Must never appear in LLM context or logs.</summary>
    Restricted
}
