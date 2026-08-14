using Hercules.Degradation;
using Hercules.LLM;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Degradation;

/// <summary>
///     Unit tests for task_061: Local-first degradation.
///     Covers: DegradationManager, DeterministicFallbackEngine, DegradationConfig.
/// </summary>
public class DegradationTests : IDisposable
{
    private readonly DegradationConfig _config;
    private readonly DeterministicFallbackEngine _engine;
    private readonly Mock<ILLMClient> _llmClientMock;
    private readonly Mock<ILogger<DeterministicFallbackEngine>> _engineLogMock;
    private readonly Mock<ILogger<DegradationManager>> _managerLogMock;
    private readonly Mock<ILogger<OperatorNotificationService>> _notificationLogMock;
    private readonly Mock<ILogger<DegradationObservability>> _observabilityLogMock;

    public DegradationTests()
    {
        _config = new DegradationConfig
        {
            Enabled = true,
            HealthCheck = new HealthCheckConfig
            {
                Enabled = true,
                CheckLlmProvider = true,
                CheckNetwork = true
            },
            Fallback = new FallbackConfig
            {
                EnableDeterministicFallback = true,
                UseLocalSkills = true,
                UseReducedCapabilityModels = true,
                QueueWorkWhenOffline = true
            }
        };

        _llmClientMock = new Mock<ILLMClient>();
        _engineLogMock = new Mock<ILogger<DeterministicFallbackEngine>>();
        _managerLogMock = new Mock<ILogger<DegradationManager>>();
        _notificationLogMock = new Mock<ILogger<OperatorNotificationService>>();
        _observabilityLogMock = new Mock<ILogger<DegradationObservability>>();

        _engine = new DeterministicFallbackEngine(
            _config,
            _llmClientMock.Object,
            _engineLogMock.Object);
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

    #region DegradationMode tests

    [Fact]
    public void DegradationMode_Full_HasExpectedValue()
    {
        Assert.Equal(0, (int)DegradationMode.Full);
    }

    [Fact]
    public void DegradationMode_Degraded_HasExpectedValue()
    {
        Assert.Equal(1, (int)DegradationMode.Degraded);
    }

    [Fact]
    public void DegradationMode_Offline_HasExpectedValue()
    {
        Assert.Equal(2, (int)DegradationMode.Offline);
    }

    #endregion

    #region DegradationConfig tests

    [Fact]
    public void DegradationConfig_HasSensibleDefaults()
    {
        var cfg = new DegradationConfig();

        Assert.True(cfg.Enabled);
        Assert.True(cfg.HealthCheck.Enabled);
        Assert.Equal(30, cfg.HealthCheck.IntervalSeconds);
        Assert.Equal(3, cfg.HealthCheck.FailureThreshold);
        Assert.True(cfg.Fallback.EnableDeterministicFallback);
        Assert.True(cfg.Fallback.UseLocalSkills);
        Assert.True(cfg.Notifications.Enabled);
        Assert.True(cfg.Observability.EnableMetrics);
    }

    [Fact]
    public void FallbackConfig_ReducedCapabilityModels_HasDefaults()
    {
        var cfg = new FallbackConfig();

        Assert.NotEmpty(cfg.ReducedCapabilityModels);
        Assert.Contains("gpt-4o-mini", cfg.ReducedCapabilityModels);
    }

    #endregion

    #region DeterministicFallbackEngine tests

    [Fact]
    public void GetFallbackStrategy_FullMode_ReturnsNone()
    {
        var state = new DegradationState(
            DegradationMode.Full,
            Array.Empty<ServiceHealthSnapshot>(),
            DateTimeOffset.UtcNow);

        var strategy = _engine.GetFallbackStrategy(state);

        Assert.Equal(FallbackStrategy.None, strategy);
    }

    [Fact]
    public void GetFallbackStrategy_OfflineMode_ReturnsQueueWork()
    {
        var statuses = new List<ServiceHealthSnapshot>
        {
            new("network", ServiceHealth.Unhealthy, "Network unavailable"),
            new("llm", ServiceHealth.Unhealthy, "LLM unavailable")
        };

        var state = new DegradationState(
            DegradationMode.Offline,
            statuses,
            DateTimeOffset.UtcNow,
            "Network and LLM unavailable");

        var strategy = _engine.GetFallbackStrategy(state);

        Assert.Equal(FallbackStrategy.QueueWork, strategy);
    }

    [Fact]
    public void GetFallbackStrategy_LlmUnhealthyDegradedMode_ReturnsReducedCapabilityModel()
    {
        var statuses = new List<ServiceHealthSnapshot>
        {
            new("llm", ServiceHealth.Unhealthy, "LLM unavailable")
        };

        var state = new DegradationState(
            DegradationMode.Degraded,
            statuses,
            DateTimeOffset.UtcNow,
            "LLM unhealthy");

        var strategy = _engine.GetFallbackStrategy(state);

        Assert.Equal(FallbackStrategy.ReducedCapabilityModel, strategy);
    }

    [Fact]
    public void ShouldAcceptDelegations_FullMode_ReturnsTrue()
    {
        var state = new DegradationState(
            DegradationMode.Full,
            Array.Empty<ServiceHealthSnapshot>(),
            DateTimeOffset.UtcNow);

        Assert.True(_engine.ShouldAcceptDelegations(state));
    }

    [Fact]
    public void ShouldAcceptDelegations_DegradedMode_ReturnsConfiguredValue()
    {
        var state = new DegradationState(
            DegradationMode.Degraded,
            Array.Empty<ServiceHealthSnapshot>(),
            DateTimeOffset.UtcNow);

        // Default config allows delegations in degraded mode
        Assert.True(_engine.ShouldAcceptDelegations(state));
    }

    [Fact]
    public void ShouldAcceptDelegations_OfflineMode_ReturnsConfiguredValue()
    {
        var state = new DegradationState(
            DegradationMode.Offline,
            Array.Empty<ServiceHealthSnapshot>(),
            DateTimeOffset.UtcNow);

        // Default config refuses delegations when offline
        Assert.False(_engine.ShouldAcceptDelegations(state));
    }

    [Fact]
    public async Task GetBestAvailableLlmProviderAsync_ReducedCapabilityModel_ReturnsFirstModel()
    {
        var provider = await _engine.GetBestAvailableLlmProviderAsync(FallbackStrategy.ReducedCapabilityModel);

        Assert.NotNull(provider);
        Assert.Equal("gpt-4o-mini", provider);
    }

    [Fact]
    public async Task GetBestAvailableLlmProviderAsync_None_ReturnsNull()
    {
        var provider = await _engine.GetBestAvailableLlmProviderAsync(FallbackStrategy.None);

        Assert.Null(provider);
    }

    #endregion

    #region DegradationState tests

    [Fact]
    public void DegradationState_CanBeCreated()
    {
        var statuses = new List<ServiceHealthSnapshot>
        {
            new("llm", ServiceHealth.Healthy, "OK"),
            new("network", ServiceHealth.Degraded, "Slow")
        };

        var since = DateTimeOffset.UtcNow.AddMinutes(-5);
        var state = new DegradationState(
            DegradationMode.Degraded,
            statuses,
            since,
            "Network degraded");

        Assert.Equal(DegradationMode.Degraded, state.Mode);
        Assert.Equal(2, state.ServiceStatuses.Count);
        Assert.Equal(since, state.Since);
        Assert.Equal("Network degraded", state.Reason);
    }

    [Fact]
    public void ServiceHealthSnapshot_CanBeCreated()
    {
        var checkedAt = DateTimeOffset.UtcNow;
        var snapshot = new ServiceHealthSnapshot(
            "test-service",
            ServiceHealth.Healthy,
            "All good",
            checkedAt);

        Assert.Equal("test-service", snapshot.ServiceName);
        Assert.Equal(ServiceHealth.Healthy, snapshot.Health);
        Assert.Equal("All good", snapshot.Message);
        Assert.Equal(checkedAt, snapshot.CheckedAt);
    }

    #endregion

    #region DegradationObservability tests

    [Fact]
    public void GenerateStatusReport_ReturnsValidReport()
    {
        var observability = new DegradationObservability(_config, _observabilityLogMock.Object);

        var statuses = new List<ServiceHealthSnapshot>
        {
            new("llm", ServiceHealth.Healthy),
            new("network", ServiceHealth.Degraded)
        };

        var state = new DegradationState(
            DegradationMode.Degraded,
            statuses,
            DateTimeOffset.UtcNow.AddMinutes(-10),
            "Network degraded");

        var report = observability.GenerateStatusReport(state);

        Assert.Equal(DegradationMode.Degraded, report.Mode);
        Assert.Equal(2, report.ServiceStatuses.Count);
        Assert.NotEmpty(report.Recommendations);
    }

    [Fact]
    public void RecordModeTransition_DoesNotThrow()
    {
        var observability = new DegradationObservability(_config, _observabilityLogMock.Object);

        // Should not throw
        observability.RecordModeTransition(DegradationMode.Full, DegradationMode.Offline, "Network failure");
    }

    [Fact]
    public void RecordFallbackStrategy_DoesNotThrow()
    {
        var observability = new DegradationObservability(_config, _observabilityLogMock.Object);

        // Should not throw
        observability.RecordFallbackStrategy(FallbackStrategy.QueueWork, DegradationMode.Offline);
    }

    #endregion

    #region DegradationModeChangedEventArgs tests

    [Fact]
    public void DegradationModeChangedEventArgs_ContainsAllProperties()
    {
        var args = new DegradationModeChangedEventArgs(
            DegradationMode.Full,
            DegradationMode.Offline,
            "Network failure");

        Assert.Equal(DegradationMode.Full, args.PreviousMode);
        Assert.Equal(DegradationMode.Offline, args.NewMode);
        Assert.Equal("Network failure", args.Reason);
    }

    #endregion
}
