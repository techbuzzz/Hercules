namespace Hercules.Offline;

/// <summary>
///     Abstraction for the offline outbox store (task_060).
///     Default implementation: SqliteOutboxStore.
/// </summary>
public interface IOutboxStore
{
    /// <summary>
    ///     Enqueue a single item. Bounded queue: may drop oldest synced items if cap is reached.
    /// </summary>
    Task EnqueueAsync(OutboxItem item, CancellationToken ct = default);

    /// <summary>
    ///     Retrieve pending items sorted by CreatedAt ASC (within priority tiers).
    /// </summary>
    Task<IReadOnlyList<OutboxItem>> GetPendingAsync(CancellationToken ct = default);

    /// <summary>
    ///     Retrieve pending items of a specific type.
    /// </summary>
    Task<IReadOnlyList<OutboxItem>> GetPendingByTypeAsync(OutboxItemType type, CancellationToken ct = default);

    /// <summary>
    ///     Mark an item as synced (removes from queue).
    /// </summary>
    Task MarkSyncedAsync(string itemId, CancellationToken ct = default);

    /// <summary>
    ///     Mark an item as failed and record the error.
    /// </summary>
    Task MarkFailedAsync(string itemId, string error, CancellationToken ct = default);

    /// <summary>
    ///     Increment retry count for an item.
    /// </summary>
    Task IncrementRetryAsync(string itemId, string? error = null, CancellationToken ct = default);

    /// <summary>
    ///     Count of pending (not-yet-synced) items.
    /// </summary>
    Task<int> GetPendingCountAsync(CancellationToken ct = default);

    /// <summary>
    ///     Count of pending items by type.
    /// </summary>
    Task<int> GetPendingCountByTypeAsync(OutboxItemType type, CancellationToken ct = default);

    /// <summary>
    ///     Check whether an item with this ItemId already exists (any status) — for deduplication.
    /// </summary>
    Task<bool> ExistsAsync(string itemId, CancellationToken ct = default);

    /// <summary>
    ///     Delete items older than the given cutoff (e.g. expired by TTL).
    ///     Returns the count of deleted items.
    /// </summary>
    Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct = default);

    /// <summary>
    ///     Prune oldest synced items to enforce the queue cap.
    ///     Returns the count of pruned items.
    /// </summary>
    Task<int> PruneSyncedToCapAsync(int maxSynced, CancellationToken ct = default);
}
