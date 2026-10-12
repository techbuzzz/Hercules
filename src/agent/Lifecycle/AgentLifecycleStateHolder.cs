namespace Hercules.Lifecycle;

/// <summary>
///     Local agent lifecycle state. Shared between <see cref="LifecycleService" /> (writer)
///     and <see cref="Hercules.Agent.AgentCore" /> / <see cref="Hercules.WebApi.Middleware.DrainMiddleware" />
///     (readers) so a draining state set by the lifecycle service is immediately visible to
///     request-handling code without going through the full <see cref="ILifecycleService" /> contract
///     (which would introduce a cycle: LifecycleService depends on AgentCore).
///     Specification: task_080.
/// </summary>
public interface IAgentLifecycleState
{
    /// <summary>Current lifecycle state.</summary>
    AgentLifecycleState State { get; }

    /// <summary>True when the agent has been told to stop accepting new requests but may still be finishing in-flight work.</summary>
    bool IsDraining { get; }

    /// <summary>True when the agent can no longer serve requests (Draining / Stopped / Decommissioned).</summary>
    bool IsShuttingDown { get; }

    /// <summary>Transition to a new state. The state is read by AgentCore on every request, so writes are visible-lock-free.</summary>
    void SetState(AgentLifecycleState state);

    /// <summary>Raised after a state change. Used by tests and observability code.</summary>
    event Action<AgentLifecycleState>? StateChanged;
}

/// <summary>Local agent lifecycle state.</summary>
public enum AgentLifecycleState
{
    Running,
    Draining,
    Stopped,
    Decommissioned
}

/// <summary>
///     Default lock-free implementation of <see cref="IAgentLifecycleState" />. Reads happen on every
///     request hot-path, so we use <see cref="Volatile" /> writes of an int-backed enum instead of a lock.
/// </summary>
public sealed class AgentLifecycleStateHolder : IAgentLifecycleState
{
    private int _state = (int)AgentLifecycleState.Running;

    public AgentLifecycleState State => (AgentLifecycleState)Volatile.Read(ref _state);

    public bool IsDraining => State == AgentLifecycleState.Draining;

    public bool IsShuttingDown => State switch
    {
        AgentLifecycleState.Running => false,
        _ => true
    };

    public void SetState(AgentLifecycleState state)
    {
        Volatile.Write(ref _state, (int)state);
        StateChanged?.Invoke(state);
    }

    public event Action<AgentLifecycleState>? StateChanged;
}
