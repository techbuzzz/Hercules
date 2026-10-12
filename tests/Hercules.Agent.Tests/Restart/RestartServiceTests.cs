using Hercules.Audit;
using Hercules.Restart;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Restart;

/// <summary>
///     task_099: unit-тесты <see cref="RestartService"/>.
///     Покрывает: request/clear, persistence across instances, auto-clear on startup,
///     audit-вызовы, validation, isolation от concurrent state.
/// </summary>
public class RestartServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _stateFile;

    public RestartServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hercules-restart-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _stateFile = Path.Combine(_tempDir, "restart-state.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
        }
    }

    private static (RestartService svc, Mock<IAuditService> audit) NewService(string stateFile)
    {
        var audit = new Mock<IAuditService>(MockBehavior.Loose);
        audit.Setup(a => a.LogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var svc = new RestartService(
            NullLogger<RestartService>.Instance,
            audit.Object,
            stateFile);
        return (svc, audit);
    }

    [Fact]
    public void FreshService_HasNoPendingFlag()
    {
        var (svc, _) = NewService(_stateFile);

        Assert.False(svc.IsRestartPending());
        var state = svc.GetStatus();
        Assert.False(state.Pending);
        Assert.Null(state.RequestedAt);
        Assert.Null(state.Reason);
        Assert.Null(state.RequestedBy);
    }

    [Fact]
    public void RequestRestart_SetsPendingFlag_AndReturnsSnapshot()
    {
        var (svc, audit) = NewService(_stateFile);

        var state = svc.RequestRestart("config update", "operator@example.com");

        Assert.True(state.Pending);
        Assert.NotNull(state.RequestedAt);
        Assert.Equal("config update", state.Reason);
        Assert.Equal("operator@example.com", state.RequestedBy);
        Assert.True(svc.IsRestartPending());

        // Audit-вызов fire-and-forget — ждём немного, чтобы он успел выполниться.
        SpinWait.SpinUntil(() => audit.Invocations.Count > 0, TimeSpan.FromSeconds(1));
        audit.Verify(a => a.LogAsync(
            "operator@example.com", "restart_requested", null, "config update",
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void RequestRestart_WithEmptyReason_StoresNull()
    {
        var (svc, _) = NewService(_stateFile);

        var state = svc.RequestRestart("   ", "studio");

        Assert.True(state.Pending);
        Assert.Null(state.Reason); // whitespace → null
        Assert.Equal("studio", state.RequestedBy);
    }

    [Fact]
    public void RequestRestart_WithEmptyActor_DefaultsToSystem()
    {
        var (svc, _) = NewService(_stateFile);

        var state = svc.RequestRestart("test", "  ");

        Assert.Equal("system", state.RequestedBy);
    }

    [Fact]
    public void ClearRestartRequest_WhenPending_RemovesFlagAndAudits()
    {
        var (svc, audit) = NewService(_stateFile);
        svc.RequestRestart("test", "operator");

        var cleared = svc.ClearRestartRequest("supervisor");

        Assert.True(cleared);
        Assert.False(svc.IsRestartPending());
        var state = svc.GetStatus();
        Assert.False(state.Pending);
        Assert.Null(state.RequestedAt);

        SpinWait.SpinUntil(() => audit.Invocations.Count >= 2, TimeSpan.FromSeconds(1));
        audit.Verify(a => a.LogAsync(
            "supervisor", "restart_cleared", null, null,
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ClearRestartRequest_WhenAlreadyClean_ReturnsFalse_NoAudit()
    {
        var (svc, audit) = NewService(_stateFile);

        var cleared = svc.ClearRestartRequest("operator");

        Assert.False(cleared);
        audit.Verify(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void RestartState_PersistsToFile()
    {
        var (svc, _) = NewService(_stateFile);
        svc.RequestRestart("persist test", "operator");

        // Файл должен существовать и содержать сериализованное состояние.
        Assert.True(File.Exists(_stateFile));
        var json = File.ReadAllText(_stateFile);
        Assert.Contains("\"pending\": true", json);
        Assert.Contains("persist test", json);
        Assert.Contains("operator", json);
    }

    [Fact]
    public void ClearRestartRequest_PersistsCleanStateToFile()
    {
        var (svc, _) = NewService(_stateFile);
        svc.RequestRestart("test", "operator");
        svc.ClearRestartRequest("supervisor");

        var json = File.ReadAllText(_stateFile);
        Assert.Contains("\"pending\": false", json);
    }

    [Fact]
    public void RestartService_StartupWithPendingFlag_AutoClears()
    {
        // Эмулируем "предыдущий restart был запрошен, supervisor его выполнил"
        File.WriteAllText(_stateFile, """
        {
          "pending": true,
          "requestedAt": "2026-08-17T10:00:00Z",
          "reason": "old restart",
          "requestedBy": "operator"
        }
        """);

        var (svc, _) = NewService(_stateFile);

        // Auto-clear при старте — flag сброшен, состояние чистое.
        Assert.False(svc.IsRestartPending());
        var state = svc.GetStatus();
        Assert.False(state.Pending);
        Assert.Null(state.RequestedAt);
    }

    [Fact]
    public void RestartService_StartupWithCorruptedFile_FallsBackToClean()
    {
        File.WriteAllText(_stateFile, "{ this is not valid json ");

        var (svc, _) = NewService(_stateFile);

        // При повреждённом файле — стартуем чисто (graceful degradation).
        Assert.False(svc.IsRestartPending());
    }

    [Fact]
    public void RestartService_StartupWithoutFile_StartsClean()
    {
        // File не существует — обычный cold start.
        Assert.False(File.Exists(_stateFile));

        var (svc, _) = NewService(_stateFile);

        Assert.False(svc.IsRestartPending());
    }

    [Fact]
    public void RequestRestart_OverwritesPreviousRequest()
    {
        var (svc, _) = NewService(_stateFile);
        svc.RequestRestart("first", "operator-A");
        Thread.Sleep(15);
        var second = svc.RequestRestart("second", "operator-B");

        Assert.Equal("second", second.Reason);
        Assert.Equal("operator-B", second.RequestedBy);
        var state = svc.GetStatus();
        Assert.Equal("second", state.Reason);
    }

    [Fact]
    public void Audit_FailureDoesNotBreakRestartFlow()
    {
        // Audit-сервис, который всегда кидает exception.
        var throwingAudit = new Mock<IAuditService>(MockBehavior.Strict);
        throwingAudit.Setup(a => a.LogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("audit DB down"));

        // Конструктор не должен падать, RequestRestart не должен падать.
        var svc = new RestartService(
            NullLogger<RestartService>.Instance,
            throwingAudit.Object,
            _stateFile);

        var state = svc.RequestRestart("test", "operator");

        Assert.True(state.Pending);
        Assert.True(svc.IsRestartPending());
    }

    [Fact]
    public void GetStatus_ReturnsConsistentSnapshot_UnderConcurrentRead()
    {
        var (svc, _) = NewService(_stateFile);
        svc.RequestRestart("concurrent", "op");

        // Concurrent readers + один writer — нет data race, не падает.
        var statuses = new System.Collections.Concurrent.ConcurrentBag<RestartState>();
        Parallel.For(0, 100, _ =>
        {
            statuses.Add(svc.GetStatus());
        });

        Assert.Equal(100, statuses.Count);
        Assert.All(statuses, s => Assert.True(s.Pending));
    }
}
