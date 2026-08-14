namespace Hercules.Agent;

/// <summary>
///     Singleton store of per-session state. Decouples per-session mutable data
///     from the <see cref="AgentCore" /> singleton so that parallel HTTP requests
///     cannot leak state across sessions (task_075 — H7 fix: AgentCore singleton with
///     mutable per-session state).
///     Implementations: <see cref="InMemorySessionStateStore" /> (in-process),
///     future <c>SqliteSessionStateStore</c> for cross-process / cold-restart resilience.
/// </summary>
public interface ISessionStateStore
{
    /// <summary>
    ///     Get or create a <see cref="SessionState" /> for the given sessionId.
    ///     The same instance is returned for every call with the same id (within process lifetime).
    /// </summary>
    SessionState GetOrCreate(string sessionId);

    /// <summary>Remove a session (e.g. on graceful shutdown or explicit cleanup).</summary>
    bool Remove(string sessionId);

    /// <summary>Enumerate all known sessions (read-only snapshots).</summary>
    IEnumerable<SessionState> ListSessions();

    /// <summary>Number of tracked sessions.</summary>
    int Count { get; }
}
