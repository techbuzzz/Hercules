using Hercules.Quotas;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Quotas;

/// <summary>
///     Тесты QuotaGuard: CheckAndGetDegradationMessage, LogSoftWarnings, CheckBeforeAction.
/// </summary>
public class QuotaGuardTests
{
    private readonly Mock<ILogger<QuotaGuard>> _loggerMock;
    private readonly QuotaGuard _guard;

    public QuotaGuardTests()
    {
        _loggerMock = new Mock<ILogger<QuotaGuard>>();
        _guard = new QuotaGuard(_loggerMock.Object);
    }

    [Fact]
    public void CheckAndGetDegradationMessage_WhenNoViolations_ReturnsNull()
    {
        var result = new QuotaCheckResult(Array.Empty<QuotaViolation>(), false, false);
        Assert.Null(_guard.CheckAndGetDegradationMessage(result));
    }

    [Fact]
    public void CheckAndGetDegradationMessage_WhenOnlySoftWarnings_ReturnsNull()
    {
        var violations = new List<QuotaViolation>
        {
            new(QuotaLimitType.TokensPerDayPerAgent, QuotaScope.Agent, "hercules",
                1000000, 1100000, "Tokens per day exceeded", "soft_warn")
        };
        var result = new QuotaCheckResult(violations, false, true);

        Assert.Null(_guard.CheckAndGetDegradationMessage(result));
    }

    [Fact]
    public void CheckAndGetDegradationMessage_WhenHardViolation_ReturnsMessage()
    {
        var violations = new List<QuotaViolation>
        {
            new(QuotaLimitType.ConcurrentRequestsPerAgent, QuotaScope.Agent, "hercules",
                5, 10, "Concurrent requests exceeded", "hard_cap")
        };
        var result = new QuotaCheckResult(violations, true, false);

        var message = _guard.CheckAndGetDegradationMessage(result);
        Assert.NotNull(message);
        Assert.Contains("hard quota limits", message);
        Assert.Contains("Concurrent requests exceeded", message);
    }

    [Fact]
    public void CheckAndGetDegradationMessage_WhenMultipleHardViolations_ReturnsAll()
    {
        var violations = new List<QuotaViolation>
        {
            new(QuotaLimitType.ConcurrentRequestsPerAgent, QuotaScope.Agent, "hercules",
                5, 10, "Concurrent exceeded", "hard_cap"),
            new(QuotaLimitType.CallsPerMinutePerAgent, QuotaScope.Agent, "hercules",
                100, 150, "Rate exceeded", "hard_cap")
        };
        var result = new QuotaCheckResult(violations, true, true);

        var message = _guard.CheckAndGetDegradationMessage(result);
        Assert.NotNull(message);
        Assert.Contains("Concurrent exceeded", message);
        Assert.Contains("Rate exceeded", message);
    }

    [Fact]
    public void LogSoftWarnings_WhenNoSoftWarnings_DoesNotLog()
    {
        var violations = new List<QuotaViolation>
        {
            new(QuotaLimitType.ConcurrentRequestsPerAgent, QuotaScope.Agent, "hercules",
                5, 6, "Concurrent exceeded", "hard_cap")
        };
        var result = new QuotaCheckResult(violations, true, false);

        _guard.LogSoftWarnings(result);

        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception?>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Never);
    }

    [Fact]
    public void LogSoftWarnings_WhenSoftWarnings_LogsWarnings()
    {
        var violations = new List<QuotaViolation>
        {
            new(QuotaLimitType.TokensPerDayPerAgent, QuotaScope.Agent, "hercules",
                1000000, 1100000, "Tokens exceeded", "soft_warn")
        };
        var result = new QuotaCheckResult(violations, false, true);

        _guard.LogSoftWarnings(result);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception?>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [Fact]
    public void CheckBeforeAction_WhenNotExceeded_ReturnsNull()
    {
        var status = new QuotaStatus(
            QuotaLimitType.ConcurrentRequestsPerAgent, QuotaScope.Agent, "hercules",
            5, 2, 3, false, true);

        Assert.Null(_guard.CheckBeforeAction(QuotaScope.Agent, "hercules", status));
    }

    [Fact]
    public void CheckBeforeAction_WhenExceeded_HardCap_ReturnsMessage()
    {
        var status = new QuotaStatus(
            QuotaLimitType.ConcurrentRequestsPerAgent, QuotaScope.Agent, "hercules",
            5, 10, 0, true, true);

        var message = _guard.CheckBeforeAction(QuotaScope.Agent, "hercules", status);
        Assert.NotNull(message);
        Assert.Contains("Hard limit exceeded", message);
    }

    [Fact]
    public void CheckBeforeAction_WhenBelow80Percent_DoesNotLogWarning()
    {
        // Usage at 50% - should not log warning
        var status = new QuotaStatus(
            QuotaLimitType.TokensPerDayPerAgent, QuotaScope.Agent, "hercules",
            1000000, 500000, 500000, false, false);

        var result = _guard.CheckBeforeAction(QuotaScope.Agent, "hercules", status);
        Assert.Null(result);
    }
}
