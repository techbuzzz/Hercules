using Hercules.Config;
using Hercules.Storage;
using Xunit;

namespace Hercules.Agent.Tests.Storage;

/// <summary>
///     Тесты BudgetService: LogAsync, GetSummaryAsync, GetDailyAsync, IsOverMonthlyBudgetAsync.
/// </summary>
public class BudgetServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteSessionStore _store;
    private readonly BudgetService _svc;

    public BudgetServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-budget-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var cfg = new StorageConfig { DataRoot = _tempDir };
        _store = new SqliteSessionStore(cfg);
        _svc = new BudgetService(_store);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task LogAsync_AddsEntry_AndGetSummary_ReturnsIt()
    {
        await _svc.LogAsync("s1", "yandexgpt", "yandexgpt-lite", 100, 50, 0.001m);

        var summary = await _svc.GetSummaryAsync();

        Assert.Equal(1, summary.TotalCalls);
        Assert.Equal(100, summary.TotalInputTokens);
        Assert.Equal(50, summary.TotalOutputTokens);
        Assert.Equal(0.001m, summary.TotalCostUsd);
    }

    [Fact]
    public async Task GetSummaryAsync_AccumulatesMultipleEntries()
    {
        await _svc.LogAsync("s1", "yandexgpt", "gpt", 100, 50, 0.001m);
        await _svc.LogAsync("s2", "ollama-cloud", "llama3.1", 200, 100, 0m);

        var summary = await _svc.GetSummaryAsync();

        Assert.Equal(2, summary.TotalCalls);
        Assert.Equal(300, summary.TotalInputTokens);
        Assert.Equal(150, summary.TotalOutputTokens);
        Assert.Equal(0.001m, summary.TotalCostUsd);
    }

    [Fact]
    public async Task GetSummaryAsync_FiltersBySince()
    {
        var old = DateTime.UtcNow.AddDays(-7);
        await _store.LogBudgetEntryAsync("s1", "yandexgpt", "gpt", 100, 50, 0.001m);

        var summary = await _svc.GetSummaryAsync(since: old.AddDays(-1));

        Assert.Equal(1, summary.TotalCalls);
    }

    [Fact]
    public async Task GetDailyAsync_ReturnsDailyBreakdown()
    {
        await _svc.LogAsync("s1", "yandexgpt", "gpt", 100, 50, 0.001m);
        await _svc.LogAsync("s2", "yandexgpt", "gpt", 200, 100, 0.002m);

        var daily = await _svc.GetDailyAsync(30);

        Assert.NotEmpty(daily);
        Assert.Equal(2, daily.Sum(d => d.Calls));
    }

    [Fact]
    public async Task IsOverMonthlyBudgetAsync_UnderLimit_ReturnsFalse()
    {
        await _svc.LogAsync("s1", "yandexgpt", "gpt", 100, 50, 0.001m);

        var result = await _svc.IsOverMonthlyBudgetAsync(1.0m);

        Assert.False(result);
    }

    [Fact]
    public async Task IsOverMonthlyBudgetAsync_AtLimit_ReturnsTrue()
    {
        await _svc.LogAsync("s1", "yandexgpt", "gpt", 100, 50, 1.0m);

        var result = await _svc.IsOverMonthlyBudgetAsync(1.0m);

        Assert.True(result);
    }

    [Fact]
    public async Task GetSummaryAsync_EmptyStore_ReturnsZeroSummary()
    {
        var summary = await _svc.GetSummaryAsync();

        Assert.Equal(0, summary.TotalCalls);
        Assert.Equal(0, summary.TotalInputTokens);
        Assert.Equal(0, summary.TotalOutputTokens);
        Assert.Equal(0m, summary.TotalCostUsd);
    }
}
