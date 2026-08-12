namespace Hercules.Storage;

/// <summary>
///     Интерфейс сервиса учёта бюджета (LLM-токены, стоимость).
/// </summary>
public interface IBudgetService
{
    /// <summary>Записать budget entry для одного LLM-вызова.</summary>
    Task LogAsync(string sessionId, string provider, string model, int inputTokens, int outputTokens, decimal costUsd, CancellationToken ct = default);

    /// <summary>Сводка по бюджету за период.</summary>
    Task<BudgetSummary> GetSummaryAsync(DateTime? since = null, CancellationToken ct = default);

    /// <summary>Дневная разбивка бюджета.</summary>
    Task<List<(string Date, int Calls, decimal CostUsd)>> GetDailyAsync(int days = 30, CancellationToken ct = default);

    /// <summary>Проверить, не превышен ли месячный бюджет.</summary>
    Task<bool> IsOverMonthlyBudgetAsync(decimal monthlyLimit, CancellationToken ct = default);
}

/// <summary>
///     Реализация BudgetService на основе SqliteSessionStore.
/// </summary>
public sealed class BudgetService(SqliteSessionStore store) : IBudgetService
{
    public async Task LogAsync(string sessionId, string provider, string model, int inputTokens, int outputTokens, decimal costUsd, CancellationToken ct = default)
    {
        await store.LogBudgetEntryAsync(sessionId, provider, model, inputTokens, outputTokens, costUsd, ct);
    }

    public Task<BudgetSummary> GetSummaryAsync(DateTime? since = null, CancellationToken ct = default)
    {
        return store.GetBudgetSummaryAsync(since, ct);
    }

    public Task<List<(string Date, int Calls, decimal CostUsd)>> GetDailyAsync(int days = 30, CancellationToken ct = default)
    {
        return store.GetDailyBudgetAsync(days, ct);
    }

    public async Task<bool> IsOverMonthlyBudgetAsync(decimal monthlyLimit, CancellationToken ct = default)
    {
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var summary = await store.GetBudgetSummaryAsync(monthStart, ct);
        return summary.TotalCostUsd >= monthlyLimit;
    }
}
