using Hercules.Quotas;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Эндпоинты rate limits и quotas (task_056).
/// </summary>
public static class QuotasController
{
    public static void MapQuotas(this IEndpointRouteBuilder app)
    {
        // GET /api/quotas — статус всех quotas для scope
        app.MapGet("/api/quotas", (IQuotaService quotaService, string scope, string? scopeId = null) =>
        {
            if (!Enum.TryParse<QuotaScope>(scope, true, out var qScope))
            {
                return Results.BadRequest(new
                {
                    error = $"Unknown scope: {scope}. Valid values: {string.Join(", ", Enum.GetNames<QuotaScope>())}"
                });
            }

            var id = scopeId ?? "default";
            var statuses = quotaService.GetStatus(qScope, id);
            var counters = quotaService.GetCounters(qScope, id);

            return Results.Ok(new
            {
                scope = qScope.ToString(),
                scopeId = id,
                counters = new
                {
                    tokensUsedToday = counters.TokensUsedToday,
                    messagesUsedToday = counters.MessagesUsedToday,
                    requestsUsedToday = counters.RequestsUsedToday,
                    storageUsedMb = counters.StorageUsedMb,
                    costUsedTodayCents = counters.CostUsedTodayCents,
                    activeConcurrentRequests = counters.ActiveConcurrentRequests,
                    activeSkillExecutions = counters.ActiveSkillExecutions,
                    lastResetDate = counters.LastResetDate
                },
                limits = statuses.Select(s => new
                {
                    type = s.Type.ToString(),
                    limit = s.Limit,
                    current = s.Current,
                    remaining = s.Remaining,
                    isExceeded = s.IsExceeded,
                    isHardCap = s.IsHardCap,
                    usagePercent = s.UsagePercent,
                    resetAt = s.ResetAt
                })
            });
        }).WithName("QuotaStatus");

        // GET /api/quotas/{scope}/{scopeId} — статус quotas для конкретного scope
        app.MapGet("/api/quotas/{scope}/{scopeId}", (IQuotaService quotaService, string scope, string scopeId) =>
        {
            if (!Enum.TryParse<QuotaScope>(scope, true, out var qScope))
            {
                return Results.BadRequest(new
                {
                    error = $"Unknown scope: {scope}. Valid values: {string.Join(", ", Enum.GetNames<QuotaScope>())}"
                });
            }

            var statuses = quotaService.GetStatus(qScope, scopeId);
            var counters = quotaService.GetCounters(qScope, scopeId);

            return Results.Ok(new
            {
                scope = qScope.ToString(),
                scopeId = scopeId,
                counters = new
                {
                    tokensUsedToday = counters.TokensUsedToday,
                    messagesUsedToday = counters.MessagesUsedToday,
                    requestsUsedToday = counters.RequestsUsedToday,
                    storageUsedMb = counters.StorageUsedMb,
                    costUsedTodayCents = counters.CostUsedTodayCents,
                    activeConcurrentRequests = counters.ActiveConcurrentRequests,
                    activeSkillExecutions = counters.ActiveSkillExecutions,
                    lastResetDate = counters.LastResetDate
                },
                limits = statuses.Select(s => new
                {
                    type = s.Type.ToString(),
                    limit = s.Limit,
                    current = s.Current,
                    remaining = s.Remaining,
                    isExceeded = s.IsExceeded,
                    isHardCap = s.IsHardCap,
                    usagePercent = s.UsagePercent,
                    resetAt = s.ResetAt
                })
            });
        }).WithName("QuotaStatusByScope");

        // GET /api/quotas/{scope}/{scopeId}/{type} — конкретный quota status
        app.MapGet("/api/quotas/{scope}/{scopeId}/{type}", (IQuotaService quotaService, string scope, string scopeId, string type) =>
        {
            if (!Enum.TryParse<QuotaScope>(scope, true, out var qScope))
            {
                return Results.BadRequest(new
                {
                    error = $"Unknown scope: {scope}. Valid values: {string.Join(", ", Enum.GetNames<QuotaScope>())}"
                });
            }

            if (!Enum.TryParse<QuotaLimitType>(type, true, out var qType))
            {
                return Results.BadRequest(new
                {
                    error = $"Unknown type: {type}. Valid values: {string.Join(", ", Enum.GetNames<QuotaLimitType>())}"
                });
            }

            var status = quotaService.GetStatus(qScope, scopeId, qType);
            if (status is null)
            {
                return Results.NotFound(new
                {
                    error = $"Quota type '{type}' not found for scope {scope}:{scopeId}"
                });
            }

            return Results.Ok(new
            {
                scope = status.Scope.ToString(),
                scopeId = status.ScopeId,
                type = status.Type.ToString(),
                limit = status.Limit,
                current = status.Current,
                remaining = status.Remaining,
                isExceeded = status.IsExceeded,
                isHardCap = status.IsHardCap,
                usagePercent = status.UsagePercent,
                resetAt = status.ResetAt
            });
        }).WithName("QuotaStatusByType");

        // GET /api/quotas/rate-limit — rate limit headers для response
        app.MapGet("/api/quotas/rate-limit", (IQuotaService quotaService, string scope, string? scopeId = null, string? type = null) =>
        {
            if (!Enum.TryParse<QuotaScope>(scope, true, out var qScope))
            {
                return Results.BadRequest(new
                {
                    error = $"Unknown scope: {scope}. Valid values: {string.Join(", ", Enum.GetNames<QuotaScope>())}"
                });
            }

            var id = scopeId ?? "default";
            QuotaLimitType qType = QuotaLimitType.CallsPerMinutePerAgent;

            if (!string.IsNullOrEmpty(type))
            {
                if (!Enum.TryParse<QuotaLimitType>(type, true, out qType))
                {
                    return Results.BadRequest(new
                    {
                        error = $"Unknown type: {type}. Valid values: {string.Join(", ", Enum.GetNames<QuotaLimitType>())}"
                    });
                }
            }

            var info = quotaService.GetRateLimitInfo(qScope, id, qType);
            if (info is null)
            {
                return Results.NotFound(new
                {
                    error = $"Rate limit info not found for {qScope}:{id}:{qType}"
                });
            }

            return Results.Ok(new
            {
                limit = info.LimitHeader,
                remaining = info.RemainingHeader,
                resetAt = info.ResetAtUtc,
                retryAfterSeconds = info.RetryAfterSeconds
            });
        }).WithName("RateLimitInfo");
    }
}
