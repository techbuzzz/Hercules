using Hercules.Lifecycle;
using Xunit;

namespace Hercules.Agent.Tests.Lifecycle;

public class AgentLifecycleStateHolderTests
{
    [Fact]
    public void Defaults_ToRunning()
    {
        var holder = new AgentLifecycleStateHolder();
        Assert.Equal(AgentLifecycleState.Running, holder.State);
        Assert.False(holder.IsDraining);
        Assert.False(holder.IsShuttingDown);
    }

    [Fact]
    public void SetState_UpdatesState()
    {
        var holder = new AgentLifecycleStateHolder();

        holder.SetState(AgentLifecycleState.Draining);

        Assert.Equal(AgentLifecycleState.Draining, holder.State);
        Assert.True(holder.IsDraining);
        Assert.True(holder.IsShuttingDown);
    }

    [Fact]
    public void SetState_FiresEvent()
    {
        var holder = new AgentLifecycleStateHolder();
        var fired = new List<AgentLifecycleState>();
        holder.StateChanged += s => fired.Add(s);

        holder.SetState(AgentLifecycleState.Draining);
        holder.SetState(AgentLifecycleState.Stopped);
        holder.SetState(AgentLifecycleState.Decommissioned);

        Assert.Equal(new[]
        {
            AgentLifecycleState.Draining,
            AgentLifecycleState.Stopped,
            AgentLifecycleState.Decommissioned
        }, fired);
    }
}
