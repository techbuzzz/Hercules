using System.Diagnostics.Metrics;
using Hercules.Config;
using Hercules.Observability;
using HerculesBus.Core;
using HerculesBus.InMemory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#pragma warning disable CS0618

namespace Hercules.Agent.Tests.BusTests;

/// <summary>
///     Backpressure &amp; bounded-channel tests for <see cref="InMemoryEventBus"/> (task_086).
/// </summary>
public class InMemoryEventBusBackpressureTests : IDisposable
{
    private static readonly MeterListener _busListener = CreateBusListener();
    private static long _busDrops;

    public InMemoryEventBusBackpressureTests()
    {
        Interlocked.Exchange(ref _busDrops, 0);
    }

    public void Dispose()
    {
        // No-op: listener is process-wide; tests reset the counter locally.
    }

    [Fact]
    public void ChannelCapacity_UsesConfiguredValue()
    {
        var bus = new InMemoryEventBus(
            NullLogger<InMemoryEventBus>.Instance,
            new BusConfig { MaxChannelCapacity = 7 });

        Assert.Equal(7, bus.ChannelCapacity);
    }

    [Fact]
    public async Task Publish_BoundedChannel_OverflowIncrementsDropCounter()
    {
        var cfg = new BusConfig
        {
            MaxChannelCapacity = 4,
            BackpressureTimeoutMs = 50,
            DropOnBackpressure = true
        };
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance, cfg);

        await using var sub = bus.Subscribe("hot", (_, _) =>
        {
            return new ValueTask(Task.Delay(Timeout.Infinite));
        });

        // Publish 10 messages; with capacity 4 and no consumption, ≥5 should be dropped.
        for (var i = 0; i < 10; i++)
        {
            await bus.PublishAsync(new AgentMessage(
                Id: "",
                Channel: "hot",
                SenderAgentId: "alice",
                SenderName: "Alice",
                Kind: MessageKinds.Text,
                Body: "msg-" + i));
        }

        Assert.True(_busDrops >= 5,
            $"Expected at least 5 drops (capacity 4, 10 messages), got {_busDrops}");
    }

    [Fact]
    public async Task Publish_BoundedChannel_FastSubscriberDrains_NoDrops()
    {
        // With DropOnBackpressure=false the publisher waits up to BackpressureTimeoutMs
        // for the subscriber to drain the channel. A fast synchronous subscriber drains
        // quickly, so no drops should occur.
        var cfg = new BusConfig
        {
            MaxChannelCapacity = 4,
            BackpressureTimeoutMs = 200,
            DropOnBackpressure = false
        };
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance, cfg);

        var received = 0;
        await using var sub = bus.Subscribe("drain", (_, _) =>
        {
            Interlocked.Increment(ref received);
            return ValueTask.CompletedTask;
        });

        for (var i = 0; i < 10; i++)
        {
            await bus.PublishAsync(new AgentMessage(
                Id: "",
                Channel: "drain",
                SenderAgentId: "alice",
                SenderName: "Alice",
                Kind: MessageKinds.Text,
                Body: "msg-" + i));
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (Volatile.Read(ref received) < 10 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.Equal(10, Volatile.Read(ref received));
        Assert.Equal(0L, _busDrops);
    }

    [Fact]
    public async Task Publish_Backpressure_DoesNotBlockIndefinitely()
    {
        var cfg = new BusConfig
        {
            MaxChannelCapacity = 2,
            BackpressureTimeoutMs = 50,
            DropOnBackpressure = false
        };
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance, cfg);

        await using var sub = bus.Subscribe("slow", (_, _) =>
        {
            return new ValueTask(Task.Delay(Timeout.Infinite));
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        // Publish 6 messages; with capacity 2 and a blocked subscriber, 4
        // will hit backpressure, time out after 50ms, and be dropped.
        for (var i = 0; i < 6; i++)
        {
            await bus.PublishAsync(new AgentMessage(
                Id: "",
                Channel: "slow",
                SenderAgentId: "alice",
                SenderName: "Alice",
                Kind: MessageKinds.Text,
                Body: "m-" + i));
        }
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2),
            $"Publish should not block indefinitely, took {sw.Elapsed}");

        Assert.True(_busDrops >= 1,
            $"Expected at least one drop after backpressure timeout, got {_busDrops}");
    }

    private static MeterListener CreateBusListener()
    {
        const string targetName = "hercules.bus.channel.drop.count";
        var listener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                if (instr.Meter.Name == OtelSetup.ServiceName && instr.Name == targetName)
                {
                    l.EnableMeasurementEvents(instr);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((instr, value, _, _) =>
        {
            if (instr.Name == targetName)
            {
                Interlocked.Add(ref _busDrops, value);
            }
        });
        listener.Start();
        return listener;
    }
}
