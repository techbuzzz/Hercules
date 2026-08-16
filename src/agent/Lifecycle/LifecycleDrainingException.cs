namespace Hercules.Lifecycle;

/// <summary>
///     Thrown by <see cref="Hercules.Agent.AgentCore.HandleAsync" /> when the agent is in
///     <see cref="AgentLifecycleState.Draining" /> (or any later terminal state). Carries
///     no payload — the caller (WebApi middleware / CLI) translates it into a 503 response
///     or a friendly console message.
///     Specification: task_080.
/// </summary>
public sealed class LifecycleDrainingException : Exception
{
    public LifecycleDrainingException(AgentLifecycleState state)
        : base($"Agent is {state} and not accepting new requests; retry after a short delay")
    {
        State = state;
    }

    public AgentLifecycleState State { get; }
}
