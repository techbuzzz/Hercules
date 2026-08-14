using System.Diagnostics;
using Hercules.Config;
using Hercules.Mesh;
using Hercules.Mesh.Observability;
using Hercules.Mesh.Policy;
using Hercules.Mesh.Router;
using Hercules.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Phase4Tests;

/// <summary>
///     Unit tests for mesh observability wiring (task_065).
///     Tests: CapabilityMeshRouter with IMeshObservabilityService,
///     ResilientTransport with IMeshObservabilityService,
///     IMeshObservabilityService.RecordMeshMetric with retry_attempt and other metrics.
/// </summary>
public class MeshObservabilityTests : IDisposable
{
    // --- Mock helper ---------------------------------------------------------------

    private static Mock<IMeshObservabilityService> CreateMockObs(bool enabled = true)
    {
        var mock = new Mock<IMeshObservabilityService>();
        mock.Setup(x => x.IsEnabled).Returns(enabled);
        mock.Setup(x => x.StartMeshSpan(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns((string name, string? peer, string? intent) =>
                enabled ? new ActivitySource("test").StartActivity(name) : null);
        mock.Setup(x => x.InjectTraceContext(It.IsAny<Activity?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(new Dictionary<string, string>());
        return mock;
    }

    // --- IMeshObservabilityService — RecordMeshMetric with retry_attempt -------------

    [Fact]
    public void MeshObservabilityService_RecordMeshMetric_RetryAttempt_NoThrow()
    {
        // Arrange
        var config = new MeshCentralizedObservabilityConfig { Enabled = true, EnableMetrics = true };
        var otelSvc = new OtelService(new OtelConfig { Enabled = true });
        var obs = new MeshObservabilityService(
            config, otelSvc, NullLogger<MeshObservabilityService>.Instance);

        // Act — should not throw
        obs.RecordMeshMetric("retry_attempt", 1,
            peerAgentId: "peer-1", intent: "test-intent", outcome: "retrying");
        obs.RecordMeshMetric("retry_attempt", 3,
            peerAgentId: "peer-2", intent: "test-intent-2", outcome: "exhausted");

        // Assert — no exception thrown
        Assert.True(true);
    }

    [Fact]
    public void MeshObservabilityService_RecordMeshMetric_UnknownMetric_NoThrow()
    {
        // Arrange
        var config = new MeshCentralizedObservabilityConfig { Enabled = true, EnableMetrics = true };
        var otelSvc = new OtelService(new OtelConfig { Enabled = true });
        var obs = new MeshObservabilityService(
            config, otelSvc, NullLogger<MeshObservabilityService>.Instance);

        // Act — should not throw
        obs.RecordMeshMetric("unknown_metric", 1.0);

        // Assert — no exception
        Assert.True(true);
    }

    [Fact]
    public void MeshObservabilityService_IsEnabled_ReflectsConfig()
    {
        var enabled = new MeshObservabilityService(
            new MeshCentralizedObservabilityConfig { Enabled = true },
            new OtelService(new OtelConfig { Enabled = true }),
            NullLogger<MeshObservabilityService>.Instance);
        Assert.True(enabled.IsEnabled);

        var disabled = new MeshObservabilityService(
            new MeshCentralizedObservabilityConfig { Enabled = false },
            new OtelService(new OtelConfig { Enabled = true }),
            NullLogger<MeshObservabilityService>.Instance);
        Assert.False(disabled.IsEnabled);
    }

    // --- CapabilityMeshRouter with observability ------------------------------------

    [Fact]
    public async Task CapabilityMeshRouter_RouteAsync_EmitsMetric_WhenDisabled()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"mesh-obs-router-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var registry = new CapabilityRegistry(Path.Combine(tempDir, "registry.db"));
            var breaker = new CircuitBreaker { FailureThreshold = 5 };
            var trustPolicy = new TrustAdmissionPolicyEngine(
                new TrustAdmissionConfig { Enabled = true, PolicyMode = "DryRun" },
                NullLogger<TrustAdmissionPolicyEngine>.Instance);
            var healthTracker = new RouterHealthTracker();
            var options = new MeshRouterOptions { Enabled = false };
            var obs = CreateMockObs();

            var router = new CapabilityMeshRouter(
                registry, breaker, trustPolicy, healthTracker, options,
                NullLogger<CapabilityMeshRouter>.Instance, obs.Object);

            // Act
            var result = await router.RouteAsync("test-capability");

            // Assert — intent is 4th arg, outcome is 5th
            obs.Verify(x => x.RecordMeshMetric(
                "routing_decision", 0, null, "test-capability", "disabled"), Times.Once);
            Assert.Empty(result);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task CapabilityMeshRouter_RouteAsync_CallsObservabilityWhenEnabled()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mesh-obs-router2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var registry = new CapabilityRegistry(Path.Combine(tempDir, "registry.db"));
            var breaker = new CircuitBreaker { FailureThreshold = 5 };
            var trustPolicy = new TrustAdmissionPolicyEngine(
                new TrustAdmissionConfig { Enabled = true, PolicyMode = "DryRun" },
                NullLogger<TrustAdmissionPolicyEngine>.Instance);
            var healthTracker = new RouterHealthTracker();
            var options = new MeshRouterOptions { Enabled = true, MinConfidenceThreshold = 0.0 };
            var obs = CreateMockObs();

            var router = new CapabilityMeshRouter(
                registry, breaker, trustPolicy, healthTracker, options,
                NullLogger<CapabilityMeshRouter>.Instance, obs.Object);

            // Act
            var result = await router.RouteAsync("unknown-capability");

            // Assert — metric called at least once (once when enabled, once with results)
            obs.Verify(x => x.RecordMeshMetric(
                It.IsAny<string>(), It.IsAny<double>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()),
                Times.AtLeastOnce());
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task CapabilityMeshRouter_RouteAsync_WithoutObservability_DoesNotThrow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mesh-obs-router3-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var registry = new CapabilityRegistry(Path.Combine(tempDir, "registry.db"));
            var breaker = new CircuitBreaker { FailureThreshold = 5 };
            var trustPolicy = new TrustAdmissionPolicyEngine(
                new TrustAdmissionConfig { Enabled = true, PolicyMode = "DryRun" },
                NullLogger<TrustAdmissionPolicyEngine>.Instance);
            var healthTracker = new RouterHealthTracker();
            var options = new MeshRouterOptions { Enabled = true };

            // Router WITHOUT observability (null) — should not throw
            var router = new CapabilityMeshRouter(
                registry, breaker, trustPolicy, healthTracker, options,
                NullLogger<CapabilityMeshRouter>.Instance, observability: null);

            // Act
            var result = await router.RouteAsync("test");

            // Assert
            Assert.NotNull(result);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    // --- TraceContextCarrier -------------------------------------------------------

    [Fact]
    public void TraceContextCarrier_HasTrace_ReturnsFalse_WhenEmpty()
    {
        var carrier = new TraceContextCarrier();
        Assert.False(carrier.HasTrace);
    }

    [Fact]
    public void TraceContextCarrier_HasTrace_ReturnsTrue_WhenTraceIdSet()
    {
        var carrier = new TraceContextCarrier { TraceId = "abc123" };
        Assert.True(carrier.HasTrace);
    }

    [Fact]
    public void TraceContextCarrier_AllPropertiesInitialized()
    {
        var carrier = new TraceContextCarrier
        {
            TraceId = "trace-1",
            SpanId = "span-1",
            TraceParent = "00-trace-1-span-1-00",
            TraceState = "key=value",
            B3Sampled = "1"
        };

        Assert.Equal("trace-1", carrier.TraceId);
        Assert.Equal("span-1", carrier.SpanId);
        Assert.Equal("00-trace-1-span-1-00", carrier.TraceParent);
        Assert.Equal("key=value", carrier.TraceState);
        Assert.Equal("1", carrier.B3Sampled);
        Assert.True(carrier.HasTrace);
    }

    public void Dispose()
    {
        // Cleanup handled in individual test methods
    }
}
