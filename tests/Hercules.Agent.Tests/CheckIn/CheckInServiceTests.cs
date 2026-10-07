using Hercules.Audit;
using Hercules.CheckIn;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.CheckIn;

/// <summary>
///     task_098: unit-тесты <see cref="CheckInService"/> (ADR-0005).
///     Покрывает: успешный checkin, отказ при дубле contribute, идемпотентный re-checkin,
///     параллельный system monitor, heartbeat, checkout, force, TTL-expiry, dispose safety.
/// </summary>
public class CheckInServiceTests
{
    private static (CheckInService svc, Mock<IAuditService> audit) NewService(
        TimeSpan? ttl = null,
        TimeSpan? cleanupInterval = null)
    {
        var audit = new Mock<IAuditService>(MockBehavior.Loose);
        audit.Setup(a => a.LogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var svc = new CheckInService(
            NullLogger<CheckInService>.Instance,
            audit.Object,
            ttl,
            cleanupInterval);
        return (svc, audit);
    }

    [Fact]
    public void FirstContributeCheckIn_Succeeds_AndMarksAgentOccupied()
    {
        var (svc, _) = NewService();

        var result = svc.CheckIn("studio-A", "Studio A", CheckInRole.Contribute);

        Assert.True(result.Success);
        Assert.Null(result.Error);
        var status = svc.GetStatus();
        Assert.True(status.CheckedOut);
        Assert.Equal("Studio A", status.CheckedOutBy);
        Assert.Equal("contribute", status.Role);
        Assert.Equal(CheckInService.DefaultTtlSeconds, status.TtlSeconds);
        Assert.NotNull(status.CheckedOutAt);
    }

    [Fact]
    public void SecondContributeCheckIn_ByDifferentStudio_IsRefused()
    {
        var (svc, _) = NewService();
        svc.CheckIn("studio-A", "Studio A", CheckInRole.Contribute);

        var result = svc.CheckIn("studio-B", "Studio B", CheckInRole.Contribute);

        Assert.False(result.Success);
        Assert.Contains("Studio A", result.Error);
        var status = svc.GetStatus();
        Assert.True(status.CheckedOut);
        Assert.Equal("Studio A", status.CheckedOutBy);
    }

    [Fact]
    public void CheckIn_BySameStudioId_IsIdempotent_AndRefreshesHeartbeat()
    {
        var (svc, _) = NewService();
        svc.CheckIn("studio-A", "Studio A v1", CheckInRole.Contribute);
        var firstCheckInAt = svc.GetStatus().CheckedOutAt;

        // Re-checkin тем же studioId — должно быть success, без отказа, обновляет heartbeat.
        Thread.Sleep(15);
        var result = svc.CheckIn("studio-A", "Studio A v2", CheckInRole.Contribute);

        Assert.True(result.Success);
        var status = svc.GetStatus();
        Assert.Equal("Studio A v2", status.CheckedOutBy);
        Assert.Equal(firstCheckInAt, status.CheckedOutAt); // CheckedInAt не меняется
    }

    [Fact]
    public void SystemRoleCheckIn_DoesNotMarkAgentOccupied_AndAllowsParallelMonitors()
    {
        var (svc, audit) = NewService();

        var result1 = svc.CheckIn("monitor-1", "Monitor One", CheckInRole.System);
        var result2 = svc.CheckIn("monitor-2", "Monitor Two", CheckInRole.System);

        Assert.True(result1.Success);
        Assert.True(result2.Success);
        var status = svc.GetStatus();
        Assert.False(status.CheckedOut);
        Assert.Null(status.CheckedOutBy);
        // Оба monitor'а записали audit-событие checkin_monitor.
        audit.Verify(a => a.LogAsync(
            It.Is<string>(s => s == "monitor-1"),
            It.Is<string>(s => s == "checkin_monitor"),
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(a => a.LogAsync(
            It.Is<string>(s => s == "monitor-2"),
            It.Is<string>(s => s == "checkin_monitor"),
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void SystemCheckIn_ThenContributeCheckIn_StillSucceeds()
    {
        var (svc, _) = NewService();
        svc.CheckIn("monitor-1", "Monitor", CheckInRole.System);

        var result = svc.CheckIn("studio-A", "Studio A", CheckInRole.Contribute);

        Assert.True(result.Success);
        Assert.True(svc.GetStatus().CheckedOut);
    }

    [Fact]
    public void Heartbeat_UpdatesLastHeartbeat_ForActiveContribute()
    {
        var (svc, _) = NewService();
        svc.CheckIn("studio-A", "Studio A", CheckInRole.Contribute);

        var ok = svc.Heartbeat("studio-A", CheckInRole.Contribute);

        Assert.True(ok);
    }

    [Fact]
    public void Heartbeat_ReturnsFalse_WhenNoActiveSession()
    {
        var (svc, _) = NewService();

        var ok = svc.Heartbeat("studio-A", CheckInRole.Contribute);

        Assert.False(ok);
    }

    [Fact]
    public void Heartbeat_ReturnsFalse_ForDifferentStudioId()
    {
        var (svc, _) = NewService();
        svc.CheckIn("studio-A", "Studio A", CheckInRole.Contribute);

        var ok = svc.Heartbeat("studio-B", CheckInRole.Contribute);

        Assert.False(ok);
    }

    [Fact]
    public void Heartbeat_ForSystemRole_IsNoOpSuccess()
    {
        var (svc, _) = NewService();

        var ok = svc.Heartbeat("monitor-1", CheckInRole.System);

        Assert.True(ok);
    }

    [Fact]
    public void CheckOut_ReleasesAgent_ForMatchingStudio()
    {
        var (svc, audit) = NewService();
        svc.CheckIn("studio-A", "Studio A", CheckInRole.Contribute);

        var ok = svc.CheckOut("studio-A", CheckInRole.Contribute);

        Assert.True(ok);
        Assert.False(svc.GetStatus().CheckedOut);
        audit.Verify(a => a.LogAsync(
            "studio-A", "checkout", "Studio A", null,
            null, null, null, null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void CheckOut_ReturnsFalse_WhenNoActiveSession()
    {
        var (svc, _) = NewService();

        var ok = svc.CheckOut("studio-A", CheckInRole.Contribute);

        Assert.False(ok);
    }

    [Fact]
    public void CheckOut_ForSystemRole_IsNoOpSuccess()
    {
        var (svc, _) = NewService();

        var ok = svc.CheckOut("monitor-1", CheckInRole.System);

        Assert.True(ok);
    }

    [Fact]
    public void ForceCheckOut_ReleasesAgent_AndAuditsActor()
    {
        var (svc, audit) = NewService();
        svc.CheckIn("studio-A", "Studio A", CheckInRole.Contribute);

        var result = svc.ForceCheckOut("admin@hercules");

        Assert.True(result.Success);
        Assert.False(svc.GetStatus().CheckedOut);
        audit.Verify(a => a.LogAsync(
            "admin@hercules", "force_checkout", "studio-A", It.IsAny<string?>(),
            null, null, null, null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ForceCheckOut_OnEmptyAgent_IsNoOpSuccess()
    {
        var (svc, audit) = NewService();

        var result = svc.ForceCheckOut("admin@hercules");

        Assert.True(result.Success);
        Assert.False(svc.GetStatus().CheckedOut);
        // Без активной сессии — audit не пишется.
        audit.Verify(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ForceCheckOut_WithoutBy_DefaultsActorToSystem()
    {
        var (svc, audit) = NewService();
        svc.CheckIn("studio-A", "Studio A", CheckInRole.Contribute);

        svc.ForceCheckOut(by: null);

        audit.Verify(a => a.LogAsync(
            "system", "force_checkout", "studio-A", It.IsAny<string?>(),
            null, null, null, null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void TtlExpiry_AutoCheckOutsExpiredStudio()
    {
        // TTL 50ms, cleanup timer отключён (Zero) — тестируем через internal CleanupExpired.
        var (svc, audit) = NewService(
            ttl: TimeSpan.FromMilliseconds(50),
            cleanupInterval: TimeSpan.Zero);
        svc.CheckIn("studio-A", "Studio A", CheckInRole.Contribute);
        Assert.True(svc.GetStatus().CheckedOut);

        Thread.Sleep(120);
        svc.CleanupExpired();

        Assert.False(svc.GetStatus().CheckedOut);
        audit.Verify(a => a.LogAsync(
            "system", "auto_checkout_ttl_expired", "studio-A", "Studio A",
            null, null, null, null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void TtlExpiry_DoesNotEvictStudioWithRecentHeartbeat()
    {
        var (svc, audit) = NewService(
            ttl: TimeSpan.FromMilliseconds(100),
            cleanupInterval: TimeSpan.Zero);
        svc.CheckIn("studio-A", "Studio A", CheckInRole.Contribute);

        // Heartbeat в середине TTL — должно держать сессию живой.
        Thread.Sleep(60);
        Assert.True(svc.Heartbeat("studio-A", CheckInRole.Contribute));
        Thread.Sleep(60);
        svc.CleanupExpired();

        Assert.True(svc.GetStatus().CheckedOut);
        audit.Verify(a => a.LogAsync(
            It.IsAny<string>(), "auto_checkout_ttl_expired", It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void StatusReflectsEmptyAgent_Initially()
    {
        var (svc, _) = NewService();

        var status = svc.GetStatus();

        Assert.False(status.CheckedOut);
        Assert.Null(status.CheckedOutBy);
        Assert.Null(status.CheckedOutAt);
        Assert.Null(status.Role);
    }

    [Fact]
    public void CheckIn_EmptyStudioId_Throws()
    {
        var (svc, _) = NewService();

        Assert.Throws<ArgumentException>(() =>
            svc.CheckIn("", "Studio", CheckInRole.Contribute));
    }

    [Fact]
    public void CheckIn_EmptyStudioName_Throws()
    {
        var (svc, _) = NewService();

        Assert.Throws<ArgumentException>(() =>
            svc.CheckIn("studio-A", "", CheckInRole.Contribute));
    }

    [Fact]
    public void Dispose_StopsTimer_AndRejectsFurtherCheckIn()
    {
        var (svc, _) = NewService();
        svc.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            svc.CheckIn("studio-A", "Studio A", CheckInRole.Contribute));
    }

    [Fact]
    public void AuditFailure_DoesNotPropagate_ToCheckIn()
    {
        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("audit db down"));

        var svc = new CheckInService(
            NullLogger<CheckInService>.Instance,
            audit.Object,
            cleanupInterval: TimeSpan.Zero);

        // CheckIn не должен ломаться при падающем audit.
        var result = svc.CheckIn("studio-A", "Studio A", CheckInRole.Contribute);
        Assert.True(result.Success);
    }
}
