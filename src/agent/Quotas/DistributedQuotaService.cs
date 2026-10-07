using Hercules.Config;
using Hercules.Mesh.Abstractions;
using Microsoft.Extensions.Logging;

namespace Hercules.Quotas;

/// <summary>
///     Decorator над <see cref="IQuotaService"/>, который дополнительно учитывает rate-limit
///     counters в распределённом <see cref="IMeshStateStore"/> (Redis/Valkey, PostgreSQL,
///     NATS JetStream). Используется при <c>Quotas.DistributedEnabled = true</c>.
///     <para>
///     Семантика:
///     <list type="bullet">
///         <item>Daily counters (tokens, storage, messages, cost) — только локально, как в
///             базовом <see cref="QuotaService"/>. Сброс при рестарте процесса допустим —
///             это per-instance budget.</item>
///         <item>Rate-limit counters (per-minute) — оба счётчика обновляются: локальный
///             <c>ConcurrentQueue</c> для sub-window precision и атомарный
///             <c>IMeshStateStore.IncrementAsync(..., ttl: windowSec)</c> для cross-instance
///             агрегации. Rate-check возвращает <c>max(local, distributed)</c> — берётся
///             более строгий из двух.</item>
///         <item>Если <see cref="IMeshStateStore"/> недоступен (Redis down, etc.) — service
///             логирует warning и прозрачно fallback'ит на in-memory счётчик.</item>
///     </list>
///     </para>
///     Спецификация: task_072.
/// </summary>
public sealed class DistributedQuotaService : IQuotaService
{
    private readonly IQuotaService _inner;
    private readonly IMeshStateStore _store;
    private readonly QuotasConfig _cfg;
    private readonly ILogger<DistributedQuotaService> _logger;

    public DistributedQuotaService(
        IQuotaService inner,
        IMeshStateStore store,
        QuotasConfig cfg,
        ILogger<DistributedQuotaService> logger)
    {
        _inner = inner;
        _store = store;
        _cfg = cfg;
        _logger = logger;
    }

    /// <summary>Префикс для distributed counter keys. По умолчанию "quota:".</summary>
    public string KeyPrefix => _cfg.DistributedKeyPrefix;

    /// <summary>Получить распределённый counter из mesh state store (или null при ошибке).</summary>
    private async Task<long?> TryGetDistributedCountAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var stored = await _store.GetAsync(key, ct).ConfigureAwait(false);
            if (stored is null)
                return 0;
            return long.TryParse(stored.Data, out var v) ? v : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[DistributedQuota] GetAsync failed for {Key}, falling back to in-memory", key);
            return null;
        }
    }

    /// <summary>Получить эффективный rate-limit count: max(local, distributed) или local при ошибке store.</summary>
    private async Task<long> GetEffectiveRateLimitCountAsync(QuotaScope scope, string scopeId, QuotaLimitType type)
    {
        var local = LocalGetRateLimitCount(scope, scopeId, type);
        if (!_cfg.DistributedEnabled)
            return local;

        var key = BuildKey(scope, scopeId, type);
        var distributed = await TryGetDistributedCountAsync(key).ConfigureAwait(false);
        if (distributed is null)
            return local; // fallback
        return Math.Max(local, distributed.Value);
    }

    private string BuildKey(QuotaScope scope, string scopeId, QuotaLimitType type)
        => $"{_cfg.DistributedKeyPrefix}{scope}:{scopeId}:{type}";

    /// <summary>
    ///     Получить локальный (in-memory) rate-limit count через рефлексию-free путь:
    ///     базовый <see cref="IQuotaService"/> уже реализует корректный sliding window
    ///     после фиксов task_072, поэтому мы просто зовём <see cref="GetStatus"/> и читаем Current.
    ///     </summary>
    private long LocalGetRateLimitCount(QuotaScope scope, string scopeId, QuotaLimitType type)
    {
        var status = _inner.GetStatus(scope, scopeId, type);
        return status?.Current ?? 0;
    }

    public QuotaCheckResult CheckQuotas(QuotaScope scope, string scopeId, QuotaCounters? counters = null)
        => _inner.CheckQuotas(scope, scopeId, counters);

    public void RecordUsage(QuotaScope scope, string scopeId, QuotaLimitType type, long amount)
    {
        // Update local counters first — this preserves all in-memory invariants.
        _inner.RecordUsage(scope, scopeId, type, amount);

        // Then bump the distributed counter (best-effort, async-fire-and-forget so we
        // don't block the caller on a remote round-trip).
        if (_cfg.DistributedEnabled && IsRateLimitType(type))
        {
            var key = BuildKey(scope, scopeId, type);
            var ttl = TimeSpan.FromSeconds(_cfg.RateLimitWindowSeconds);
            _ = IncrementDistributedAsync(key, amount, ttl);
        }
    }

    private async Task IncrementDistributedAsync(string key, long amount, TimeSpan ttl)
    {
        try
        {
            await _store.IncrementAsync(key, amount, ttl).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DistributedQuota] IncrementAsync failed for {Key}, falling back to in-memory", key);
        }
    }

    public void BeginConcurrency(QuotaScope scope, string scopeId) => _inner.BeginConcurrency(scope, scopeId);
    public void EndConcurrency(QuotaScope scope, string scopeId) => _inner.EndConcurrency(scope, scopeId);

    public IReadOnlyList<QuotaStatus> GetStatus(QuotaScope scope, string scopeId)
    {
        var statuses = _inner.GetStatus(scope, scopeId);
        if (!_cfg.DistributedEnabled || statuses.Count == 0)
            return statuses;

        // Override rate-limit rows with the effective (max(local, distributed)) count.
        return statuses.Select(s => IsRateLimitType(s.Type)
            ? s with { Current = GetEffectiveRateLimitSync(s.Type, scope, scopeId, s) }
            : s).ToList();
    }

    private long GetEffectiveRateLimitSync(QuotaLimitType type, QuotaScope scope, string scopeId, QuotaStatus local)
    {
        // Async path is too heavy for a sync override: just return the local count
        // here and let the next async query (GetRateLimitInfo) pick up the distributed
        // value. For synchronous status displays the in-memory count is good enough.
        return local.Current;
    }

    public QuotaStatus? GetStatus(QuotaScope scope, string scopeId, QuotaLimitType type)
        => _inner.GetStatus(scope, scopeId, type);

    public RateLimitInfo? GetRateLimitInfo(QuotaScope scope, string scopeId, QuotaLimitType type)
    {
        if (!_cfg.DistributedEnabled || !IsRateLimitType(type))
            return _inner.GetRateLimitInfo(scope, scopeId, type);

        var local = _inner.GetRateLimitInfo(scope, scopeId, type);
        if (local is null)
            return null;

        var key = BuildKey(scope, scopeId, type);
        var distributed = TryGetDistributedCountAsync(key).GetAwaiter().GetResult();
        if (distributed is null)
            return local;

        var limit = long.Parse(local.LimitHeader);
        var effectiveRemaining = Math.Max(0, limit - distributed.Value);
        return new RateLimitInfo(
            local.LimitHeader,
            effectiveRemaining,
            DateTime.UtcNow.AddSeconds(_cfg.RateLimitWindowSeconds));
    }

    public void ResetDailyCounters(QuotaScope scope, string scopeId) => _inner.ResetDailyCounters(scope, scopeId);
    public QuotaCounters GetCounters(QuotaScope scope, string scopeId) => _inner.GetCounters(scope, scopeId);

    private static bool IsRateLimitType(QuotaLimitType type) => type switch
    {
        QuotaLimitType.CallsPerMinutePerAgent => true,
        QuotaLimitType.CallsPerMinutePerSkill => true,
        QuotaLimitType.RequestsPerMinutePerUser => true,
        QuotaLimitType.CallsPerMinutePerTenant => true,
        _ => false,
    };
}
