using Hercules.Observability;
using Microsoft.Extensions.Logging;

namespace Hercules.Quotas;

/// <summary>
///     Enforcement helper для quota violations.
///     Возвращает degradation message для soft-warn и блокирует для hard-cap.
/// </summary>
public sealed class QuotaGuard
{
    private readonly ILogger<QuotaGuard> _logger;

    // [task_085] Sampled warning counters to avoid log flooding on sustained
    // quota pressure. Logged 1-in-N (default 10).
    private long _softWarnLogCount;
    private long _approachLogCount;
    private int _logSampleRate = 10;

    public QuotaGuard(ILogger<QuotaGuard> logger)
    {
        _logger = logger;
    }

    /// <summary>
    ///     [task_085] Update sampled-log rate for soft-warn warnings.
    /// </summary>
    public void SetLogSampleRate(int sampleRate) => _logSampleRate = sampleRate;

    /// <summary>
    ///     Проверить результат quota check и вернуть degradation message если есть hard violations.
    ///     Null = можно продолжать.
    /// </summary>
    public string? CheckAndGetDegradationMessage(QuotaCheckResult result)
    {
        if (result.HasHardViolation)
        {
            var hardViolations = result.Violations
                .Where(v => v.EnforcementMode == "hard_cap")
                .ToList();

            if (hardViolations.Count > 0)
            {
                var msg = string.Join("; ", hardViolations.Select(v => v.Message));
                return $"[Quota Guard] Request blocked due to hard quota limits: {msg}";
            }
        }

        return null;
    }

    /// <summary>
    ///     Логировать soft warnings из quota check.
    ///     [task_085] Sampled 1-in-N to avoid log flooding.
    /// </summary>
    public void LogSoftWarnings(QuotaCheckResult result)
    {
        if (!result.HasSoftWarning)
            return;

        var softViolations = result.Violations
            .Where(v => v.EnforcementMode != "hard_cap")
            .ToList();

        if (softViolations.Count == 0)
            return;

        if (OtelMetrics.ShouldLogSampledWarning(ref _softWarnLogCount, _logSampleRate))
        {
            foreach (var v in softViolations)
            {
                _logger.LogWarning(
                    "[QuotaGuard] Soft warning: {Message} (scope={Scope}:{ScopeId})",
                    v.Message, v.Scope, v.ScopeId);
            }
        }
    }

    /// <summary>
    ///     Проверить конкретный quota перед выполнением действия.
    ///     [task_085] Sampled warning for &gt;80% usage.
    /// </summary>
    public string? CheckBeforeAction(QuotaScope scope, string scopeId, QuotaStatus status)
    {
        if (status.IsExceeded && status.IsHardCap)
        {
            return $"[Quota Guard] Hard limit exceeded for {status.Type} on {scope}:{scopeId}: " +
                   $"{status.Current}/{status.Limit}";
        }

        // Warn if > 80% used (sampled)
        if (status.UsagePercent > 80 && !status.IsHardCap)
        {
            if (OtelMetrics.ShouldLogSampledWarning(ref _approachLogCount, _logSampleRate))
            {
                _logger.LogWarning(
                    "[QuotaGuard] Approaching limit: {Type} on {Scope}:{ScopeId} at {Percent:F1}%",
                    status.Type, scope, scopeId, status.UsagePercent);
            }
        }

        return null;
    }
}
