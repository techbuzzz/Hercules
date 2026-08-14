using Hercules.Mesh;
using Hercules.Mesh.Transport;
using Moq;
using Xunit;
using TestAgents = Hercules.Agent.Tests.Phase3Tests.TestAgents;

namespace Hercules.Agent.Tests.Phase3Tests;

/// <summary>
/// Transport fault simulation tests.
/// Verifies graceful handling of timeouts, connection failures, and unreachable peers.
/// </summary>
public class TransportFaultTests
{
    private readonly Mock<ICapabilityLookup> _mockRegistry;
    private readonly TestAgents.TestTransport _transport;

    public TransportFaultTests()
    {
        _mockRegistry = new Mock<ICapabilityLookup>();
        _mockRegistry.Setup(r => r.TryGet(It.IsAny<string>()))
            .Returns(new AgentManifest { AgentId = "test-agent", Endpoint = "http://localhost:9000" });
        _transport = new TestAgents.TestTransport(_mockRegistry.Object);
    }

    [Fact]
    public async Task SendAsync_WithSimulatedTimeout_ReturnsTimeoutResult()
    {
        _transport.SetLatency(100);
        _transport.InjectFailure("Timeout simulated", TransportErrorKind.Timeout);

        var envelope = IntentEnvelope.Create("req-timeout", "sender", "test", "payload");

        var result = await _transport.SendAsync("test-agent", envelope);

        Assert.False(result.IsSuccess);
        Assert.Equal(TransportErrorKind.Timeout, result.Kind);
        Assert.Contains("timed out", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_WithSimulatedUnreachable_ReturnsUnreachableResult()
    {
        _transport.InjectFailure("Connection refused", TransportErrorKind.Unreachable);

        var envelope = IntentEnvelope.Create("req-unreachable", "sender", "test", "payload");

        var result = await _transport.SendAsync("test-agent", envelope);

        Assert.False(result.IsSuccess);
        Assert.Equal(TransportErrorKind.Unreachable, result.Kind);
        Assert.Contains("refused", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_WithSimulatedTransportError_ReturnsTransportErrorResult()
    {
        _transport.InjectFailure("Network error: reset by peer", TransportErrorKind.TransportError);

        var envelope = IntentEnvelope.Create("req-neterr", "sender", "test", "payload");

        var result = await _transport.SendAsync("test-agent", envelope);

        Assert.False(result.IsSuccess);
        Assert.Equal(TransportErrorKind.TransportError, result.Kind);
    }

    [Fact]
    public async Task SendAsync_WithSimulatedRejection_ReturnsRejectedResult()
    {
        _transport.InjectFailure("Policy denied: insufficient scope", TransportErrorKind.Rejected);

        var envelope = IntentEnvelope.Create("req-rejected", "sender", "test", "payload");

        var result = await _transport.SendAsync("test-agent", envelope);

        Assert.False(result.IsSuccess);
        Assert.Equal(TransportErrorKind.Rejected, result.Kind);
        Assert.Contains("denied", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_NoFailure_ReturnsSuccess()
    {
        _transport.ClearFailure();

        var envelope = IntentEnvelope.Create("req-ok", "sender", "test", "payload");

        var result = await _transport.SendAsync("test-agent", envelope);

        Assert.True(result.IsSuccess);
        Assert.Equal(TransportErrorKind.None, result.Kind);
        Assert.NotNull(result.Response);
        Assert.Equal("ok", result.Response.Status);
    }

    [Fact]
    public async Task SendAsync_UnknownAgent_ReturnsUnreachable()
    {
        _mockRegistry.Setup(r => r.TryGet("unknown-agent")).Returns((AgentManifest?)null);

        var envelope = IntentEnvelope.Create("req-unknown", "sender", "test", "payload");

        var result = await _transport.SendAsync("unknown-agent", envelope);

        Assert.False(result.IsSuccess);
        Assert.Equal(TransportErrorKind.Unreachable, result.Kind);
    }

    [Fact]
    public async Task SendAsync_WithLatency_ReportsLatency()
    {
        _transport.SetLatency(50);
        _transport.ClearFailure();

        var envelope = IntentEnvelope.Create("req-latency", "sender", "test", "payload");

        var result = await _transport.SendAsync("test-agent", envelope);

        Assert.True(result.IsSuccess);
        Assert.True(result.LatencyMs >= 50);
    }

    [Fact]
    public async Task SendAsync_AfterClearFailure_SucceedsAgain()
    {
        _transport.InjectFailure("always fails", TransportErrorKind.TransportError);
        _transport.ClearFailure();

        var envelope = IntentEnvelope.Create("req-recovered", "sender", "test", "payload");

        var result = await _transport.SendAsync("test-agent", envelope);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void TestTransport_Dispose_PreventsFurtherUse()
    {
        _transport.Dispose();

        var envelope = IntentEnvelope.Create("req-disposed", "sender", "test", "payload");

        Assert.Throws<ObjectDisposedException>(() => _transport.SendAsync("test-agent", envelope).GetAwaiter().GetResult());
    }

    [Fact]
    public void TestTransport_Implements_ITransport_Interface()
    {
        Assert.IsAssignableFrom<ITransport>(_transport);
        Assert.Equal(TransportKind.Http, _transport.Kind);
        Assert.False(_transport.SupportsBidirectionalStreaming);
        Assert.Equal(DeliveryGuarantee.AtMostOnce, _transport.DeliveryGuarantee);
    }

    [Fact]
    public async Task SendAsync_Cancellation_ThrowsOperationCanceled()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var envelope = IntentEnvelope.Create("req-cancel", "sender", "test", "payload");

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _transport.SendAsync("test-agent", envelope, cts.Token));
    }

    [Fact]
    public async Task SendAsync_Chain_FailureModes_DifferentResults()
    {
        var envelope = IntentEnvelope.Create("req-chain", "sender", "test", "payload");

        // Test each failure mode returns different result
        foreach (TransportErrorKind kind in Enum.GetValues<TransportErrorKind>())
        {
            if (kind == TransportErrorKind.None || kind == TransportErrorKind.SchemaMismatch) continue;

            _transport.InjectFailure($"Error type: {kind}", kind);
            var result = await _transport.SendAsync("test-agent", envelope);

            Assert.False(result.IsSuccess);
            Assert.Equal(kind, result.Kind);
        }
    }
}
