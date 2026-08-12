using System.Collections.Concurrent;
using Hercules.Config;
using Hercules.Storage;

namespace Hercules.Budget;

/// <summary>
///     Реализация IGuardrailService:
///     - in-memory per-session request counters (ConcurrentDictionary)
///     - per-day counters из SQLite budget_entries
///     - hard-cap vs soft-warn enforcement
/// </summary>
public sealed class GuardrailService : IGuardrailService
{
    private readonly BudgetConfig _cfg;
    private readonly IBudgetService _budget;

    // per-session request counters (сбрасываются после каждого HandleAsync)
    private readonly ConcurrentDictionary<string, RequestCounters> _requestCounters = new();

    // per-session/day accumulated (tokens + calls) from current in-memory session
    // Key: "sessionId:yyyy-MM-dd"
    private readonly ConcurrentDictionary<string, DailySessionAccumulator> _sessionDaily = new();

    private long _todayTotalTokens;
    private int _todayTotalCalls;
    private long _todayTotalCostCents; // cents × 100 (decimal not atomic)
    private string _todayKey = "";

    public GuardrailService(BudgetConfig cfg, IBudgetService budget)
    {
        _cfg = cfg;
        _budget = budget;
        ResetDailyCounters();
    }

    private void ResetDailyCounters()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (_todayKey != today)
        {
            _todayKey = today;
            _todayTotalTokens = 0;
            _todayTotalCalls = 0;
            _todayTotalCostCents = 0;
        }
    }

    public GuardrailCheckResult CheckLimits(string sessionId, int estimatedTokens = 0)
    {
        if (!_cfg.Enabled)
        {
            return new GuardrailCheckResult(Array.Empty<GuardrailViolation>(), false);
        }

        ResetDailyCounters();
        var counters = GetOrCreateCounters(sessionId);
        var enforcement = _cfg.EnforcementMode;
        var violations = new List<GuardrailViolation>();

        // Per-request checks
        violations.AddRange(CheckTokensPerRequest(counters.TokensUsed + estimatedTokens, enforcement));
        violations.AddRange(CheckToolCallsPerRequest(counters.ToolCalls, enforcement));
        violations.AddRange(CheckRetriesPerTool(counters.RetriesForCurrentTool, enforcement));
        violations.AddRange(CheckWallClockPerRequest(counters.ElapsedMilliseconds, enforcement));

        // Per-day checks
        violations.AddRange(CheckTokensPerDay(_todayTotalTokens + estimatedTokens, enforcement));
        violations.AddRange(CheckCallsPerDay(_todayTotalCalls, enforcement));
        violations.AddRange(CheckCostPerDay(_todayTotalCostCents, enforcement));

        var hasHard = violations.Any(v => v.EnforcementMode == "hard_cap" && v.Actual > v.Limit);
        return new GuardrailCheckResult(violations, hasHard);
    }

    private IEnumerable<GuardrailViolation> CheckTokensPerRequest(long current, string enforcement)
    {
        var limit = _cfg.MaxTokensPerRequest;
        if (limit <= 0) yield break;
        if (current > limit)
            yield return new GuardrailViolation(
                GuardrailLimitType.TokensPerRequest, limit, current,
                $"Tokens per request exceeded: {current}/{limit}",
                IsHardCap(GuardrailLimitType.TokensPerRequest) ? "hard_cap" : enforcement);
    }

    private IEnumerable<GuardrailViolation> CheckToolCallsPerRequest(long current, string enforcement)
    {
        var limit = _cfg.MaxToolCallsPerRequest;
        if (limit <= 0) yield break;
        if (current > limit)
            yield return new GuardrailViolation(
                GuardrailLimitType.ToolCallsPerRequest, limit, current,
                $"Tool calls per request exceeded: {current}/{limit}",
                IsHardCap(GuardrailLimitType.ToolCallsPerRequest) ? "hard_cap" : enforcement);
    }

    private IEnumerable<GuardrailViolation> CheckRetriesPerTool(long current, string enforcement)
    {
        var limit = _cfg.MaxRetriesPerTool;
        if (limit <= 0) yield break;
        if (current > limit)
            yield return new GuardrailViolation(
                GuardrailLimitType.RetriesPerTool, limit, current,
                $"Tool retries per request exceeded: {current}/{limit}",
                IsHardCap(GuardrailLimitType.RetriesPerTool) ? "hard_cap" : enforcement);
    }

    private IEnumerable<GuardrailViolation> CheckWallClockPerRequest(long elapsedMs, string enforcement)
    {
        var limitMs = _cfg.MaxWallClockSecondsPerRequest * 1000;
        if (limitMs <= 0) yield break;
        if (elapsedMs > limitMs)
            yield return new GuardrailViolation(
                GuardrailLimitType.WallClockSecondsPerRequest, limitMs, elapsedMs,
                $"Wall-clock time per request exceeded: {elapsedMs}ms/{limitMs}ms",
                IsHardCap(GuardrailLimitType.WallClockSecondsPerRequest) ? "hard_cap" : enforcement);
    }

    private IEnumerable<GuardrailViolation> CheckTokensPerDay(long current, string enforcement)
    {
        var limit = _cfg.MaxTokensPerDay;
        if (limit <= 0) yield break;
        if (current > limit)
            yield return new GuardrailViolation(
                GuardrailLimitType.TokensPerDay, limit, current,
                $"Tokens per day exceeded: {current}/{limit}",
                IsHardCap(GuardrailLimitType.TokensPerDay) ? "hard_cap" : "soft_warn");
    }

    private IEnumerable<GuardrailViolation> CheckCallsPerDay(long current, string enforcement)
    {
        var limit = _cfg.MaxCallsPerDay;
        if (limit <= 0) yield break;
        if (current > limit)
            yield return new GuardrailViolation(
                GuardrailLimitType.CallsPerDay, limit, current,
                $"Calls per day exceeded: {current}/{limit}",
                IsHardCap(GuardrailLimitType.CallsPerDay) ? "hard_cap" : "soft_warn");
    }

    private IEnumerable<GuardrailViolation> CheckCostPerDay(long currentCents, string enforcement)
    {
        var limitUsd = _cfg.MaxCostPerDayUsd;
        if (limitUsd <= 0) yield break;
        var limitCents = (long)(limitUsd * 100);
        if (currentCents > limitCents)
            yield return new GuardrailViolation(
                GuardrailLimitType.CostPerDay, limitCents, currentCents,
                $"Cost per day exceeded: ${currentCents / 100.0:F4}/${limitUsd}",
                IsHardCap(GuardrailLimitType.CostPerDay) ? "hard_cap" : "soft_warn");
    }

    /// <summary>
    ///     Определяет enforcement mode для конкретного типа лимита.
    ///     Per-request лимиты по умолчанию — hard_cap; per-day — soft_warn.
    /// </summary>
    private bool IsHardCap(GuardrailLimitType type) => type switch
    {
        GuardrailLimitType.TokensPerRequest => true,
        GuardrailLimitType.ToolCallsPerRequest => true,
        GuardrailLimitType.RetriesPerTool => true,
        GuardrailLimitType.WallClockSecondsPerRequest => true,
        _ => false,
    };

    public void RecordLlmUsage(string sessionId, int inputTokens, int outputTokens, decimal costUsd, string provider)
    {
        var totalTokens = inputTokens + outputTokens;
        GetOrCreateCounters(sessionId).TokensUsed += totalTokens;

        // Update daily in-memory accumulator (cost tracked in cents × 100 for atomicity)
        ResetDailyCounters();
        Interlocked.Add(ref _todayTotalTokens, totalTokens);
        Interlocked.Increment(ref _todayTotalCalls);
        Interlocked.Add(ref _todayTotalCostCents, (long)(costUsd * 100));
        _ = Task.Run(async () =>
        {
            try
            {
                await _budget.LogAsync(sessionId, provider, "", inputTokens, outputTokens, costUsd);
            }
            catch
            {
                // Best-effort logging
            }
        });
    }

    public void RecordToolCall(string sessionId)
    {
        GetOrCreateCounters(sessionId).ToolCalls++;
    }

    public void RecordToolRetry(string sessionId)
    {
        GetOrCreateCounters(sessionId).RetriesForCurrentTool++;
    }

    public void RecordElapsedTime(string sessionId, long elapsedMs)
    {
        GetOrCreateCounters(sessionId).ElapsedMilliseconds = elapsedMs;
    }

    public IReadOnlyList<GuardrailStatus> GetStatus(string sessionId)
    {
        ResetDailyCounters();
        var counters = GetOrCreateCounters(sessionId);
        var list = new List<GuardrailStatus>();

        list.Add(MakeStatus(GuardrailLimitType.TokensPerRequest, counters.TokensUsed, _cfg.MaxTokensPerRequest));
        list.Add(MakeStatus(GuardrailLimitType.ToolCallsPerRequest, counters.ToolCalls, _cfg.MaxToolCallsPerRequest));
        list.Add(MakeStatus(GuardrailLimitType.RetriesPerTool, counters.RetriesForCurrentTool, _cfg.MaxRetriesPerTool));
        list.Add(MakeStatus(GuardrailLimitType.WallClockSecondsPerRequest, counters.ElapsedMilliseconds, _cfg.MaxWallClockSecondsPerRequest * 1000L));
        list.Add(MakeStatus(GuardrailLimitType.TokensPerDay, _todayTotalTokens, _cfg.MaxTokensPerDay));
        list.Add(MakeStatus(GuardrailLimitType.CallsPerDay, _todayTotalCalls, _cfg.MaxCallsPerDay));
        list.Add(MakeStatusCost(GuardrailLimitType.CostPerDay, _todayTotalCostCents, _cfg.MaxCostPerDayUsd));

        return list;
    }

    private GuardrailStatus MakeStatus(GuardrailLimitType type, long current, long limit)
    {
        if (limit <= 0) return new GuardrailStatus(type, 0, current, 0, false, IsHardCap(type));
        var remaining = limit - current;
        return new GuardrailStatus(type, limit, current, remaining > 0 ? remaining : 0, current > limit, IsHardCap(type));
    }

    private GuardrailStatus MakeStatusCost(GuardrailLimitType type, long currentCents, decimal limitUsd)
    {
        var limitCents = (long)(limitUsd * 100);
        if (limitCents <= 0) return new GuardrailStatus(type, 0, currentCents, 0, false, IsHardCap(type));
        var remaining = limitCents - currentCents;
        return new GuardrailStatus(type, limitCents, currentCents, remaining > 0 ? remaining : 0, currentCents > limitCents, IsHardCap(type));
    }

    public void ResetRequestCounters(string sessionId)
    {
        _requestCounters.AddOrUpdate(sessionId,
            _ => new RequestCounters(),
            (_, existing) => new RequestCounters());
    }

    public RequestCounters GetRequestCounters(string sessionId) => GetOrCreateCounters(sessionId);

    private RequestCounters GetOrCreateCounters(string sessionId) =>
        _requestCounters.GetOrAdd(sessionId, _ => new RequestCounters());

    /// <summary>Accumulator for session/day tokens.</summary>
    private sealed class DailySessionAccumulator
    {
        public long Tokens { get; set; }
        public int Calls { get; set; }
        public decimal CostUsd { get; set; }
    }
}
