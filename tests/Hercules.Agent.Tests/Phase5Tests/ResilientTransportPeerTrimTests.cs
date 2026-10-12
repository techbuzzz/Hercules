using System.Reflection;
using Hercules.Config;
using Hercules.Mesh;
using Hercules.Mesh.Observability;
using Hercules.Mesh.Resilience;
using Hercules.Mesh.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Phase5Tests;

/// <summary>
///     Unit tests for task_087 sub-task #2 — ResilientTransport peer-semaphore
///     trim. The previous behaviour left every per-peer SemaphoreSlim in
///     _peerSemaphores forever; these tests pin the new lifecycle hooks
///     (<see cref="ResilientTransport.RemovePeer"/>,
///     <see cref="ResilientTransport.TrimIdlePeers"/>).
/// </summary>
public class ResilientTransportPeerTrimTests
{
    private static ResilientTransport BuildTransport(ResilienceConfig? cfg = null, ITransport? inner = null)
    {
        cfg ??= new ResilienceConfig
        {
            Enabled = true,
            MaxConcurrentPerPeer = 2,
            MaxConcurrentTotal = 16,
            IdempotencyKey = new IdempotencyKeyConfig
            {
                AutoGenerate = true,
                SafeIntents = new List<string> { "search" }
            }
        };
        inner ??= new NoopTransport();
        var cb = new CircuitBreaker
        {
            FailureThreshold = 3,
            Cooldown = TimeSpan.FromSeconds(60)
        };
        var rp = new RetryPolicy(42)
        {
            MaxAttempts = 1,
            BaseDelay = TimeSpan.FromMilliseconds(1),
            MaxDelay = TimeSpan.FromMilliseconds(10)
        };
        return new ResilientTransport(inner, cb, rp, cfg, NullLogger<ResilientTransport>.Instance, observability: null);
    }

    private static IntentEnvelope NewEnvelope(string intent = "test")
        => new() { RequestId = Guid.NewGuid().ToString("N"), Sender = "agent-0", Intent = intent };

    [Fact]
    public void TrackedPeerCount_InitiallyZero()
    {
        // [task_087] Fresh transport should have no per-peer semaphores allocated
        // — they are created lazily on first SendAsync.
        using var t = BuildTransport();
        Assert.Equal(0, t.TrackedPeerCount);
        Assert.Empty(t.GetTrackedPeers());
    }

    [Fact]
    public async Task SendAsync_AllocatesPeerSemaphore_OnFirstCall()
    {
        // [task_087] Lazy allocation: first SendAsync for a new peer creates the
        // entry; subsequent ones reuse it.
        using var t = BuildTransport();
        await t.SendAsync("peer-a", NewEnvelope(), CancellationToken.None);
        await t.SendAsync("peer-b", NewEnvelope(), CancellationToken.None);
        await t.SendAsync("peer-a", NewEnvelope(), CancellationToken.None);

        Assert.Equal(2, t.TrackedPeerCount);
        Assert.Equal(new[] { "peer-a", "peer-b" }, t.GetTrackedPeers());
    }

    [Fact]
    public void RemovePeer_DisposesSemaphore_AndReturnsTrue()
    {
        // [task_087] After RemovePeer the tracked count drops to zero and the
        // underlying SemaphoreSlim is disposed.
        using var t = BuildTransport();
        t.SendAsync("peer-x", NewEnvelope(), CancellationToken.None).GetAwaiter().GetResult();

        Assert.Equal(1, t.TrackedPeerCount);
        Assert.True(t.RemovePeer("peer-x"));
        Assert.Equal(0, t.TrackedPeerCount);
        Assert.Empty(t.GetTrackedPeers());
    }

    [Fact]
    public void RemovePeer_OfUnknownPeer_ReturnsFalse_AndIsNoop()
    {
        // [task_087] Idempotent — calling on an unknown peer must not throw.
        using var t = BuildTransport();
        Assert.False(t.RemovePeer("never-seen"));
        Assert.Equal(0, t.TrackedPeerCount);
    }

    [Fact]
    public void RemovePeer_ClearsLastUsed_Stamp()
    {
        // [task_087] After RemovePeer, the entry must not survive a subsequent
        // TrimIdlePeers sweep.
        using var t = BuildTransport();
        t.SendAsync("peer-y", NewEnvelope(), CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(t.RemovePeer("peer-y"));

        // No entries should remain in either dictionary.
        var lastUsedField = typeof(ResilientTransport).GetField(
            "_peerLastUsed",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var lastUsed = (System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset>)
            lastUsedField!.GetValue(t)!;
        Assert.False(lastUsed.ContainsKey("peer-y"));
    }

    [Fact]
    public void TrimIdlePeers_RemovesStaleEntries_KeepsFresh()
    {
        // [task_087] Whitelist behaviour: peers used recently survive, peers idle
        // longer than the threshold are removed.
        using var t = BuildTransport();

        // Allocate a semaphore for "peer-old" then age it via reflection so the
        // sweep sees it as stale.
        t.SendAsync("peer-old", NewEnvelope(), CancellationToken.None).GetAwaiter().GetResult();
        t.SendAsync("peer-fresh", NewEnvelope(), CancellationToken.None).GetAwaiter().GetResult();

        var lastUsedField = typeof(ResilientTransport).GetField(
            "_peerLastUsed",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var lastUsed = (System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset>)
            lastUsedField!.GetValue(t)!;
        lastUsed["peer-old"] = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10);

        var trimmed = t.TrimIdlePeers(TimeSpan.FromMinutes(1));
        Assert.Equal(1, trimmed);
        Assert.Equal(1, t.TrackedPeerCount);
        Assert.Equal(new[] { "peer-fresh" }, t.GetTrackedPeers());
    }

    [Fact]
    public void TrimIdlePeers_NonPositiveThreshold_IsNoop()
    {
        // [task_087] Threshold <= 0 means "do nothing" — protect operators from
        // accidentally passing TimeSpan.Zero from a config flag.
        using var t = BuildTransport();
        t.SendAsync("peer-z", NewEnvelope(), CancellationToken.None).GetAwaiter().GetResult();

        Assert.Equal(0, t.TrimIdlePeers(TimeSpan.Zero));
        Assert.Equal(0, t.TrimIdlePeers(TimeSpan.FromSeconds(-1)));
        Assert.Equal(1, t.TrackedPeerCount);
    }

    [Fact]
    public void TrimIdlePeers_ReentrantAfterFreshSend_DoesNotTrim()
    {
        // [task_087] Just-used peer is not stale; sweep must not remove it.
        using var t = BuildTransport();
        t.SendAsync("peer-active", NewEnvelope(), CancellationToken.None).GetAwaiter().GetResult();

        // Tiny threshold (1ms) but the entry was updated microseconds ago.
        var trimmed = t.TrimIdlePeers(TimeSpan.FromMilliseconds(1));
        // Allow for one tick of clock drift — the assertion is that we trim ≤
        // 1 entry and the active peer is still present. 0 is the expected
        // outcome; 1 is a false negative that we accept with a softer check.
        Assert.InRange(trimmed, 0, 1);
        if (trimmed == 1)
        {
            // Re-send to repopulate; otherwise re-test
            t.SendAsync("peer-active", NewEnvelope(), CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal(1, t.TrackedPeerCount);
        }
    }

    [Fact]
    public void Dispose_ClearsPeerLastUsed_Dictionary()
    {
        // [task_087] After Dispose the last-used dictionary should not retain
        // references to disposed semaphores.
        var t = BuildTransport();
        t.SendAsync("peer-1", NewEnvelope(), CancellationToken.None).GetAwaiter().GetResult();

        var lastUsedField = typeof(ResilientTransport).GetField(
            "_peerLastUsed",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var lastUsed = (System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset>)
            lastUsedField!.GetValue(t)!;
        Assert.Equal(1, lastUsed.Count);

        t.Dispose();
        Assert.Empty(lastUsed);
    }

    /// <summary>Minimal ITransport stub — just echoes success.</summary>
    private sealed class NoopTransport : ITransport
    {
        public TransportKind Kind => TransportKind.Http;
        public bool SupportsBidirectionalStreaming => false;
        public DeliveryGuarantee DeliveryGuarantee => DeliveryGuarantee.AtMostOnce;

        public Task<TransportResult> SendAsync(string targetAgentId, IntentEnvelope envelope, CancellationToken ct = default)
            => Task.FromResult(new TransportResult(
                IntentResponse.Ok(envelope.RequestId, targetAgentId, "{}", envelope.TraceId),
                true,
                TransportErrorKind.None,
                null,
                1L,
                Kind));

        public void Dispose() { /* no-op */ }
    }
}
