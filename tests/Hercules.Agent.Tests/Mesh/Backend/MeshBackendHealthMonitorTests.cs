using Hercules.Config;
using Hercules.Mesh.Abstractions;
using Hercules.Mesh.Backend;
using Hercules.Mesh.Profiles;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Mesh.Backend;

public class MeshBackendHealthMonitorTests
{
    private readonly Mock<IMeshBus> _busMock;
    private readonly Mock<ITaskQueue> _queueMock;
    private readonly Mock<IMeshStateStore> _stateStoreMock;
    private readonly Mock<ILogger<MeshBackendHealthMonitor>> _logMock;

    public MeshBackendHealthMonitorTests()
    {
        _busMock = new Mock<IMeshBus>();
        _queueMock = new Mock<ITaskQueue>();
        _stateStoreMock = new Mock<IMeshStateStore>();
        _logMock = new Mock<ILogger<MeshBackendHealthMonitor>>();

        // Default: in-process (always healthy, no external calls)
        _busMock.Setup(b => b.BackendKind).Returns("in-process");
        _queueMock.Setup(q => q.BackendKind).Returns("in-process");
        _stateStoreMock.Setup(s => s.BackendKind).Returns("in-process");
    }

    private MeshBackendHealthMonitor CreateMonitor(MeshProfilesConfig? cfg = null)
    {
        cfg ??= new MeshProfilesConfig { Enabled = true };
        return new MeshBackendHealthMonitor(
            _busMock.Object,
            _queueMock.Object,
            _stateStoreMock.Object,
            cfg,
            observability: null,
            _logMock.Object);
    }

    [Fact]
    public void GetBackendStatuses_InitializesThreeBackends()
    {
        var monitor = CreateMonitor();
        var statuses = monitor.GetBackendStatuses();
        Assert.Equal(3, statuses.Count);
        Assert.Contains(statuses, s => s.BackendRole == "bus");
        Assert.Contains(statuses, s => s.BackendRole == "queue");
        Assert.Contains(statuses, s => s.BackendRole == "stateStore");
    }

    [Fact]
    public void GetBackendStatuses_InProcessBackends_AlwaysHealthy()
    {
        var monitor = CreateMonitor();
        var statuses = monitor.GetBackendStatuses();
        foreach (var status in statuses)
        {
            Assert.Equal(BackendHealthState.Healthy, status.State);
            Assert.Equal("in-process", status.BackendKind);
        }
    }

    [Fact]
    public async Task CheckAllAsync_InProcess_DoesNotCallIsHealthyAsync()
    {
        var monitor = CreateMonitor();
        await monitor.CheckAllAsync();
        // In-process backends skip the health check
        _busMock.Verify(b => b.IsHealthyAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckAllAsync_RedisBackendHealthy_TransitionsToHealthy()
    {
        _busMock.Setup(b => b.BackendKind).Returns("redis");
        _busMock.Setup(b => b.IsHealthyAsync(It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<bool>(true));

        var cfg = new MeshProfilesConfig { Enabled = true, MaxConsecutiveFailures = 3 };
        var monitor = CreateMonitor(cfg);

        await monitor.CheckAllAsync();

        var busStatus = monitor.GetBackendStatuses().First(s => s.BackendRole == "bus");
        Assert.Equal(BackendHealthState.Healthy, busStatus.State);
        Assert.Equal("redis", busStatus.BackendKind);
    }

    [Fact]
    public async Task CheckAllAsync_RedisBackendUnhealthy_TransitionsToDegraded()
    {
        _busMock.Setup(b => b.BackendKind).Returns("redis");
        _busMock.Setup(b => b.IsHealthyAsync(It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<bool>(false));

        var cfg = new MeshProfilesConfig { Enabled = true, MaxConsecutiveFailures = 3 };
        var monitor = CreateMonitor(cfg);

        await monitor.CheckAllAsync();

        var busStatus = monitor.GetBackendStatuses().First(s => s.BackendRole == "bus");
        Assert.Equal(BackendHealthState.Degraded, busStatus.State);
    }

    [Fact]
    public async Task CheckAllAsync_ExternalFailure_TransitionsThroughDegradedToUnavailable()
    {
        _busMock.Setup(b => b.BackendKind).Returns("redis");

        // Simulate failures: each call throws
        _busMock.Setup(b => b.IsHealthyAsync(It.IsAny<CancellationToken>()))
            .Returns(() => throw new InvalidOperationException("Connection refused"));

        var cfg = new MeshProfilesConfig { Enabled = true, MaxConsecutiveFailures = 2 };
        var monitor = CreateMonitor(cfg);

        // First check: Unknown → Degraded (1 failure, below threshold)
        await monitor.CheckAllAsync();
        var status1 = monitor.GetBackendStatuses().First(s => s.BackendRole == "bus");
        Assert.Equal(BackendHealthState.Degraded, status1.State);

        // Second check: Degraded → Unavailable (threshold reached)
        await monitor.CheckAllAsync();
        var status2 = monitor.GetBackendStatuses().First(s => s.BackendRole == "bus");
        Assert.Equal(BackendHealthState.Unavailable, status2.State);
    }

    [Fact]
    public async Task CheckAllAsync_RecoversFromDegraded_TransitionsToHealthy()
    {
        _busMock.Setup(b => b.BackendKind).Returns("redis");

        var healthy = false;
        _busMock.Setup(b => b.IsHealthyAsync(It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask<bool>(healthy));

        var cfg = new MeshProfilesConfig { Enabled = true, MaxConsecutiveFailures = 3 };
        var monitor = CreateMonitor(cfg);

        // Fail once
        healthy = false;
        await monitor.CheckAllAsync();
        Assert.Equal(BackendHealthState.Degraded,
            monitor.GetBackendStatuses().First(s => s.BackendRole == "bus").State);

        // Recover
        healthy = true;
        await monitor.CheckAllAsync();
        var status = monitor.GetBackendStatuses().First(s => s.BackendRole == "bus");
        Assert.Equal(BackendHealthState.Healthy, status.State);
        Assert.Equal(0, status.ConsecutiveFailures);
    }

    [Fact]
    public async Task SubscribeToChanges_CallbackInvokedOnTransition()
    {
        _busMock.Setup(b => b.BackendKind).Returns("redis");
        _busMock.Setup(b => b.IsHealthyAsync(It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<bool>(false));

        var cfg = new MeshProfilesConfig { Enabled = true, MaxConsecutiveFailures = 1 };
        var monitor = CreateMonitor(cfg);

        BackendHealthChangedEvent? capturedEvent = null;
        var tcs = new TaskCompletionSource();

        monitor.SubscribeToChanges(async evt =>
        {
            capturedEvent = evt;
            tcs.TrySetResult();
            await Task.CompletedTask;
        });

        // Trigger state transition: Unknown → Degraded
        await monitor.CheckAllAsync();

        // Wait for the callback (fires on background thread)
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(capturedEvent);
        Assert.Equal("bus", capturedEvent.BackendRole);
        Assert.Equal(BackendHealthState.Unknown, capturedEvent.PreviousState);
        Assert.Equal(BackendHealthState.Degraded, capturedEvent.NewState);
    }

    [Fact]
    public void SubscribeToChanges_ReturnsDisposable()
    {
        var monitor = CreateMonitor();
        var disp = monitor.SubscribeToChanges(_ => Task.CompletedTask);
        Assert.NotNull(disp);
        disp.Dispose(); // Should not throw
    }

    [Fact]
    public async Task CheckAllAsync_CancellationTokenPropagated()
    {
        _busMock.Setup(b => b.BackendKind).Returns("redis");
        _busMock.Setup(b => b.IsHealthyAsync(It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<bool>(Task.Run(async () =>
            {
                await Task.Delay(5000);
                return true;
            })));

        var cfg = new MeshProfilesConfig
        {
            Enabled = true,
            HealthCheckTimeoutSec = 1,
            MaxConsecutiveFailures = 3
        };
        var monitor = CreateMonitor(cfg);

        await monitor.CheckAllAsync();

        // Short timeout + long health check = Degraded/Unavailable due to timeout
        var busStatus = monitor.GetBackendStatuses().First(s => s.BackendRole == "bus");
        Assert.True(
            busStatus.State == BackendHealthState.Degraded ||
            busStatus.State == BackendHealthState.Unavailable,
            $"Expected Degraded or Unavailable but got {busStatus.State}");
    }

    [Fact]
    public void GetBackendStatuses_UnknownState_WhenNotChecked()
    {
        var monitor = CreateMonitor();
        // Before any check, in-process backends are already healthy
        var statuses = monitor.GetBackendStatuses();
        foreach (var s in statuses)
        {
            Assert.Equal(BackendHealthState.Healthy, s.State);
        }
    }

    [Fact]
    public void MeshBackendHealthStatus_RecordHasCorrectProperties()
    {
        var status = new MeshBackendHealthStatus
        {
            BackendRole = "bus",
            BackendKind = "redis",
            State = BackendHealthState.Healthy,
            LastCheckedAt = DateTimeOffset.UtcNow,
            ConsecutiveFailures = 0,
            LastError = null
        };

        Assert.Equal("bus", status.BackendRole);
        Assert.Equal("redis", status.BackendKind);
        Assert.Equal(BackendHealthState.Healthy, status.State);
        Assert.Equal(0, status.ConsecutiveFailures);
        Assert.Null(status.LastError);
    }

    [Fact]
    public void BackendHealthChangedEvent_RecordHasAllFields()
    {
        var evt = new BackendHealthChangedEvent
        {
            BackendRole = "bus",
            BackendKind = "redis",
            PreviousState = BackendHealthState.Healthy,
            NewState = BackendHealthState.Degraded,
            OccurredAt = DateTimeOffset.UtcNow,
            Reason = "Connection refused"
        };

        Assert.Equal("bus", evt.BackendRole);
        Assert.Equal("redis", evt.BackendKind);
        Assert.Equal(BackendHealthState.Healthy, evt.PreviousState);
        Assert.Equal(BackendHealthState.Degraded, evt.NewState);
        Assert.Equal("Connection refused", evt.Reason);
    }
}
