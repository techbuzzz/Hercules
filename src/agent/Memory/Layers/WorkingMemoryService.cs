using System.Collections.Concurrent;

namespace Hercules.Memory.Layers;

/// <summary>
///     Short-lived, session-scoped in-memory key-value store.
///     Not persisted. Thread-safe. Enforces capacity limit.
/// </summary>
public sealed class WorkingMemoryService : IWorkingMemory
{
    private readonly ConcurrentDictionary<string, (string Value, MemoryEntry Entry)> _store = new();
    private readonly int _maxEntries;

    public WorkingMemoryService(LayeredMemoryConfig? config = null)
    {
        _maxEntries = config?.MaxWorkingMemoryEntries ?? 100;
    }

    public int Count => _store.Count;

    public void Set(string key, string value, MemoryEntry? entry = null)
    {
        entry ??= new MemoryEntry("working_memory");
        _store[key] = (value, entry);

        // Evict oldest if over capacity
        if (_store.Count > _maxEntries)
        {
            // Remove a random entry (approximate LRU)
            if (_store.TryRemove(KeyOfOldest(), out _))
            {
                // evicted
            }
        }
    }

    public string? Get(string key)
    {
        return _store.TryGetValue(key, out var pair) ? pair.Value : null;
    }

    public IReadOnlyDictionary<string, (string Value, MemoryEntry Entry)> GetAll()
    {
        return _store.ToDictionary(
            static kvp => kvp.Key,
            static kvp => kvp.Value);
    }

    public bool Remove(string key) => _store.TryRemove(key, out _);

    public void Clear() => _store.Clear();

    private string KeyOfOldest()
    {
        return _store.MinBy(static kvp => kvp.Value.Entry.CreatedAt).Key;
    }
}
