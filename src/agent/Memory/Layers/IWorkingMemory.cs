namespace Hercules.Memory.Layers;

/// <summary>
///     Short-lived, session-scoped key-value store for mid-session reasoning,
///     scratchpad, and transient facts. Not persisted to disk.
/// </summary>
public interface IWorkingMemory
{
    /// <summary>Store a value with metadata.</summary>
    void Set(string key, string value, MemoryEntry? entry = null);

    /// <summary>Retrieve a value by key.</summary>
    string? Get(string key);

    /// <summary>Get all entries (for context assembly).</summary>
    IReadOnlyDictionary<string, (string Value, MemoryEntry Entry)> GetAll();

    /// <summary>Remove a key.</summary>
    bool Remove(string key);

    /// <summary>Clear all working memory (end of session).</summary>
    void Clear();

    /// <summary>Count of entries.</summary>
    int Count { get; }
}
