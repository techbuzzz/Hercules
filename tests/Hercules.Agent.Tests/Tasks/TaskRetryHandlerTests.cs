using Hercules.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Tasks;

public class TaskRetryHandlerTests
{
    private readonly TaskRetryHandler _handler;

    public TaskRetryHandlerTests()
    {
        var loggerMock = new Mock<ILogger<TaskRetryHandler>>();
        _handler = new TaskRetryHandler(loggerMock.Object);
    }

    [Fact]
    public void ShouldRetry_WithinMaxRetries_ReturnsTrue()
    {
        var policy = new TaskRetryPolicy { MaxRetries = 3 };
        Assert.True(_handler.ShouldRetry("network error", policy, 0));
        Assert.True(_handler.ShouldRetry("timeout", policy, 1));
        Assert.True(_handler.ShouldRetry("connection refused", policy, 2));
    }

    [Fact]
    public void ShouldRetry_AtMaxRetries_ReturnsFalse()
    {
        var policy = new TaskRetryPolicy { MaxRetries = 3 };
        Assert.False(_handler.ShouldRetry("error", policy, 3));
    }

    [Fact]
    public void ShouldRetry_BeyondMaxRetries_ReturnsFalse()
    {
        var policy = new TaskRetryPolicy { MaxRetries = 2 };
        Assert.False(_handler.ShouldRetry("error", policy, 5));
    }

    [Fact]
    public void ShouldRetry_NonRetryableError_ReturnsFalse()
    {
        var policy = new TaskRetryPolicy { MaxRetries = 5, NonRetryableErrors = new List<string> { "ValidationError", "Unauthorized" } };

        Assert.False(_handler.ShouldRetry("ValidationError: bad input", policy, 0));
        Assert.False(_handler.ShouldRetry("Unauthorized access denied", policy, 1));
        Assert.False(_handler.ShouldRetry("validationerror: bad input", policy, 0)); // case insensitive
    }

    [Fact]
    public void ShouldRetry_RetryableError_WithinMax_ReturnsTrue()
    {
        var policy = new TaskRetryPolicy { MaxRetries = 5, NonRetryableErrors = new List<string> { "ValidationError" } };
        Assert.True(_handler.ShouldRetry("network error", policy, 0));
        Assert.True(_handler.ShouldRetry("timeout after 30s", policy, 1));
    }

    [Fact]
    public void ShouldRetry_NullError_ReturnsTrue()
    {
        var policy = new TaskRetryPolicy { MaxRetries = 1 };
        Assert.True(_handler.ShouldRetry(null, policy, 0));
    }

    [Fact]
    public void ComputeNextRetryDelay_ExponentialBackoff_Correct()
    {
        var policy = new TaskRetryPolicy { RetryDelayMs = 1000, BackoffMultiplier = 2.0, UseJitter = false };

        Assert.Equal(1000, _handler.ComputeNextRetryDelay(policy, 0));   // 1000 * 2^0
        Assert.Equal(2000, _handler.ComputeNextRetryDelay(policy, 1));   // 1000 * 2^1
        Assert.Equal(4000, _handler.ComputeNextRetryDelay(policy, 2));   // 1000 * 2^2
        Assert.Equal(8000, _handler.ComputeNextRetryDelay(policy, 3));   // 1000 * 2^3
    }

    [Fact]
    public void ComputeNextRetryDelay_WithCustomBase_Correct()
    {
        var policy = new TaskRetryPolicy { RetryDelayMs = 500, BackoffMultiplier = 3.0, UseJitter = false };

        Assert.Equal(500, _handler.ComputeNextRetryDelay(policy, 0));    // 500 * 3^0
        Assert.Equal(1500, _handler.ComputeNextRetryDelay(policy, 1));   // 500 * 3^1
        Assert.Equal(4500, _handler.ComputeNextRetryDelay(policy, 2));    // 500 * 3^2
    }

    [Fact]
    public void ComputeNextRetryDelay_CappedAt30Seconds()
    {
        var policy = new TaskRetryPolicy { RetryDelayMs = 1000, BackoffMultiplier = 2.0, UseJitter = false };

        // 1000 * 2^15 = 32,768,000 ms → capped at 30,000 ms
        var delay = _handler.ComputeNextRetryDelay(policy, 15);
        Assert.Equal(30_000, delay);
    }

    [Fact]
    public void ComputeNextRetryDelay_Jitter_Randomizes()
    {
        var policy = new TaskRetryPolicy { RetryDelayMs = 1000, BackoffMultiplier = 2.0, UseJitter = true };

        // With jitter, results should vary across calls
        var results = new HashSet<int>();
        for (int i = 0; i < 20; i++)
        {
            var delay = _handler.ComputeNextRetryDelay(policy, 2); // base = 4000
            Assert.InRange(delay, 3000, 5000); // ±25% of 4000
            results.Add(delay);
        }

        // At least some variation expected
        Assert.True(results.Count > 1);
    }

    [Fact]
    public void ComputeNextRetryDelay_NoJitter_Deterministic()
    {
        var policy = new TaskRetryPolicy { RetryDelayMs = 1000, BackoffMultiplier = 2.0, UseJitter = false };

        var delay1 = _handler.ComputeNextRetryDelay(policy, 2);
        var delay2 = _handler.ComputeNextRetryDelay(policy, 2);
        Assert.Equal(delay1, delay2); // identical without jitter
    }

    [Theory]
    [InlineData("ValidationError: bad input", false)]
    [InlineData("Unauthorized", false)]
    [InlineData("Forbidden", false)]
    [InlineData("network error", true)]
    [InlineData("connection refused", true)]
    [InlineData("timeout", true)]
    [InlineData("internal server error", true)]
    [InlineData("service unavailable", true)]
    public void IsRetryableError_ClassifiesCorrectly(string error, bool expected)
    {
        Assert.Equal(expected, TaskRetryHandler.IsRetryableError(error));
    }

    [Fact]
    public void IsRetryableError_NullOrEmpty_ReturnsTrue()
    {
        Assert.True(TaskRetryHandler.IsRetryableError(null));
        Assert.True(TaskRetryHandler.IsRetryableError(""));
    }
}
