using System.Collections.Concurrent;
using Hercules.Config;
using Microsoft.Extensions.Logging;

namespace Hercules.Quotas;

/// <summary>
///     Реализация IQuotaService:
///     - in-memory concurrent tracking
///     - per-scope sliding window для rate limits
///     - daily counters с авто-reset
/// </summary>
public sealed class QuotaService : IQuotaService
{
    private readonly QuotasConfig _cfg;
    private readonly ILogger<QuotaService> _logger;

    // Per-scope counters: key = "scope:scopeId"
    private readonly ConcurrentDictionary<string, QuotaCounters> _counters = new();

    // Sliding window buckets для rate limiting: key = "scope:scopeId:type"
    // ConcurrentQueue chosen over ConcurrentBag: O(1) Enqueue/TryDequeue, FIFO order so
    // head is always the oldest entry and pruning is trivial. ConcurrentBag.Count is O(n)
    // and order is undefined, which is why the original implementation was broken.
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> _rateBuckets = new();

    public QuotaService(QuotasConfig cfg, ILogger<QuotaService> logger)
    {
        _cfg = cfg;
        _logger = logger;
    }

    public QuotaCheckResult CheckQuotas(QuotaScope scope, string scopeId, QuotaCounters? counters = null)
    {
        if (!_cfg.Enabled)
            return QuotaCheckResult.Empty;

        var c = counters ?? GetCounters(scope, scopeId);
        ResetDailyIfNeeded(scope, scopeId, c);
        var violations = new List<QuotaViolation>();
        var hasHard = false;
        var hasSoft = false;

        // Check limits based on scope
        switch (scope)
        {
            case QuotaScope.Agent:
                violations.AddRange(CheckAgentLimits(scopeId, c));
                break;
            case QuotaScope.Skill:
                violations.AddRange(CheckSkillLimits(scopeId, c));
                break;
            case QuotaScope.User:
                violations.AddRange(CheckUserLimits(scopeId, c));
                break;
            case QuotaScope.Tenant:
                violations.AddRange(CheckTenantLimits(scopeId, c));
                break;
        }

        foreach (var v in violations)
        {
            if (v.EnforcementMode == "hard_cap") hasHard = true;
            else hasSoft = true;
        }

        return new QuotaCheckResult(violations, hasHard, hasSoft);
    }

    private IEnumerable<QuotaViolation> CheckAgentLimits(string agentId, QuotaCounters c)
    {
        // Concurrent requests
        var concurrentLimit = _cfg.MaxConcurrentRequestsPerAgent;
        if (concurrentLimit > 0 && c.ActiveConcurrentRequests > concurrentLimit)
            yield return new QuotaViolation(
                QuotaLimitType.ConcurrentRequestsPerAgent, QuotaScope.Agent, agentId,
                concurrentLimit, c.ActiveConcurrentRequests,
                $"Concurrent requests exceeded: {c.ActiveConcurrentRequests}/{concurrentLimit}",
                IsHardCap(QuotaLimitType.ConcurrentRequestsPerAgent) ? "hard_cap" : _cfg.EnforcementMode);

        // Rate limit (calls per minute)
        var rateLimit = GetRateLimitCount(QuotaScope.Agent, agentId, QuotaLimitType.CallsPerMinutePerAgent);
        var rateMax = _cfg.MaxCallsPerMinutePerAgent;
        if (rateMax > 0 && rateLimit > rateMax)
            yield return new QuotaViolation(
                QuotaLimitType.CallsPerMinutePerAgent, QuotaScope.Agent, agentId,
                rateMax, rateLimit,
                $"Calls per minute exceeded: {rateLimit}/{rateMax}",
                IsHardCap(QuotaLimitType.CallsPerMinutePerAgent) ? "hard_cap" : _cfg.EnforcementMode);

        // Tokens per day
        var tokensMax = _cfg.MaxTokensPerDayPerAgent;
        if (tokensMax > 0 && c.TokensUsedToday > tokensMax)
            yield return new QuotaViolation(
                QuotaLimitType.TokensPerDayPerAgent, QuotaScope.Agent, agentId,
                tokensMax, c.TokensUsedToday,
                $"Tokens per day exceeded: {c.TokensUsedToday}/{tokensMax}",
                IsHardCap(QuotaLimitType.TokensPerDayPerAgent) ? "hard_cap" : "soft_warn");

        // Storage
        var storageMax = _cfg.MaxStorageMbPerAgent;
        if (storageMax > 0 && c.StorageUsedMb > storageMax)
            yield return new QuotaViolation(
                QuotaLimitType.StorageMbPerAgent, QuotaScope.Agent, agentId,
                storageMax, c.StorageUsedMb,
                $"Storage exceeded: {c.StorageUsedMb}MB/{storageMax}MB",
                IsHardCap(QuotaLimitType.StorageMbPerAgent) ? "hard_cap" : "soft_warn");

        // Messages per day
        var msgsMax = _cfg.MaxMessagesPerDayPerAgent;
        if (msgsMax > 0 && c.MessagesUsedToday > msgsMax)
            yield return new QuotaViolation(
                QuotaLimitType.MessagesPerDayPerAgent, QuotaScope.Agent, agentId,
                msgsMax, c.MessagesUsedToday,
                $"Messages per day exceeded: {c.MessagesUsedToday}/{msgsMax}",
                IsHardCap(QuotaLimitType.MessagesPerDayPerAgent) ? "hard_cap" : "soft_warn");
    }

    private IEnumerable<QuotaViolation> CheckSkillLimits(string skillId, QuotaCounters c)
    {
        // Rate limit
        var rateLimit = GetRateLimitCount(QuotaScope.Skill, skillId, QuotaLimitType.CallsPerMinutePerSkill);
        var rateMax = _cfg.MaxCallsPerMinutePerSkill;
        if (rateMax > 0 && rateLimit > rateMax)
            yield return new QuotaViolation(
                QuotaLimitType.CallsPerMinutePerSkill, QuotaScope.Skill, skillId,
                rateMax, rateLimit,
                $"Skill calls per minute exceeded: {rateLimit}/{rateMax}",
                IsHardCap(QuotaLimitType.CallsPerMinutePerSkill) ? "hard_cap" : _cfg.EnforcementMode);

        // Concurrent executions
        var concurrentLimit = _cfg.MaxConcurrentPerSkill;
        if (concurrentLimit > 0 && c.ActiveSkillExecutions > concurrentLimit)
            yield return new QuotaViolation(
                QuotaLimitType.ConcurrentPerSkill, QuotaScope.Skill, skillId,
                concurrentLimit, c.ActiveSkillExecutions,
                $"Concurrent skill executions exceeded: {c.ActiveSkillExecutions}/{concurrentLimit}",
                IsHardCap(QuotaLimitType.ConcurrentPerSkill) ? "hard_cap" : _cfg.EnforcementMode);
    }

    private IEnumerable<QuotaViolation> CheckUserLimits(string userId, QuotaCounters c)
    {
        // Rate limit
        var rateLimit = GetRateLimitCount(QuotaScope.User, userId, QuotaLimitType.RequestsPerMinutePerUser);
        var rateMax = _cfg.MaxRequestsPerMinutePerUser;
        if (rateMax > 0 && rateLimit > rateMax)
            yield return new QuotaViolation(
                QuotaLimitType.RequestsPerMinutePerUser, QuotaScope.User, userId,
                rateMax, rateLimit,
                $"Requests per minute exceeded: {rateLimit}/{rateMax}",
                IsHardCap(QuotaLimitType.RequestsPerMinutePerUser) ? "hard_cap" : _cfg.EnforcementMode);

        // Daily requests
        var dailyMax = _cfg.MaxRequestsPerDayPerUser;
        if (dailyMax > 0 && c.RequestsUsedToday > dailyMax)
            yield return new QuotaViolation(
                QuotaLimitType.RequestsPerDayPerUser, QuotaScope.User, userId,
                dailyMax, c.RequestsUsedToday,
                $"Daily requests exceeded: {c.RequestsUsedToday}/{dailyMax}",
                IsHardCap(QuotaLimitType.RequestsPerDayPerUser) ? "hard_cap" : "soft_warn");
    }

    private IEnumerable<QuotaViolation> CheckTenantLimits(string tenantId, QuotaCounters c)
    {
        // Calls per minute
        var rateLimit = GetRateLimitCount(QuotaScope.Tenant, tenantId, QuotaLimitType.CallsPerMinutePerTenant);
        var rateMax = _cfg.MaxCallsPerMinutePerTenant;
        if (rateMax > 0 && rateLimit > rateMax)
            yield return new QuotaViolation(
                QuotaLimitType.CallsPerMinutePerTenant, QuotaScope.Tenant, tenantId,
                rateMax, rateLimit,
                $"Tenant calls per minute exceeded: {rateLimit}/{rateMax}",
                IsHardCap(QuotaLimitType.CallsPerMinutePerTenant) ? "hard_cap" : _cfg.EnforcementMode);

        // Cost per day
        var costMaxCents = (long)(_cfg.MaxCostPerDayPerTenantUsd * 100);
        if (costMaxCents > 0 && c.CostUsedTodayCents > costMaxCents)
            yield return new QuotaViolation(
                QuotaLimitType.CostPerDayPerTenant, QuotaScope.Tenant, tenantId,
                costMaxCents, c.CostUsedTodayCents,
                $"Tenant cost per day exceeded: ${c.CostUsedTodayCents / 100.0:F2}/$ {_cfg.MaxCostPerDayPerTenantUsd}",
                IsHardCap(QuotaLimitType.CostPerDayPerTenant) ? "hard_cap" : "soft_warn");
    }

    private long GetRateLimitCount(QuotaScope scope, string scopeId, QuotaLimitType type)
    {
        var key = $"{scope}:{scopeId}:{type}";
        if (!_rateBuckets.TryGetValue(key, out var bucket))
            return 0;

        PruneOldEntries(bucket, DateTime.UtcNow.AddSeconds(-_cfg.RateLimitWindowSeconds));
        return bucket.Count;
    }

    /// <summary>
    ///     In-place prune: pops entries from the head (oldest first) while they fall outside
    ///     the sliding window. O(k) where k is the number of expired entries, and the queue
    ///     itself remains the same ConcurrentQueue instance (no swap, no leaked references).
    /// </summary>
    private static void PruneOldEntries(ConcurrentQueue<DateTime> bucket, DateTime cutoff)
    {
        while (bucket.TryPeek(out var head) && head < cutoff)
        {
            // TryDequeue may fail under contention, but the head is still < cutoff,
            // so another caller (or the background sweeper) will catch it next pass.
            bucket.TryDequeue(out _);
        }
    }

    /// <summary>
    ///     Sweep all rate-limit buckets and prune expired entries. Invoked periodically by
    ///     <see cref="QuotaCleanupBackgroundService"/> so the buckets do not grow without bound
    ///     between <see cref="GetRateLimitCount"/> calls.
    /// </summary>
    public int SweepAllBuckets()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-_cfg.RateLimitWindowSeconds);
        var swept = 0;
        foreach (var key in _rateBuckets.Keys)
        {
            if (_rateBuckets.TryGetValue(key, out var bucket))
            {
                var before = bucket.Count;
                PruneOldEntries(bucket, cutoff);
                swept += before - bucket.Count;
            }
        }
        return swept;
    }

    public void RecordUsage(QuotaScope scope, string scopeId, QuotaLimitType type, long amount)
    {
        var c = GetCounters(scope, scopeId);
        ResetDailyIfNeeded(scope, scopeId, c);

        switch (type)
        {
            case QuotaLimitType.TokensPerDayPerAgent:
                Interlocked.Add(ref c.TokensUsedToday, amount);
                break;
            case QuotaLimitType.MessagesPerDayPerAgent:
            case QuotaLimitType.RequestsPerDayPerUser:
                Interlocked.Add(ref c.RequestsUsedToday, amount);
                Interlocked.Add(ref c.MessagesUsedToday, amount);
                break;
            case QuotaLimitType.StorageMbPerAgent:
                Interlocked.Add(ref c.StorageUsedMb, amount);
                break;
            case QuotaLimitType.CostPerDayPerTenant:
                Interlocked.Add(ref c.CostUsedTodayCents, amount);
                break;
        }

        c.LastRequestTime = DateTime.UtcNow;

        // Add to rate limit bucket
        var bucketKey = $"{scope}:{scopeId}:{type}";
        _rateBuckets.GetOrAdd(bucketKey, _ => new ConcurrentQueue<DateTime>()).Enqueue(DateTime.UtcNow);
    }

    public void BeginConcurrency(QuotaScope scope, string scopeId)
    {
        var c = GetCounters(scope, scopeId);
        Interlocked.Increment(ref c.ActiveConcurrentRequests);
        if (scope == QuotaScope.Skill)
            Interlocked.Increment(ref c.ActiveSkillExecutions);
    }

    public void EndConcurrency(QuotaScope scope, string scopeId)
    {
        var c = GetCounters(scope, scopeId);
        Interlocked.Decrement(ref c.ActiveConcurrentRequests);
        if (scope == QuotaScope.Skill)
            Interlocked.Decrement(ref c.ActiveSkillExecutions);
    }

    public IReadOnlyList<QuotaStatus> GetStatus(QuotaScope scope, string scopeId)
    {
        var c = GetCounters(scope, scopeId);
        ResetDailyIfNeeded(scope, scopeId, c);
        var statuses = new List<QuotaStatus>();

        switch (scope)
        {
            case QuotaScope.Agent:
                statuses.Add(MakeStatus(QuotaLimitType.ConcurrentRequestsPerAgent, scope, scopeId, c.ActiveConcurrentRequests, _cfg.MaxConcurrentRequestsPerAgent));
                statuses.Add(MakeStatus(QuotaLimitType.CallsPerMinutePerAgent, scope, scopeId, GetRateLimitCount(scope, scopeId, QuotaLimitType.CallsPerMinutePerAgent), _cfg.MaxCallsPerMinutePerAgent));
                statuses.Add(MakeStatus(QuotaLimitType.TokensPerDayPerAgent, scope, scopeId, c.TokensUsedToday, _cfg.MaxTokensPerDayPerAgent));
                statuses.Add(MakeStatus(QuotaLimitType.StorageMbPerAgent, scope, scopeId, c.StorageUsedMb, _cfg.MaxStorageMbPerAgent));
                statuses.Add(MakeStatus(QuotaLimitType.MessagesPerDayPerAgent, scope, scopeId, c.MessagesUsedToday, _cfg.MaxMessagesPerDayPerAgent));
                break;
            case QuotaScope.Skill:
                statuses.Add(MakeStatus(QuotaLimitType.CallsPerMinutePerSkill, scope, scopeId, GetRateLimitCount(scope, scopeId, QuotaLimitType.CallsPerMinutePerSkill), _cfg.MaxCallsPerMinutePerSkill));
                statuses.Add(MakeStatus(QuotaLimitType.ConcurrentPerSkill, scope, scopeId, c.ActiveSkillExecutions, _cfg.MaxConcurrentPerSkill));
                break;
            case QuotaScope.User:
                statuses.Add(MakeStatus(QuotaLimitType.RequestsPerMinutePerUser, scope, scopeId, GetRateLimitCount(scope, scopeId, QuotaLimitType.RequestsPerMinutePerUser), _cfg.MaxRequestsPerMinutePerUser));
                statuses.Add(MakeStatus(QuotaLimitType.RequestsPerDayPerUser, scope, scopeId, c.RequestsUsedToday, _cfg.MaxRequestsPerDayPerUser));
                break;
            case QuotaScope.Tenant:
                statuses.Add(MakeStatus(QuotaLimitType.CallsPerMinutePerTenant, scope, scopeId, GetRateLimitCount(scope, scopeId, QuotaLimitType.CallsPerMinutePerTenant), _cfg.MaxCallsPerMinutePerTenant));
                statuses.Add(MakeStatus(QuotaLimitType.CostPerDayPerTenant, scope, scopeId, c.CostUsedTodayCents, (long)(_cfg.MaxCostPerDayPerTenantUsd * 100)));
                break;
        }

        return statuses;
    }

    public QuotaStatus? GetStatus(QuotaScope scope, string scopeId, QuotaLimitType type)
    {
        return GetStatus(scope, scopeId).FirstOrDefault(s => s.Type == type);
    }

    public RateLimitInfo? GetRateLimitInfo(QuotaScope scope, string scopeId, QuotaLimitType type)
    {
        var status = GetStatus(scope, scopeId, type);
        if (status is null || status.Limit <= 0)
            return null;

        var windowEnd = DateTime.UtcNow.AddSeconds(_cfg.RateLimitWindowSeconds);
        return new RateLimitInfo(
            status.Limit.ToString(),
            Math.Max(0, status.Remaining),
            windowEnd);
    }

    public void ResetDailyCounters(QuotaScope scope, string scopeId)
    {
        var key = Key(scope, scopeId);
        _counters.AddOrUpdate(key,
            _ => new QuotaCounters(),
            (_, c) =>
            {
                c.TokensUsedToday = 0;
                c.MessagesUsedToday = 0;
                c.RequestsUsedToday = 0;
                c.CostUsedTodayCents = 0;
                c.LastResetDate = DateTime.UtcNow.Date;
                return c;
            });
    }

    public QuotaCounters GetCounters(QuotaScope scope, string scopeId)
    {
        var key = Key(scope, scopeId);
        return _counters.GetOrAdd(key, _ => new QuotaCounters());
    }

    private void ResetDailyIfNeeded(QuotaScope scope, string scopeId, QuotaCounters c)
    {
        var today = DateTime.UtcNow.Date;
        if (c.LastResetDate < today)
        {
            c.TokensUsedToday = 0;
            c.MessagesUsedToday = 0;
            c.RequestsUsedToday = 0;
            c.CostUsedTodayCents = 0;
            c.LastResetDate = today;
        }
    }

    private QuotaStatus MakeStatus(QuotaLimitType type, QuotaScope scope, string scopeId, long current, long limit)
    {
        if (limit <= 0)
            return new QuotaStatus(type, scope, scopeId, 0, current, 0, false, IsHardCap(type));

        var remaining = limit - current;
        return new QuotaStatus(
            type, scope, scopeId,
            limit, current,
            remaining > 0 ? remaining : 0,
            current > limit,
            IsHardCap(type));
    }

    private bool IsHardCap(QuotaLimitType type) => type switch
    {
        QuotaLimitType.ConcurrentRequestsPerAgent => true,
        QuotaLimitType.ConcurrentPerSkill => true,
        QuotaLimitType.CallsPerMinutePerAgent => true,
        QuotaLimitType.CallsPerMinutePerSkill => true,
        QuotaLimitType.RequestsPerMinutePerUser => true,
        QuotaLimitType.CallsPerMinutePerTenant => true,
        _ => false,
    };

    private static string Key(QuotaScope scope, string scopeId) => $"{scope}:{scopeId}";
}
