using Hercules.Mesh.Abstractions;
using Hercules.Mesh.Backends.Nats;
using NATS.Client.JetStream;
using Xunit;

namespace Hercules.Agent.Tests.Mesh.Backends.Nats;

/// <summary>
///     Unit tests for <see cref="NatsTaskInFlightTracker"/> (task_074).
///     Covers AddOrReplace, Get, Remove, Count, and the action-delegate contract.
/// </summary>
public class NatsTaskInFlightTrackerTests
{
    private static NatsTaskInFlightTracker.Entry MakeEntry(
        string taskId = "t1",
        string queue = "default",
        int delivery = 1,
        bool ackCalled = false,
        bool nakCalled = false,
        bool terminateCalled = false)
    {
        return new NatsTaskInFlightTracker.Entry
        {
            TaskId = taskId,
            QueueName = queue,
            NumDelivered = delivery,
            Task = new MeshTask { Id = taskId, QueueName = queue, Intent = "noop", MaxRetries = 3 },
            AckAsync = _ => { ackCalled = true; return ValueTask.CompletedTask; },
            NakAsync = (_, _) => { nakCalled = true; return ValueTask.CompletedTask; },
            TerminateAsync = (_, _) => { terminateCalled = true; return ValueTask.CompletedTask; }
        };
    }

    [Fact]
    public void New_Tracker_IsEmpty()
    {
        var t = new NatsTaskInFlightTracker();
        Assert.Equal(0, t.Count);
        Assert.Empty(t.Ids);
    }

    [Fact]
    public void AddOrReplace_StoresEntry()
    {
        var t = new NatsTaskInFlightTracker();
        var entry = MakeEntry();

        t.AddOrReplace(entry);

        Assert.Equal(1, t.Count);
        Assert.Same(entry, t.Get("t1"));
    }

    [Fact]
    public void AddOrReplace_OverwritesExistingEntry()
    {
        var t = new NatsTaskInFlightTracker();
        var first = MakeEntry(ackCalled: false);
        var second = MakeEntry(ackCalled: true);

        t.AddOrReplace(first);
        t.AddOrReplace(second);

        Assert.Equal(1, t.Count);
        var stored = t.Get("t1");
        Assert.NotNull(stored);
        Assert.Same(second, stored);
    }

    [Fact]
    public void Get_MissingTask_ReturnsNull()
    {
        var t = new NatsTaskInFlightTracker();
        Assert.Null(t.Get("nope"));
    }

    [Fact]
    public void Remove_RemovesEntry()
    {
        var t = new NatsTaskInFlightTracker();
        t.AddOrReplace(MakeEntry());

        Assert.True(t.Remove("t1"));
        Assert.Equal(0, t.Count);
        Assert.Null(t.Get("t1"));
    }

    [Fact]
    public void Remove_MissingTask_ReturnsFalse()
    {
        var t = new NatsTaskInFlightTracker();
        Assert.False(t.Remove("nope"));
    }

    [Fact]
    public void MultipleEntries_TrackedIndependently()
    {
        var t = new NatsTaskInFlightTracker();
        t.AddOrReplace(MakeEntry("a", "q1", 1));
        t.AddOrReplace(MakeEntry("b", "q2", 2));
        t.AddOrReplace(MakeEntry("c", "q3", 3));

        Assert.Equal(3, t.Count);
        Assert.Equal("q1", t.Get("a")!.QueueName);
        Assert.Equal(2, t.Get("b")!.NumDelivered);
        Assert.Equal("q3", t.Get("c")!.QueueName);

        Assert.True(t.Remove("b"));
        Assert.Equal(2, t.Count);
        Assert.Null(t.Get("b"));
    }

    [Fact]
    public async Task Entry_ActionDelegates_AreInvoked()
    {
        bool ackCalled = false, nakCalled = false, termCalled = false;
        var entry = new NatsTaskInFlightTracker.Entry
        {
            TaskId = "tx",
            QueueName = "q",
            NumDelivered = 1,
            Task = new MeshTask { Id = "tx", QueueName = "q", MaxRetries = 3 },
            AckAsync = _ => { ackCalled = true; return ValueTask.CompletedTask; },
            NakAsync = (_, _) => { nakCalled = true; return ValueTask.CompletedTask; },
            TerminateAsync = (_, _) => { termCalled = true; return ValueTask.CompletedTask; }
        };

        await entry.AckAsync(default);
        await entry.NakAsync(null, default);
        await entry.TerminateAsync(new AckOpts { TerminateReason = "test" }, default);

        Assert.True(ackCalled);
        Assert.True(nakCalled);
        Assert.True(termCalled);
    }
}
