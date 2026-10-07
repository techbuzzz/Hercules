using Hercules.Budget;
using Hercules.Config;
using Hercules.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Budget;

/// <summary>
///     Тесты BudgetGuard: CheckAndGetDegradationMessage, LogSoftWarnings, CheckTokensBeforeRequest.
/// </summary>
public class BudgetGuardTests
{
    private readonly Mock<ILogger<BudgetGuard>> _loggerMock;
    private readonly BudgetGuard _guard;

    public BudgetGuardTests()
    {
        _loggerMock = new Mock<ILogger<BudgetGuard>>();
        var cfg = new BudgetConfig { Enabled = true };
        _guard = new BudgetGuard(cfg, _loggerMock.Object);
    }

    [Fact]
    public void CheckAndGetDegradationMessage_WhenDisabled_ReturnsNull()
    {
        var guard = new BudgetGuard(new BudgetConfig { Enabled = false }, _loggerMock.Object);
        var result = new GuardrailCheckResult(Array.Empty<GuardrailViolation>(), false);
        Assert.Null(guard.CheckAndGetDegradationMessage(result));
    }

    [Fact]
    public void CheckAndGetDegradationMessage_WhenNoViolations_ReturnsNull()
    {
        var result = new GuardrailCheckResult(Array.Empty<GuardrailViolation>(), false);
        Assert.Null(_guard.CheckAndGetDegradationMessage(result));
    }

    [Fact]
    public void CheckAndGetDegradationMessage_WhenOnlySoftWarn_ReturnsNull()
    {
        var violations = new List<GuardrailViolation>
        {
            new(GuardrailLimitType.CostPerDay, 100, 150, "Cost per day exceeded: $1.5/$1.0", "soft_warn")
        };
        var result = new GuardrailCheckResult(violations, false);
        Assert.Null(_guard.CheckAndGetDegradationMessage(result));
    }

    [Fact]
    public void CheckAndGetDegradationMessage_WhenHardCapViolation_ReturnsMessage()
    {
        var violations = new List<GuardrailViolation>
        {
            new(GuardrailLimitType.ToolCallsPerRequest, 3, 5, "Tool calls per request exceeded: 5/3", "hard_cap")
        };
        var result = new GuardrailCheckResult(violations, true);
        var msg = _guard.CheckAndGetDegradationMessage(result);
        Assert.NotNull(msg);
        // Source: BudgetGuard.cs использует "Превышен" (capital P) — корректно для русского.
        // OrdinalIgnoreCase делает тест устойчивым к возможной смене регистра в source.
        Assert.Contains("Превышен лимит безопасности", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tool calls per request exceeded", msg);
    }

    [Fact]
    public void CheckAndGetDegradationMessage_WhenMultipleHardCap_ReturnsAllInMessage()
    {
        var violations = new List<GuardrailViolation>
        {
            new(GuardrailLimitType.ToolCallsPerRequest, 3, 5, "Tool calls: 5/3", "hard_cap"),
            new(GuardrailLimitType.TokensPerRequest, 1000, 2000, "Tokens: 2000/1000", "hard_cap")
        };
        var result = new GuardrailCheckResult(violations, true);
        var msg = _guard.CheckAndGetDegradationMessage(result);
        Assert.NotNull(msg);
        Assert.Contains("Tool calls: 5/3", msg);
        Assert.Contains("Tokens: 2000/1000", msg);
    }

    [Fact]
    public void CheckTokensBeforeRequest_WhenUnderLimit_ReturnsNull()
    {
        var guard = new BudgetGuard(new BudgetConfig { Enabled = true, MaxTokensPerRequest = 1000 }, _loggerMock.Object);
        Assert.Null(guard.CheckTokensBeforeRequest(500));
    }

    [Fact]
    public void CheckTokensBeforeRequest_WhenOverLimit_ReturnsMessage()
    {
        var guard = new BudgetGuard(new BudgetConfig { Enabled = true, MaxTokensPerRequest = 1000 }, _loggerMock.Object);
        var msg = guard.CheckTokensBeforeRequest(2000);
        Assert.NotNull(msg);
        Assert.Contains("2000", msg);
        Assert.Contains("1000", msg);
    }

    [Fact]
    public void CheckTokensBeforeRequest_WhenDisabled_ReturnsNull()
    {
        var guard = new BudgetGuard(new BudgetConfig { Enabled = false, MaxTokensPerRequest = 100 }, _loggerMock.Object);
        Assert.Null(guard.CheckTokensBeforeRequest(5000));
    }

    [Fact]
    public void CheckTokensBeforeRequest_WhenNoLimitConfigured_ReturnsNull()
    {
        var guard = new BudgetGuard(new BudgetConfig { Enabled = true, MaxTokensPerRequest = 0 }, _loggerMock.Object);
        Assert.Null(guard.CheckTokensBeforeRequest(10000));
    }

    [Fact]
    public void LogSoftWarnings_WhenHardCapOnly_DoesNotLog()
    {
        var violations = new List<GuardrailViolation>
        {
            new(GuardrailLimitType.TokensPerRequest, 1000, 2000, "Tokens exceeded", "hard_cap")
        };
        var result = new GuardrailCheckResult(violations, true);
        _guard.LogSoftWarnings(result);
        _loggerMock.Verify(
            l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<object>(), null, It.IsAny<Func<object, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public void LogSoftWarnings_WhenSoftWarn_LogsWarning()
    {
        var violations = new List<GuardrailViolation>
        {
            new(GuardrailLimitType.CostPerDay, 100, 150, "Cost per day exceeded", "soft_warn")
        };
        var result = new GuardrailCheckResult(violations, false);
        _guard.LogSoftWarnings(result);
        _loggerMock.Verify(
            l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void LogSoftWarnings_WhenDisabled_DoesNotLog()
    {
        var guard = new BudgetGuard(new BudgetConfig { Enabled = false }, _loggerMock.Object);
        var violations = new List<GuardrailViolation>
        {
            new(GuardrailLimitType.CostPerDay, 100, 200, "Cost exceeded", "soft_warn")
        };
        guard.LogSoftWarnings(new GuardrailCheckResult(violations, false));
        _loggerMock.Verify(
            l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}

/// <summary>
///     Тесты GuardrailService: CheckLimits, RecordUsage, GetStatus, ResetRequestCounters.
/// </summary>
public class GuardrailServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteSessionStore _store;
    private readonly IBudgetService _budget;
    private readonly GuardrailService _svc;
    private readonly string _sessionId;

    public GuardrailServiceTests()
    {
        _sessionId = Guid.NewGuid().ToString("N")[..8];
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-guardrail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var storageCfg = new StorageConfig { DataRoot = _tempDir };
        _store = new SqliteSessionStore(storageCfg);
        _budget = new BudgetService(_store);
        var budgetCfg = new BudgetConfig
        {
            Enabled = true,
            MaxTokensPerRequest = 1000,
            MaxToolCallsPerRequest = 5,
            MaxRetriesPerTool = 2,
            MaxCostPerDayUsd = 10m,
            MaxTokensPerDay = 5000,
            MaxCallsPerDay = 100
        };
        _svc = new GuardrailService(budgetCfg, _budget);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    [Fact]
    public void CheckLimits_WhenDisabled_ReturnsNoViolations()
    {
        var guard = new GuardrailService(new BudgetConfig { Enabled = false }, _budget);
        var result = guard.CheckLimits(_sessionId);
        Assert.Empty(result.Violations);
        Assert.False(result.HasHardViolation);
    }

    [Fact]
    public void CheckLimits_UnderAllLimits_ReturnsNoViolations()
    {
        var result = _svc.CheckLimits(_sessionId);
        Assert.Empty(result.Violations);
        Assert.False(result.HasHardViolation);
    }

    [Fact]
    public void CheckLimits_TokensOverPerRequest_ReturnsHardCapViolation()
    {
        _svc.RecordLlmUsage(_sessionId, 800, 500, 0.001m, "yandexgpt");
        var result = _svc.CheckLimits(_sessionId);
        var tokenViolation = result.Violations.FirstOrDefault(v => v.Type == GuardrailLimitType.TokensPerRequest);
        Assert.NotNull(tokenViolation);
        Assert.Equal("hard_cap", tokenViolation.EnforcementMode);
    }

    [Fact]
    public void CheckLimits_ToolCallsOverLimit_ReturnsHardCapViolation()
    {
        for (var i = 0; i < 6; i++)
            _svc.RecordToolCall(_sessionId);
        var result = _svc.CheckLimits(_sessionId);
        var toolViolation = result.Violations.FirstOrDefault(v => v.Type == GuardrailLimitType.ToolCallsPerRequest);
        Assert.NotNull(toolViolation);
        Assert.Equal("hard_cap", toolViolation.EnforcementMode);
    }

    [Fact]
    public void CheckLimits_RetriesOverLimit_ReturnsHardCapViolation()
    {
        _svc.RecordToolRetry(_sessionId);
        _svc.RecordToolRetry(_sessionId);
        _svc.RecordToolRetry(_sessionId);
        var result = _svc.CheckLimits(_sessionId);
        var retryViolation = result.Violations.FirstOrDefault(v => v.Type == GuardrailLimitType.RetriesPerTool);
        Assert.NotNull(retryViolation);
        Assert.Equal("hard_cap", retryViolation.EnforcementMode);
    }

    [Fact]
    public void CheckLimits_CostPerDayOverLimit_ReturnsSoftWarnViolation()
    {
        // 15 USD > 10 USD limit configured in constructor
        _svc.RecordLlmUsage(_sessionId, 500, 500, 15m, "yandexgpt");
        var result = _svc.CheckLimits(_sessionId);
        var costViolation = result.Violations.FirstOrDefault(v => v.Type == GuardrailLimitType.CostPerDay);
        Assert.NotNull(costViolation);
        Assert.Equal("soft_warn", costViolation.EnforcementMode);
    }

    [Fact]
    public void ResetRequestCounters_AfterRecording_ResetsToolCalls()
    {
        _svc.RecordToolCall(_sessionId);
        _svc.RecordToolCall(_sessionId);
        _svc.ResetRequestCounters(_sessionId);
        var result = _svc.CheckLimits(_sessionId);
        var toolViolation = result.Violations.FirstOrDefault(v => v.Type == GuardrailLimitType.ToolCallsPerRequest);
        Assert.Null(toolViolation);
    }

    [Fact]
    public void GetStatus_ReturnsAllLimitTypes()
    {
        var statuses = _svc.GetStatus(_sessionId);
        Assert.Equal(7, statuses.Count);
        Assert.Contains(statuses, s => s.Type == GuardrailLimitType.TokensPerRequest);
        Assert.Contains(statuses, s => s.Type == GuardrailLimitType.ToolCallsPerRequest);
        Assert.Contains(statuses, s => s.Type == GuardrailLimitType.RetriesPerTool);
        Assert.Contains(statuses, s => s.Type == GuardrailLimitType.WallClockSecondsPerRequest);
        Assert.Contains(statuses, s => s.Type == GuardrailLimitType.CostPerDay);
        Assert.Contains(statuses, s => s.Type == GuardrailLimitType.TokensPerDay);
        Assert.Contains(statuses, s => s.Type == GuardrailLimitType.CallsPerDay);
    }

    [Fact]
    public void GetRequestCounters_ReturnsSameInstance()
    {
        _svc.RecordToolCall(_sessionId);
        var c1 = _svc.GetRequestCounters(_sessionId);
        var c2 = _svc.GetRequestCounters(_sessionId);
        Assert.Same(c1, c2);
        Assert.Equal(1, c1.ToolCalls);
    }

    [Fact]
    public void CheckLimits_NoLimitConfigured_ReturnsNoViolations()
    {
        var guard = new GuardrailService(new BudgetConfig { Enabled = true }, _budget);
        guard.RecordLlmUsage(_sessionId, 999999, 999999, 999m, "yandexgpt");
        for (var i = 0; i < 1000; i++) guard.RecordToolCall(_sessionId);
        var result = guard.CheckLimits(_sessionId);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void RecordElapsedTime_UpdatesCounter()
    {
        _svc.RecordElapsedTime(_sessionId, 30000);
        var counters = _svc.GetRequestCounters(_sessionId);
        Assert.Equal(30000, counters.ElapsedMilliseconds);
    }
}
