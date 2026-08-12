using Hercules.Budget;
using Hercules.Storage;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Эндпоинты бюджета: учёт LLM-токенов и стоимости.
/// </summary>
public static class BudgetController
{
    public static void MapBudget(this IEndpointRouteBuilder app)
    {
        // GET /api/budget — сводка по бюджету
        app.MapGet("/api/budget", async (IBudgetService budget, int days = 30, CancellationToken ct = default) =>
        {
            var summary = await budget.GetSummaryAsync(DateTime.UtcNow.AddDays(-days), ct);
            var daily = await budget.GetDailyAsync(days, ct);
            return Results.Ok(new
            {
                periodDays = days,
                summary = new
                {
                    totalCalls = summary.TotalCalls,
                    totalInputTokens = summary.TotalInputTokens,
                    totalOutputTokens = summary.TotalOutputTokens,
                    totalCostUsd = summary.TotalCostUsd
                },
                daily = daily.Select(d => new { date = d.Date, calls = d.Calls, costUsd = d.CostUsd })
            });
        }).WithName("BudgetSummary");

        // GET /api/budget/monthly — месячная сводка
        app.MapGet("/api/budget/monthly", async (IBudgetService budget, decimal? limit, CancellationToken ct = default) =>
        {
            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var summary = await budget.GetSummaryAsync(monthStart, ct);
            var isOver = limit.HasValue && await budget.IsOverMonthlyBudgetAsync(limit.Value, ct);
            return Results.Ok(new
            {
                month = $"{DateTime.UtcNow.Year}-{DateTime.UtcNow.Month:D2}",
                totalCalls = summary.TotalCalls,
                totalInputTokens = summary.TotalInputTokens,
                totalOutputTokens = summary.TotalOutputTokens,
                totalCostUsd = summary.TotalCostUsd,
                limitUsd = limit,
                isOverBudget = isOver
            });
        }).WithName("BudgetMonthly");

        // GET /api/budget/guardrails — статус всех guardrail-лимитов
        app.MapGet("/api/budget/guardrails", (IGuardrailService guardrails, string? sessionId) =>
        {
            var sid = sessionId ?? "default";
            var statuses = guardrails.GetStatus(sid);
            var counters = guardrails.GetRequestCounters(sid);
            return Results.Ok(new
            {
                sessionId = sid,
                requestCounters = new
                {
                    toolCalls = counters.ToolCalls,
                    retriesForCurrentTool = counters.RetriesForCurrentTool,
                    elapsedMs = counters.ElapsedMilliseconds,
                    tokensUsed = counters.TokensUsed
                },
                limits = statuses.Select(s => new
                {
                    type = s.Type.ToString(),
                    limit = s.Limit,
                    current = s.Current,
                    remaining = s.Remaining,
                    isExceeded = s.IsExceeded,
                    isHardCap = s.IsHardCap
                })
            });
        }).WithName("BudgetGuardrails");

        // GET /api/budget/guardrails/{type} — статус конкретного лимита
        app.MapGet("/api/budget/guardrails/{type}", (IGuardrailService guardrails, string type, string? sessionId) =>
        {
            if (!Enum.TryParse<GuardrailLimitType>(type, true, out var limitType))
            {
                return Results.BadRequest(new { error = $"Unknown limit type: {type}. Valid values: {string.Join(", ", Enum.GetNames<GuardrailLimitType>())}" });
            }
            var sid = sessionId ?? "default";
            var statuses = guardrails.GetStatus(sid);
            var status = statuses.FirstOrDefault(s => s.Type == limitType);
            if (status is null || (status.Type == default && status.Limit == 0))
            {
                return Results.NotFound(new { error = $"Limit type '{type}' not found" });
            }
            return Results.Ok(new
            {
                sessionId = sid,
                type = status.Type.ToString(),
                limit = status.Limit,
                current = status.Current,
                remaining = status.Remaining,
                isExceeded = status.IsExceeded,
                isHardCap = status.IsHardCap
            });
        }).WithName("BudgetGuardrailType");
    }
}
