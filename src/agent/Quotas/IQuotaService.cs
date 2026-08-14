namespace Hercules.Quotas;

/// <summary>
///     Сервис для проверки и учёта rate limits и quotas (task_056).
///     Поддерживает per-agent, per-skill, per-user, per-tenant scopes.
/// </summary>
public interface IQuotaService
{
    /// <summary>
    ///     Проверить все applicable quotas для заданного scope.
    /// </summary>
    QuotaCheckResult CheckQuotas(QuotaScope scope, string scopeId, QuotaCounters? counters = null);

    /// <summary>
    ///     Записать использование (вызов, токены, storage, cost).
    /// </summary>
    void RecordUsage(QuotaScope scope, string scopeId, QuotaLimitType type, long amount);

    /// <summary>
    ///     Начать concurrency tracking (increment active requests).
    /// </summary>
    void BeginConcurrency(QuotaScope scope, string scopeId);

    /// <summary>
    ///     Завершить concurrency tracking (decrement active requests).
    /// </summary>
    void EndConcurrency(QuotaScope scope, string scopeId);

    /// <summary>
    ///     Получить статус всех quotas для scope.
    /// </summary>
    IReadOnlyList<QuotaStatus> GetStatus(QuotaScope scope, string scopeId);

    /// <summary>
    ///     Получить статус конкретного quota type.
    /// </summary>
    QuotaStatus? GetStatus(QuotaScope scope, string scopeId, QuotaLimitType type);

    /// <summary>
    ///     Получить rate limit headers для HTTP response.
    /// </summary>
    RateLimitInfo? GetRateLimitInfo(QuotaScope scope, string scopeId, QuotaLimitType type);

    /// <summary>
    ///     Сбросить счётчики (например, при смене дня).
    /// </summary>
    void ResetDailyCounters(QuotaScope scope, string scopeId);

    /// <summary>
    ///     Получить текущие counters для scope.
    /// </summary>
    QuotaCounters GetCounters(QuotaScope scope, string scopeId);
}
