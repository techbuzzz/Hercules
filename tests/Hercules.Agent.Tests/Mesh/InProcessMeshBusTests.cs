using Hercules.Mesh;
using Hercules.Mesh.Abstractions;
using Hercules.Mesh.InProcess;
using Xunit;

namespace Hercules.Agent.Tests.Mesh;

/// <summary>
///     Unit tests for <see cref="InProcessMeshBus"/> (task_066).
///     Covers: Publish, Subscribe (fan-out), RequestReply, IsHealthy, Dispose.
/// </summary>
public class InProcessMeshBusTests : IDisposable
{
    private readonly InProcessMeshBus _bus;

    public InProcessMeshBusTests()
    {
        _bus = new InProcessMeshBus();
    }

    [Fact]
    public void BackendKind_ReturnsInProcess()
    {
        Assert.Equal("in-process", _bus.BackendKind);
    }

    [Fact]
    public async Task IsHealthyAsync_ReturnsTrue_WhenNotDisposed()
    {
        var result = await _bus.IsHealthyAsync();
        Assert.True(result);
    }

    [Fact]
    public async Task PublishAsync_PublishesToTopic()
    {
        var received = new List<IntentEnvelope>();
        var topic = $"test/{Guid.NewGuid():N}";

        var sub = await _bus.SubscribeAsync(topic, async (env, _) =>
        {
            lock (received) { received.Add(env); }
            await Task.CompletedTask;
        });

        var envelope = new IntentEnvelope
        {
            RequestId = "req-1",
            Intent = "test",
            Payload = "hello"
        };

        await _bus.PublishAsync(topic, envelope);

        // Wait for async handler
        await Task.Delay(200);

        Assert.Single(received);
        Assert.Equal("req-1", received[0].RequestId);
    }

    [Fact]
    public async Task SubscribeAsync_FanOut_MultipleSubscribers()
    {
        var count1 = 0;
        var count2 = 0;
        var topic = $"test/{Guid.NewGuid():N}";

        var d1 = await _bus.SubscribeAsync(topic, async (_, _) => { count1++; await Task.CompletedTask; });
        var d2 = await _bus.SubscribeAsync(topic, async (_, _) => { count2++; await Task.CompletedTask; });

        await _bus.PublishAsync(topic, new IntentEnvelope { RequestId = "r1", Intent = "test" });
        await _bus.PublishAsync(topic, new IntentEnvelope { RequestId = "r2", Intent = "test" });
        await Task.Delay(100);

        Assert.Equal(2, count1);
        Assert.Equal(2, count2);

        d1.Dispose();
        d2.Dispose();
    }

    [Fact]
    public async Task SubscribeAsync_Unsubscribe_StopsDelivery()
    {
        var count = 0;
        var topic = $"test/{Guid.NewGuid():N}";

        var d = await _bus.SubscribeAsync(topic, async (_, _) => { count++; await Task.CompletedTask; });

        await _bus.PublishAsync(topic, new IntentEnvelope { RequestId = "r1", Intent = "test" });
        await Task.Delay(50);
        Assert.Equal(1, count);

        d.Dispose();

        await _bus.PublishAsync(topic, new IntentEnvelope { RequestId = "r2", Intent = "test" });
        await Task.Delay(50);
        Assert.Equal(1, count); // No more delivered after dispose
    }

    [Fact]
    public async Task RequestReplyAsync_ReturnsResponse()
    {
        var targetAgent = "agent-1";
        var replyWasCalled = 0;

        // Subscribe to handle the request and reply
        // NOTE: ReplyTo MUST be set to the correlation ID for RouteReply to find the pending TCS
        await _bus.SubscribeAsync($"agent/{targetAgent}", async (env, ct) =>
        {
            Interlocked.Increment(ref replyWasCalled);
            _bus.RouteReply(new IntentEnvelope
            {
                RequestId = env.RequestId,
                ReplyTo = env.ReplyTo, // MUST match correlation ID for RouteReply to complete TCS
                Sender = targetAgent,
                Intent = env.Intent,
                Payload = $"processed: {env.Payload}",
                TraceId = env.TraceId
            });
            await Task.CompletedTask;
        });

        var request = new IntentEnvelope
        {
            RequestId = "req-reply",
            Intent = "compute",
            Payload = "2+2",
            TraceId = "trace-1"
        };

        var response = await _bus.RequestReplyAsync(targetAgent, request, default, TimeSpan.FromSeconds(5));

        Assert.True(replyWasCalled > 0, $"Handler not called. Count={replyWasCalled}");
        Assert.True(response.IsSuccess, $"Expected success, got Status={response.Status}, Error={response.Error}");
        Assert.Contains("processed:", response.Result ?? "");
    }

    [Fact]
    public async Task RequestReplyAsync_TimesOut_WhenNoReply()
    {
        var request = new IntentEnvelope
        {
            RequestId = "req-timeout",
            Intent = "never",
            Payload = ""
        };

        var response = await _bus.RequestReplyAsync(
            "nonexistent-agent",
            request,
            default,
            TimeSpan.FromMilliseconds(100));

        Assert.False(response.IsSuccess);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var bus1 = new InProcessMeshBus();
        bus1.Dispose();
        bus1.Dispose(); // No throw
    }

    [Fact]
    public async Task PublishAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var bus2 = new InProcessMeshBus();
        bus2.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => bus2.PublishAsync("t", new IntentEnvelope { RequestId = "r", Intent = "t" }));
    }

    /// <summary>
    ///     [task_086] Default constructor exposes the configured concurrency
    ///     limit; bus honors it under saturation.
    /// </summary>
    [Fact]
    public void MaxConcurrentHandlers_DefaultsToBackpressureConfig()
    {
        var cfg = new Hercules.Config.MeshBackpressureConfig { MaxConcurrentHandlers = 3 };
        var bus = new InProcessMeshBus(cfg, null);
        Assert.Equal(3, bus.MaxConcurrentHandlers);
        bus.Dispose();
    }

    /// <summary>
    ///     [task_086] With MaxConcurrentHandlers=2 only two handlers run at a
    ///     time even when many messages are published concurrently.
    /// </summary>
    [Fact]
    public async Task PublishAsync_HandlerConcurrency_IsBoundedBySemaphore()
    {
        const int MaxConcurrent = 2;
        var cfg = new Hercules.Config.MeshBackpressureConfig
        {
            MaxConcurrentHandlers = MaxConcurrent,
            HandlerAcquireTimeoutMs = 5000 // Generous so the gate acquisition itself never drops.
        };
        var bus = new InProcessMeshBus(cfg, null);

        var current = 0;
        var peak = 0;
        var completed = 0;
        const int N = 6;

        await bus.SubscribeAsync("bounded", async (_, ct) =>
        {
            var now = Interlocked.Increment(ref current);
            InterlockedMax(ref peak, now);
            try
            {
                await Task.Delay(80, ct);
            }
            finally
            {
                Interlocked.Decrement(ref current);
                Interlocked.Increment(ref completed);
            }
        });

        var publishTasks = Enumerable.Range(0, N)
            .Select(i => bus.PublishAsync("bounded", new IntentEnvelope
            {
                RequestId = "r-" + i,
                Intent = "bounded"
            }))
            .ToArray();
        await Task.WhenAll(publishTasks);

        // Give the last handler time to drain.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (Volatile.Read(ref completed) < N && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.Equal(N, Volatile.Read(ref completed));
        Assert.True(peak <= MaxConcurrent, $"Peak concurrent handlers {peak} exceeded limit {MaxConcurrent}");
        Assert.True(peak >= 2, $"Expected at least 2 concurrent handlers with limit 2, observed {peak}");
        bus.Dispose();
    }

    private static void InterlockedMax(ref int location, int value)
    {
        int initial, newMax;
        do
        {
            initial = Volatile.Read(ref location);
            newMax = Math.Max(initial, value);
            if (newMax == initial) return;
        } while (Interlocked.CompareExchange(ref location, newMax, initial) != initial);
    }

    public void Dispose()
    {
        _bus.Dispose();
    }
}
