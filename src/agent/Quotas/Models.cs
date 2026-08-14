namespace Hercules.Quotas;

/// <summary>
///     Типы quota limits (task_056).
/// </summary>
public enum QuotaLimitType
{
    /// <summary>Concurrent requests per agent.</summary>
    ConcurrentRequestsPerAgent,

    /// <summary>Calls per minute per agent.</summary>
    CallsPerMinutePerAgent,

    /// <summary>Tokens per day per agent.</summary>
    TokensPerDayPerAgent,

    /// <summary>Storage in MB per agent.</summary>
    StorageMbPerAgent,

    /// <summary>Messages per day per agent.</summary>
    MessagesPerDayPerAgent,

    /// <summary>Calls per minute per skill.</summary>
    CallsPerMinutePerSkill,

    /// <summary>Concurrent executions per skill.</summary>
    ConcurrentPerSkill,

    /// <summary>Requests per minute per user.</summary>
    RequestsPerMinutePerUser,

    /// <summary>Requests per day per user.</summary>
    RequestsPerDayPerUser,

    /// <summary>Total agents per tenant.</summary>
    AgentsPerTenant,

    /// <summary>Total calls per minute per tenant.</summary>
    CallsPerMinutePerTenant,

    /// <summary>Total cost per day per tenant (USD cents).</summary>
    CostPerDayPerTenant
}

/// <summary>
///     Scope (granularity) для quota tracking.
/// </summary>
public enum QuotaScope
{
    /// <summary>Quota applies to a specific agent.</summary>
    Agent,

    /// <summary>Quota applies to a specific skill.</summary>
    Skill,

    /// <summary>Quota applies to a specific user.</summary>
    User,

    /// <summary>Quota applies to a specific tenant.</summary>
    Tenant
}

/// <summary>
///     Текущий статус одного quota limit.
/// </summary>
public sealed record QuotaStatus(
    QuotaLimitType Type,
    QuotaScope Scope,
    string ScopeId,
    long Limit,
    long Current,
    long Remaining,
    bool IsExceeded,
    bool IsHardCap,
    DateTime? ResetAt = null)
{
    /// <summary>Процент использования [0..100].</summary>
    public double UsagePercent => Limit > 0 ? Math.Min(100, (Current * 100.0) / Limit) : 0;
}

/// <summary>
///     Одно нарушение quota.
/// </summary>
public sealed record QuotaViolation(
    QuotaLimitType Type,
    QuotaScope Scope,
    string ScopeId,
    long Limit,
    long Actual,
    string Message,
    string EnforcementMode);

/// <summary>
///     Результат проверки всех quota limits.
/// </summary>
public sealed record QuotaCheckResult(
    IReadOnlyList<QuotaViolation> Violations,
    bool HasHardViolation,
    bool HasSoftWarning)
{
    public static QuotaCheckResult Empty => new(Array.Empty<QuotaViolation>(), false, false);
}

/// <summary>
///     Rate limit info for HTTP headers.
/// </summary>
public sealed record RateLimitInfo(
    string Limit,
    long Remaining,
    DateTime ResetAtUtc)
{
    /// <summary>X-RateLimit-Limit header value.</summary>
    public string LimitHeader => Limit;

    /// <summary>X-RateLimit-Remaining header value.</summary>
    public string RemainingHeader => Remaining.ToString();

    /// <summary>Retry-After header value in seconds.</summary>
    public int RetryAfterSeconds => Math.Max(0, (int)(ResetAtUtc - DateTime.UtcNow).TotalSeconds);
}

/// <summary>
///     Counters для отслеживания использования quota.
/// </summary>
public sealed class QuotaCounters
{
    public long TokensUsedToday;
    public long MessagesUsedToday;
    public long StorageUsedMb;
    public long RequestsUsedToday;
    public long CostUsedTodayCents;
    public DateTime LastResetDate = DateTime.UtcNow.Date;
    public DateTime LastRequestTime;
    public int ActiveConcurrentRequests;
    public int ActiveSkillExecutions;
}
