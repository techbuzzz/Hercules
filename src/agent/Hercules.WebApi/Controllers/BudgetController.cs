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
    }
}
