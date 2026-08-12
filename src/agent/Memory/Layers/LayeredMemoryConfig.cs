namespace Hercules.Memory.Layers;

/// <summary>
///     Configuration for layered memory.
/// </summary>
public sealed class LayeredMemoryConfig
{
    /// <summary>Maximum entries in working memory before eviction.</summary>
    public int MaxWorkingMemoryEntries { get; set; } = 100;

    /// <summary>How many recent episodes to include in context block.</summary>
    public int MaxEpisodesInContext { get; set; } = 5;

    /// <summary>Default TTL for facts without explicit TTL (minutes). 0 = permanent.</summary>
    public int DefaultFactTtlMinutes { get; set; } = 0;

    /// <summary>Enable sensitivity-based redaction from LLM context.</summary>
    public bool SensitivityRedactionEnabled { get; set; } = true;

    /// <summary>Maximum fact age before auto-cleanup (days). 0 = no cleanup.</summary>
    public int MaxFactAgeDays { get; set; } = 0;
}
