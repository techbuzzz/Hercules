using System;
using Microsoft.Extensions.Logging;

namespace Hercules.Tasks;

/// <summary>
///     Handles retry logic for durable tasks: exponential backoff, jitter, non-retryable error filtering.
/// </summary>
public sealed class TaskRetryHandler
{
    private readonly ILogger<TaskRetryHandler> _logger;
    private static readonly Random _jitter = new();

    public TaskRetryHandler(ILogger<TaskRetryHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    ///     Determines whether a failed task should be retried based on policy and error type.
    /// </summary>
    /// <param name="error">Error message or type.</param>
    /// <param name="policy">Retry policy.</param>
    /// <param name="currentAttempt">Current attempt count (0-based).</param>
    public bool ShouldRetry(string? error, TaskRetryPolicy policy, int currentAttempt)
    {
        if (currentAttempt >= policy.MaxRetries)
        {
            _logger.LogDebug("[TaskRetry] Max retries ({Max}) reached, not retrying", policy.MaxRetries);
            return false;
        }

        if (string.IsNullOrEmpty(error))
            return true;

        // Check non-retryable errors (case-insensitive)
        var errorLower = error.ToLowerInvariant();
        foreach (var nonRetryable in policy.NonRetryableErrors)
        {
            if (errorLower.Contains(nonRetryable.ToLowerInvariant()))
            {
                _logger.LogDebug("[TaskRetry] Error '{Error}' matches non-retryable pattern '{Pattern}'", error, nonRetryable);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Compute the delay before the next retry attempt using exponential backoff with optional jitter.
    /// </summary>
    /// <param name="policy">Retry policy.</param>
    /// <param name="attemptNumber">The attempt number being scheduled (0-based).</param>
    public int ComputeNextRetryDelay(TaskRetryPolicy policy, int attemptNumber)
    {
        // Exponential backoff: delay = base * (multiplier ^ attempt)
        var baseDelay = policy.RetryDelayMs;
        var multiplier = policy.BackoffMultiplier;

        double delay = baseDelay * Math.Pow(multiplier, attemptNumber);

        if (policy.UseJitter)
        {
            // Add ±25% jitter
            var jitterRange = delay * 0.25;
            delay += (_jitter.NextDouble() * 2 - 1) * jitterRange;
        }

        // Cap at 30 seconds
        return (int)Math.Min(delay, 30_000);
    }

    /// <summary>
    ///     Determines if an error is retryable (for task retry decision).
    ///     Retryable: network errors, timeout, transient failures.
    ///     Non-retryable: validation errors, auth failures, etc.
    /// </summary>
    public static bool IsRetryableError(string? error)
    {
        if (string.IsNullOrEmpty(error)) return true;

        var lower = error.ToLowerInvariant();
        var nonRetryable = new[]
        {
            "validationerror", "invalid", "unauthorized", "forbidden",
            "notfound", "conflict", "gone", "badrequest",
            "syntaxerror", "parseerror"
        };

        foreach (var pattern in nonRetryable)
        {
            if (lower.Contains(pattern))
                return false;
        }

        return true;
    }
}
