namespace Hercules.Memory.Layers;

/// <summary>
///     Long-lived, durable fact store backed by Markdown + JSON sidecars.
///     Facts can have TTL (permanent if 0). Supports redaction by sensitivity.
/// </summary>
public interface IDurableFactsStore
{
    /// <summary>Store a fact with metadata. Overwrites existing key.</summary>
    Task StoreFactAsync(string key, string value, MemoryEntry entry, CancellationToken ct = default);

    /// <summary>Retrieve a fact by key. Returns null if not found or expired.</summary>
    Task<(string? Value, MemoryEntry? Entry)?> GetFactAsync(string key, CancellationToken ct = default);

    /// <summary>Delete a fact by key.</summary>
    Task<bool> DeleteFactAsync(string key, CancellationToken ct = default);

    /// <summary>List all fact keys.</summary>
    Task<IReadOnlyList<string>> ListFactKeysAsync(CancellationToken ct = default);

    /// <summary>Search facts by tag or key prefix.</summary>
    Task<IReadOnlyList<(string Key, string Value, MemoryEntry Entry)>> SearchFactsAsync(
        string? tag = null,
        string? keyPrefix = null,
        bool includeExpired = false,
        CancellationToken ct = default);

    /// <summary>Remove all expired facts.</summary>
    Task<int> CleanupExpiredAsync(CancellationToken ct = default);
}
