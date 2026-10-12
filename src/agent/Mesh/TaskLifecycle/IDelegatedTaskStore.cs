namespace Hercules.Mesh.TaskLifecycle;

/// <summary>
///     Persistence contract for inter-agent delegated tasks (task_106).
///     The <see cref="TaskLifecycleProtocol"/> is the in-memory state machine
///     and orchestrator; this store is the durable source of truth that
///     survives process restarts. The same in-memory protocol surface stays
///     the same — the only change is that the protocol write-throughs every
///     state transition through this interface.
/// </summary>
/// <remarks>
///     Implementations must be thread-safe. The default in-tree
///     implementation is <see cref="SqliteDelegatedTaskStore"/>; a future
///     PostgreSQL variant (mirroring <c>PostgresSessionStore</c>) can plug
///     in here without touching the protocol.
/// </remarks>
public interface IDelegatedTaskStore
{
    /// <summary>True when the underlying connection is reachable.</summary>
    bool IsHealthy();

    /// <summary>Insert or update a task (upsert by <see cref="DelegatedTask.TaskId"/>).</summary>
    Task SaveAsync(DelegatedTask task, CancellationToken ct = default);

    /// <summary>Load a task by id. Returns null if not present.</summary>
    Task<DelegatedTask?> GetAsync(string taskId, CancellationToken ct = default);

    /// <summary>
    ///     List tasks, optionally filtered by state and/or parent request id.
    ///     <paramref name="limit"/> caps the result set (default 100).
    /// </summary>
    Task<List<DelegatedTask>> ListAsync(
        DelegatedTaskState? stateFilter = null,
        string? parentRequestId = null,
        int limit = 100,
        CancellationToken ct = default);

    /// <summary>
    ///     List all non-terminal tasks (Accepted / Working / AwaitingInput).
    ///     Used at startup to rebuild the in-memory cache so pollers can
    ///     re-attach to a task that survived a restart.
    /// </summary>
    Task<List<DelegatedTask>> ListPendingAsync(CancellationToken ct = default);

    /// <summary>Delete a task by id (rare — used by ops tooling and tests).</summary>
    Task DeleteAsync(string taskId, CancellationToken ct = default);

    /// <summary>
    ///     Delete tasks whose <c>ExpiresAt &lt;= <paramref name="asOf"/></c> AND
    ///     that are in a terminal state. Returns the number of rows removed.
    /// </summary>
    Task<int> PruneExpiredAsync(DateTimeOffset asOf, CancellationToken ct = default);
}
