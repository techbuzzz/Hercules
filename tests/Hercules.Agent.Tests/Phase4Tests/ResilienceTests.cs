using Hercules.Budget;
using Hercules.Config;
using Hercules.Mesh;
using Hercules.Mesh.Aggregation;
using Hercules.Mesh.Resilience;
using Hercules.Mesh.Router;
using Hercules.Mesh.Transport;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Phase4Tests;

public class ResilienceTests
{
    // =====================================================================
    // RetryPolicy — jitter
    // =====================================================================

    [Fact]
    public void RetryPolicy_GetDelay_Jitter_IsDeterministic_WithSeed()
    {
        // Given two policies with the same seed
        var rp1 = new RetryPolicy(42);
        var rp2 = new RetryPolicy(42);

        // When getting delays
        var d1_1 = rp1.GetDelay(1);
        var d1_2 = rp2.GetDelay(1);
        var d2_1 = rp1.GetDelay(2);
        var d2_2 = rp2.GetDelay(2);

        // Then they are identical
        Assert.Equal(d1_1, d1_2);
        Assert.Equal(d2_1, d2_2);
    }

    [Fact]
    public void RetryPolicy_GetDelay_Jitter_VariesWithoutSeed()
    {
        var rp = new RetryPolicy();

        var delays = new HashSet<long>();
        for (var i = 0; i < 100; i++)
        {
            var d = rp.GetDelay(1);
            delays.Add((long)d.TotalMilliseconds);
        }

        // With jitter, delays should vary (more than 1 unique value)
        Assert.True(delays.Count > 1);
    }

    [Fact]
    public void RetryPolicy_GetDelay_Jitter_Bounded()
    {
        var rp = new RetryPolicy(12345)
        {
            BaseDelay = TimeSpan.FromMilliseconds(1000),
            BackoffMultiplier = 2.0,
            JitterFactor = 0.3
        };

        // Jitter at 30% means: 700ms <= delay <= 1300ms for attempt 1
        var d1 = rp.GetDelay(1);
        Assert.True(d1.TotalMilliseconds >= 700 && d1.TotalMilliseconds <= 1300,
            $"Expected 700-1300ms, got {d1.TotalMilliseconds}ms");

        // Jitter at 30% means: 1400ms <= delay <= 2600ms for attempt 2
        var d2 = rp.GetDelay(2);
        Assert.True(d2.TotalMilliseconds >= 1400 && d2.TotalMilliseconds <= 2600,
            $"Expected 1400-2600ms, got {d2.TotalMilliseconds}ms");
    }

    [Fact]
    public void RetryPolicy_GetDelay_Jitter_ZeroJitter_IsDeterministic()
    {
        var rp = new RetryPolicy(12345)
        {
            BaseDelay = TimeSpan.FromMilliseconds(100),
            JitterFactor = 0.0
        };

        var d1 = rp.GetDelay(1);
        var d2 = rp.GetDelay(1);
        Assert.Equal(d1, d2);
    }

    [Fact]
    public void RetryPolicy_GetDelay_ClampedByMaxDelay()
    {
        var rp = new RetryPolicy
        {
            BaseDelay = TimeSpan.FromSeconds(1),
            BackoffMultiplier = 10.0,
            MaxDelay = TimeSpan.FromSeconds(5),
            JitterFactor = 0.0
        };

        var d = rp.GetDelay(10);
        Assert.True(d <= TimeSpan.FromSeconds(5));
    }

    // =====================================================================
    // ResilienceConfig — defaults
    // =====================================================================

    [Fact]
    public void ResilienceConfig_HasCorrectDefaults()
    {
        var cfg = new ResilienceConfig();

        Assert.True(cfg.Enabled);
        Assert.Equal(3, cfg.MaxAttempts);
        Assert.Equal(500, cfg.BaseDelayMs);
        Assert.Equal(2.0, cfg.BackoffMultiplier);
        Assert.Equal(5_000, cfg.MaxDelayMs);
        Assert.Equal(0.3, cfg.JitterFactor);
        Assert.Equal(5, cfg.CircuitBreakerFailureThreshold);
        Assert.Equal(60, cfg.CircuitBreakerCooldownSeconds);
        Assert.Equal(4, cfg.MaxConcurrentPerPeer);
        Assert.Equal(20, cfg.MaxConcurrentTotal);
        Assert.True(cfg.IdempotencyKey.AutoGenerate);
        Assert.Contains("read:", cfg.IdempotencyKey.SafeIntents);
    }

    // =====================================================================
    // ResilientTransport — disabled (passthrough)
    // =====================================================================

    [Fact]
    public async Task ResilientTransport_Disabled_Passthrough()
    {
        var inner = new Mock<ITransport>();
        inner.Setup(t => t.Kind).Returns(TransportKind.Http);

        var envelope = new IntentEnvelope { RequestId = "req1", Intent = "test", Sender = "agent-1" };
        inner.Setup(t => t.SendAsync("peer-1", envelope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransportResult.Ok(IntentResponse.Ok("req1", "peer-1", "ok"), 10, TransportKind.Http));

        var rt = new ResilientTransport(
            inner.Object,
            new CircuitBreaker(),
            new RetryPolicy(),
            new ResilienceConfig { Enabled = false },
            Mock.Of<ILogger<ResilientTransport>>());

        var result = await rt.SendAsync("peer-1", envelope);

        Assert.True(result.IsSuccess);
        inner.Verify(t => t.SendAsync("peer-1", envelope, It.IsAny<CancellationToken>()), Times.Once);
    }

    // =====================================================================
    // ResilientTransport — success path
    // =====================================================================

    [Fact]
    public async Task ResilientTransport_Success_RecordsCircuitBreakerSuccess()
    {
        var inner = new Mock<ITransport>();
        inner.Setup(t => t.Kind).Returns(TransportKind.Http);

        var envelope = new IntentEnvelope { RequestId = "req1", Intent = "test", Sender = "agent-1" };
        inner.Setup(t => t.SendAsync("peer-1", envelope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransportResult.Ok(IntentResponse.Ok("req1", "peer-1", "ok"), 10, TransportKind.Http));

        var cb = new CircuitBreaker();
        var rt = new ResilientTransport(
            inner.Object, cb, new RetryPolicy(), new ResilienceConfig { Enabled = true },
            Mock.Of<ILogger<ResilientTransport>>());

        await rt.SendAsync("peer-1", envelope);

        Assert.Equal(CircuitState.Closed, cb.GetState("peer-1"));
    }

    // =====================================================================
    // ResilientTransport — circuit breaker fast reject
    // =====================================================================

    [Fact]
    public async Task ResilientTransport_CircuitBreakerOpen_RejectsFast()
    {
        var inner = new Mock<ITransport>();
        inner.Setup(t => t.Kind).Returns(TransportKind.Http);

        var cb = new CircuitBreaker { FailureThreshold = 1 };
        cb.RecordFailure("broken-peer"); // Opens the circuit

        var rt = new ResilientTransport(
            inner.Object, cb, new RetryPolicy(), new ResilienceConfig { Enabled = true },
            Mock.Of<ILogger<ResilientTransport>>());

        var envelope = new IntentEnvelope { RequestId = "req1", Intent = "test", Sender = "agent-1" };
        var result = await rt.SendAsync("broken-peer", envelope);

        Assert.False(result.IsSuccess);
        Assert.Equal("rejected", result.Response?.Status);
        inner.Verify(t => t.SendAsync(It.IsAny<string>(), It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // =====================================================================
    // ResilientTransport — retry on transient failure
    // =====================================================================

    [Fact]
    public async Task ResilientTransport_Retries_OnTransientFailure()
    {
        var inner = new Mock<ITransport>();
        inner.Setup(t => t.Kind).Returns(TransportKind.Http);

        var envelope = new IntentEnvelope { RequestId = "req1", Intent = "read:test", Sender = "agent-1" };
        var callCount = 0;
        inner.Setup(t => t.SendAsync("peer-1", envelope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return TransportResult.TransportError("peer-1", null, "Network error", 10, TransportKind.Http);
                }
                return TransportResult.Ok(IntentResponse.Ok("req1", "peer-1", "ok"), 10, TransportKind.Http);
            });

        var rp = new RetryPolicy(42) { MaxAttempts = 3, BaseDelay = TimeSpan.FromMilliseconds(10), JitterFactor = 0.0 };
        var rt = new ResilientTransport(
            inner.Object, new CircuitBreaker(), rp, new ResilienceConfig { Enabled = true },
            Mock.Of<ILogger<ResilientTransport>>());

        var result = await rt.SendAsync("peer-1", envelope);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, callCount);
    }

    // =====================================================================
    // ResilientTransport — exhausted retries
    // =====================================================================

    [Fact]
    public async Task ResilientTransport_ExhaustedRetries_ReturnsFailed()
    {
        var inner = new Mock<ITransport>();
        inner.Setup(t => t.Kind).Returns(TransportKind.Http);

        // Use a dedicated counter to avoid Moq Invocations.Count ambiguity with It.IsAny<>
        var callCount = 0;
        inner.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref callCount);
                return new TransportResult(
                    new IntentResponse("req1", "error", "peer-1", Error: "Network error"),
                    false, TransportErrorKind.TransportError, "Network error", 10, TransportKind.Http);
            });

        var rp = new RetryPolicy(42) { MaxAttempts = 2, BaseDelay = TimeSpan.FromMilliseconds(5), JitterFactor = 0.0 };
        var rt = new ResilientTransport(
            inner.Object, new CircuitBreaker(), rp, new ResilienceConfig { Enabled = true },
            Mock.Of<ILogger<ResilientTransport>>());

        var envelope = new IntentEnvelope { RequestId = "req1", Intent = "read:test", Sender = "agent-1" };
        var result = await rt.SendAsync("peer-1", envelope);

        Assert.False(result.IsSuccess);
        // MaxAttempts=2 → 2 total calls: attempt 0 (error+retry), attempt 1 (error+stop)
        Assert.Equal(2, callCount);
    }

    // =====================================================================
    // ResilientTransport — idempotency key generation
    // =====================================================================

    [Fact]
    public async Task ResilientTransport_IdempotencyKey_AutoGenerated_ForNonIdempotent()
    {
        var inner = new Mock<ITransport>();
        inner.Setup(t => t.Kind).Returns(TransportKind.Http);

        IntentEnvelope? capturedEnvelope = null;
        inner.Setup(t => t.SendAsync("peer-1", It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()))
            .Callback<string, IntentEnvelope, CancellationToken>((_, e, _) => capturedEnvelope = e)
            .ReturnsAsync(TransportResult.Ok(IntentResponse.Ok("req1", "peer-1", "ok"), 10, TransportKind.Http));

        var rp = new RetryPolicy(42) { MaxAttempts = 1, JitterFactor = 0.0 };
        var rt = new ResilientTransport(
            inner.Object, new CircuitBreaker(), rp, new ResilienceConfig { Enabled = true },
            Mock.Of<ILogger<ResilientTransport>>());

        var envelope = new IntentEnvelope { RequestId = "req1", Intent = "write:data", Sender = "agent-1" };
        await rt.SendAsync("peer-1", envelope);

        Assert.NotNull(capturedEnvelope);
        Assert.NotNull(capturedEnvelope.IdempotencyKey);
        Assert.StartsWith("agent-1:", capturedEnvelope.IdempotencyKey);
    }

    [Fact]
    public async Task ResilientTransport_IdempotencyKey_NotGenerated_ForSafeIntents()
    {
        var inner = new Mock<ITransport>();
        inner.Setup(t => t.Kind).Returns(TransportKind.Http);

        IntentEnvelope? capturedEnvelope = null;
        inner.Setup(t => t.SendAsync("peer-1", It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()))
            .Callback<string, IntentEnvelope, CancellationToken>((_, e, _) => capturedEnvelope = e)
            .ReturnsAsync(TransportResult.Ok(IntentResponse.Ok("req1", "peer-1", "ok"), 10, TransportKind.Http));

        var rp = new RetryPolicy(42) { MaxAttempts = 1, JitterFactor = 0.0 };
        var rt = new ResilientTransport(
            inner.Object, new CircuitBreaker(), rp, new ResilienceConfig { Enabled = true },
            Mock.Of<ILogger<ResilientTransport>>());

        var envelope = new IntentEnvelope { RequestId = "req1", Intent = "read:data", Sender = "agent-1" };
        await rt.SendAsync("peer-1", envelope);

        Assert.NotNull(capturedEnvelope);
        Assert.Null(capturedEnvelope.IdempotencyKey);
    }

    // =====================================================================
    // ResilientTransport — bulkhead (concurrent limit per peer)
    // =====================================================================

    [Fact]
    public async Task ResilientTransport_Bulkhead_RespectsMaxConcurrentPerPeer()
    {
        var inner = new Mock<ITransport>();
        inner.Setup(t => t.Kind).Returns(TransportKind.Http);

        int concurrentCount = 0;
        int maxConcurrent = 0;

        // Track how many inner SendAsync calls are running concurrently.
        // The peer semaphore (MaxConcurrentPerPeer=2) limits this to at most 2.
        inner.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                var current = Interlocked.Increment(ref concurrentCount);
                if (current > maxConcurrent) maxConcurrent = current;
                // Use Task.Run to simulate async work without blocking the caller
                return Task.Run(() =>
                {
                    Thread.Sleep(20); // Simulate network latency while holding the semaphore
                    Interlocked.Decrement(ref concurrentCount);
                    return TransportResult.TransportError("peer-1", null, "err", 10, TransportKind.Http);
                });
            });

        // MaxAttempts=2 so retry is exercised (error response is retryable)
        var rp = new RetryPolicy { MaxAttempts = 2, BaseDelay = TimeSpan.FromMilliseconds(5), JitterFactor = 0.0 };
        var rt = new ResilientTransport(
            inner.Object, new CircuitBreaker(), rp,
            new ResilienceConfig { Enabled = true, MaxConcurrentPerPeer = 2 },
            Mock.Of<ILogger<ResilientTransport>>());

        var envelope = new IntentEnvelope { RequestId = "req1", Intent = "read:test", Sender = "agent-1" };

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => rt.SendAsync("peer-1", envelope))
            .ToList();

        await Task.WhenAll(tasks);

        // Without the peer semaphore, all 5 tasks would enter the inner SendAsync concurrently.
        // With the semaphore (MaxConcurrentPerPeer=2), at most 2 concurrent inner calls at a time.
        Assert.True(maxConcurrent <= 2,
            $"Peer semaphore should limit to 2 concurrent, but observed {maxConcurrent}");
    }

    // =====================================================================
    // ResilientTransport — 4xx errors are non-retryable
    // =====================================================================

    [Fact]
    public async Task ResilientTransport_4xx_IsNotRetried()
    {
        var inner = new Mock<ITransport>();
        inner.Setup(t => t.Kind).Returns(TransportKind.Http);

        // Use a dedicated counter to avoid Moq Invocations.Count ambiguity with It.IsAny<>
        var callCount = 0;
        var envelope = new IntentEnvelope { RequestId = "req1", Intent = "read:test", Sender = "agent-1" };

        inner.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref callCount);
                // Return Rejected (4xx-like) which is NOT retryable
                return TransportResult.Rejected("peer-1", null, "Bad Request", 10, TransportKind.Http);
            });

        var rp = new RetryPolicy(42) { MaxAttempts = 3, JitterFactor = 0.0 }; // High attempts to prove no retry
        var rt = new ResilientTransport(
            inner.Object, new CircuitBreaker(), rp, new ResilienceConfig { Enabled = true },
            Mock.Of<ILogger<ResilientTransport>>());

        var result = await rt.SendAsync("peer-1", envelope);

        Assert.False(result.IsSuccess);
        // Rejected (4xx) is not retryable — exactly 1 call regardless of MaxAttempts
        Assert.Equal(1, callCount);
    }

    // =====================================================================
    // ResilientTransport — Kind delegation
    // =====================================================================

    [Fact]
    public void ResilientTransport_Kind_DelegatesToInner()
    {
        var inner = new Mock<ITransport>();
        inner.Setup(t => t.Kind).Returns(TransportKind.Grpc);
        inner.Setup(t => t.SupportsBidirectionalStreaming).Returns(true);
        inner.Setup(t => t.DeliveryGuarantee).Returns(DeliveryGuarantee.AtLeastOnce);

        var rt = new ResilientTransport(
            inner.Object, new CircuitBreaker(), new RetryPolicy(), new ResilienceConfig { Enabled = true },
            Mock.Of<ILogger<ResilientTransport>>());

        Assert.Equal(TransportKind.Grpc, rt.Kind);
        Assert.True(rt.SupportsBidirectionalStreaming);
        Assert.Equal(DeliveryGuarantee.AtLeastOnce, rt.DeliveryGuarantee);
    }

    // =====================================================================
    // ResilientTransport — Dispose
    // =====================================================================

    [Fact]
    public void ResilientTransport_Dispose_ReleasesSemaphores()
    {
        var inner = new Mock<ITransport>();
        inner.Setup(t => t.Kind).Returns(TransportKind.Http);
        inner.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransportResult.Ok(IntentResponse.Ok("req1", "peer-1", "ok"), 10, TransportKind.Http));

        var rt = new ResilientTransport(
            inner.Object, new CircuitBreaker(), new RetryPolicy(), new ResilienceConfig { Enabled = true },
            Mock.Of<ILogger<ResilientTransport>>());

        // Create some peer semaphores by sending
        var envelope = new IntentEnvelope { RequestId = "req1", Intent = "read:test", Sender = "agent-1" };
        rt.SendAsync("peer-1", envelope).Wait();
        rt.SendAsync("peer-2", envelope).Wait();

        // Should not throw
        rt.Dispose();
        rt.Dispose(); // Idempotent
    }

    // =====================================================================
    // FanOutOrchestrator — circuit breaker integration
    // =====================================================================

    [Fact]
    public async Task FanOutOrchestrator_SkipsPeersWithOpenCircuit()
    {
        // This test verifies that FanOutOrchestrator filters out peers
        // with open circuit breakers (task_047 integration).
        var cb = new CircuitBreaker { FailureThreshold = 1 };
        cb.RecordFailure("broken-peer"); // Open the circuit

        var peers = new List<PeerCandidate>
        {
            new PeerCandidate { AgentId = "healthy-peer", CompositeScore = 0.9, CostHintUsd = 0.01m, CircuitState = CircuitState.Closed },
            new PeerCandidate { AgentId = "broken-peer", CompositeScore = 0.8, CostHintUsd = 0.01m, CircuitState = CircuitState.Open },
        };

        var inner = new Mock<ITransport>();
        inner.Setup(t => t.Kind).Returns(TransportKind.Http);
        inner.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransportResult.Ok(IntentResponse.Ok("req1", "healthy-peer", "ok"), 10, TransportKind.Http));

        var meshRouter = new Mock<IMeshRouter>();
        meshRouter.Setup(r => r.RouteAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(peers);

        var aggregator = new ResponseAggregator(new FanOutOptions { Enabled = true, Strategy = FanOutSelectionStrategy.Deterministic, DeterministicCriterion = DeterministicCriterion.FirstSuccess },
            null, Mock.Of<ILogger<ResponseAggregator>>());

        var fanOut = new FanOutOrchestrator(
            meshRouter.Object,
            inner.Object,
            aggregator,
            new FanOutOptions { Enabled = true, MinPeersForFanOut = 1 },
            cb,
            Mock.Of<ILogger<FanOutOrchestrator>>());

        var result = await fanOut.OrchestrateFanOutAsync(
            new IntentEnvelope { RequestId = "req1", Intent = "test", Sender = "agent-1" },
            null, null, null);

        // broken-peer should be skipped, healthy-peer should be called
        inner.Verify(t => t.SendAsync("healthy-peer", It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()), Times.Once);
        inner.Verify(t => t.SendAsync("broken-peer", It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
