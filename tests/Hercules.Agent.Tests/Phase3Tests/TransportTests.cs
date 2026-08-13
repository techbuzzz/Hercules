using Hercules.Mesh;
using Hercules.Mesh.Schema;
using Hercules.Mesh.Transport;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Phase3Tests;

public class TransportResultTests
{
    [Fact]
    public void Ok_ReturnsSuccessResult()
    {
        var response = IntentResponse.Ok("req-1", "agent-b", "result", "skill");
        var result = TransportResult.Ok(response, latencyMs: 42, TransportKind.Http);

        Assert.True(result.IsSuccess);
        Assert.Equal(response, result.Response);
        Assert.Equal(42, result.LatencyMs);
        Assert.Equal(TransportKind.Http, result.TransportKind);
        Assert.Equal(TransportErrorKind.None, result.Kind);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void TimedOut_ReturnsFailedResult()
    {
        var result = TransportResult.TimedOut("agent-b", "trace-1", 5000, TransportKind.Grpc);

        Assert.False(result.IsSuccess);
        Assert.Equal(TransportErrorKind.Timeout, result.Kind);
        Assert.Equal(5000, result.LatencyMs);
        Assert.Equal("Request timed out", result.Response?.Error);
    }

    [Fact]
    public void Unreachable_ReturnsUnreachableKind()
    {
        var result = TransportResult.Unreachable("agent-b", "trace-1", "Connection refused", 0, TransportKind.Bus);

        Assert.False(result.IsSuccess);
        Assert.Equal(TransportErrorKind.Unreachable, result.Kind);
        Assert.Equal("Connection refused", result.ErrorMessage);
    }

    [Fact]
    public void Rejected_ReturnsRejectedKind()
    {
        var result = TransportResult.Rejected("agent-b", "trace-1", "Policy denied", 10, TransportKind.Http);

        Assert.False(result.IsSuccess);
        Assert.Equal(TransportErrorKind.Rejected, result.Kind);
        Assert.Equal("Policy denied", result.ErrorMessage);
    }

    [Fact]
    public void TransportError_ReturnsTransportErrorKind()
    {
        var result = TransportResult.TransportError("agent-b", "trace-1", "Network error", 0, TransportKind.Http);

        Assert.False(result.IsSuccess);
        Assert.Equal(TransportErrorKind.TransportError, result.Kind);
    }
}

public class HttpTransportAdapterTests
{
    private static readonly AgentManifest SampleManifest = new()
    {
        AgentId = "peer-agent",
        Endpoint = "http://localhost:5555",
        Auth = new ManifestAuth { Type = "none" }
    };

    private static IntentEnvelope MakeEnvelope(string requestId = "req-1")
    {
        return IntentEnvelope.Create(
            requestId,
            "caller",
            "code-review",
            new { code = "test" },
            recipient: "peer-agent",
            traceId: "trace-1");
    }

    [Fact]
    public void Kind_IsHttp()
    {
        var mockLookup = new Mock<ICapabilityLookup>();
        using var adapter = new HttpTransportAdapter(mockLookup.Object);
        Assert.Equal(TransportKind.Http, adapter.Kind);
    }

    [Fact]
    public void SupportsBidirectionalStreaming_IsFalse()
    {
        var mockLookup = new Mock<ICapabilityLookup>();
        using var adapter = new HttpTransportAdapter(mockLookup.Object);
        Assert.False(adapter.SupportsBidirectionalStreaming);
    }

    [Fact]
    public void DeliveryGuarantee_IsAtMostOnce()
    {
        var mockLookup = new Mock<ICapabilityLookup>();
        using var adapter = new HttpTransportAdapter(mockLookup.Object);
        Assert.Equal(DeliveryGuarantee.AtMostOnce, adapter.DeliveryGuarantee);
    }

    [Fact]
    public async Task SendAsync_UnknownPeer_ReturnsUnreachable()
    {
        var mockLookup = new Mock<ICapabilityLookup>();
        mockLookup.Setup(l => l.TryGet("unknown")).Returns((AgentManifest?)null);

        using var adapter = new HttpTransportAdapter(mockLookup.Object);
        var result = await adapter.SendAsync("unknown", MakeEnvelope());

        Assert.False(result.IsSuccess);
        Assert.Equal(TransportErrorKind.Unreachable, result.Kind);
        Assert.Contains("unknown", result.ErrorMessage);
    }

    [Fact]
    public async Task SendAsync_NoEndpoint_ReturnsUnreachable()
    {
        var manifest = new AgentManifest { AgentId = "peer", Endpoint = "", Auth = new ManifestAuth { Type = "none" } };
        var mockLookup = new Mock<ICapabilityLookup>();
        mockLookup.Setup(l => l.TryGet("peer")).Returns(manifest);

        using var adapter = new HttpTransportAdapter(mockLookup.Object);
        var result = await adapter.SendAsync("peer", MakeEnvelope());

        Assert.False(result.IsSuccess);
        Assert.Equal(TransportErrorKind.Unreachable, result.Kind);
        Assert.Contains("endpoint", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_CancellationRequested_ReturnsTimedOut()
    {
        var mockLookup = new Mock<ICapabilityLookup>();
        mockLookup.Setup(l => l.TryGet("peer")).Returns(SampleManifest);

        using var adapter = new HttpTransportAdapter(mockLookup.Object);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Pre-check: when ct is already cancelled, we return TimedOut result immediately
        var result = await adapter.SendAsync("peer", MakeEnvelope(), cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(TransportErrorKind.Timeout, result.Kind);
    }

    [Fact]
    public async Task SendAsync_NullOrWhitespaceTargetAgentId_Throws()
    {
        var mockLookup = new Mock<ICapabilityLookup>();
        using var adapter = new HttpTransportAdapter(mockLookup.Object);

        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null
        var ex1 = await Assert.ThrowsAnyAsync<ArgumentException>(
            () => adapter.SendAsync(null!, MakeEnvelope()));
        Assert.IsType<ArgumentNullException>(ex1);

        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentException for whitespace
        await Assert.ThrowsAsync<ArgumentException>(
            () => adapter.SendAsync("  ", MakeEnvelope()));
    }

    [Fact]
    public async Task SendAsync_NullEnvelope_Throws()
    {
        var mockLookup = new Mock<ICapabilityLookup>();
        using var adapter = new HttpTransportAdapter(mockLookup.Object);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => adapter.SendAsync("peer", null!));
    }
}

public class GrpcTransportAdapterTests
{
    [Fact]
    public void Kind_IsGrpc()
    {
        var mockLookup = new Mock<ICapabilityLookup>();
        using var adapter = new GrpcTransportAdapter(mockLookup.Object);
        Assert.Equal(TransportKind.Grpc, adapter.Kind);
    }

    [Fact]
    public void SupportsBidirectionalStreaming_IsTrue()
    {
        var mockLookup = new Mock<ICapabilityLookup>();
        using var adapter = new GrpcTransportAdapter(mockLookup.Object);
        Assert.True(adapter.SupportsBidirectionalStreaming);
    }

    [Fact]
    public void DeliveryGuarantee_IsAtMostOnce()
    {
        var mockLookup = new Mock<ICapabilityLookup>();
        using var adapter = new GrpcTransportAdapter(mockLookup.Object);
        Assert.Equal(DeliveryGuarantee.AtMostOnce, adapter.DeliveryGuarantee);
    }

    [Fact]
    public async Task SendAsync_UnknownPeer_ReturnsUnreachable()
    {
        var mockLookup = new Mock<ICapabilityLookup>();
        mockLookup.Setup(l => l.TryGet("unknown")).Returns((AgentManifest?)null);

        using var adapter = new GrpcTransportAdapter(mockLookup.Object);
        var envelope = IntentEnvelope.Create("req-1", "caller", "code-review", new { code = "test" }, recipient: "unknown");

        var result = await adapter.SendAsync("unknown", envelope);

        Assert.False(result.IsSuccess);
        Assert.Equal(TransportErrorKind.Unreachable, result.Kind);
    }
}

public class NoopBusTransportTests
{
    [Fact]
    public void Kind_IsBus()
    {
        var bus = new NoopBusTransport();
        Assert.Equal(TransportKind.Bus, bus.Kind);
    }

    [Fact]
    public async Task SendAsync_AlwaysReturnsTransportError()
    {
        var bus = new NoopBusTransport();
        var envelope = IntentEnvelope.Create("req-1", "caller", "code-review", new { test = true });

        var result = await bus.SendAsync("peer", envelope);

        Assert.False(result.IsSuccess);
        Assert.Equal(TransportErrorKind.TransportError, result.Kind);
        Assert.Contains("not configured", result.ErrorMessage);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsNoopDisposable()
    {
        var bus = new NoopBusTransport();
        var sub = await bus.SubscribeAsync("test-queue", (_, _) => Task.FromResult(IntentResponse.Ok("req", "peer", "ok")));

        sub.Dispose(); // Should not throw
    }

    [Fact]
    public async Task IsConnectedAsync_ReturnsFalse()
    {
        var bus = new NoopBusTransport();
        Assert.False(await bus.IsConnectedAsync());
    }
}

public class TransportFactoryTests
{
    [Fact]
    public void Primary_WhenDisabled_ReturnsHttpAdapter()
    {
        var mockLookup = new Mock<ICapabilityLookup>();
        var config = new TransportConfig { Enabled = false, Kind = TransportKind.Http };
        var services = new ServiceCollection().BuildServiceProvider();

        var factory = new TransportFactory(mockLookup.Object, services, config);
        Assert.Equal(TransportKind.Http, factory.Primary.Kind);
    }

    [Fact]
    public void Primary_WhenGrpcConfigured_ReturnsGrpcAdapter()
    {
        var mockLookup = new Mock<ICapabilityLookup>();
        var config = new TransportConfig { Enabled = true, Kind = TransportKind.Grpc };
        var services = new ServiceCollection().BuildServiceProvider();

        var factory = new TransportFactory(mockLookup.Object, services, config);
        Assert.Equal(TransportKind.Grpc, factory.Primary.Kind);
    }

    [Fact]
    public void Primary_DefaultConfig_ReturnsHttpAdapter()
    {
        var mockLookup = new Mock<ICapabilityLookup>();
        var config = new TransportConfig();
        var services = new ServiceCollection().BuildServiceProvider();

        var factory = new TransportFactory(mockLookup.Object, services, config);
        Assert.Equal(TransportKind.Http, factory.Primary.Kind);
    }
}

public class TransportConfigTests
{
    [Fact]
    public void DefaultValues_AreSensible()
    {
        var cfg = new TransportConfig();

        Assert.False(cfg.Enabled);
        Assert.Equal(TransportKind.Http, cfg.Kind);
        Assert.Equal(30_000, cfg.DefaultTimeoutMs);
        Assert.False(cfg.EnableGzip);
        Assert.Equal("none", cfg.BusType);
        Assert.Equal("hercules.intents", cfg.OutboundQueue);
        Assert.Equal("hercules.intents.inbound", cfg.InboundQueue);
        Assert.True(cfg.UseTls);
        Assert.Null(cfg.FallbackKind);
    }
}
