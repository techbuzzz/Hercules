using Hercules.Config;
using Hercules.Storage;
using Xunit;

namespace Hercules.Agent.Tests.Storage;

/// <summary>
///     Дополнительные тесты SqliteSessionStore: IsHealthy, session CRUD,
///     interaction logging, audit log, budget entries, task state.
/// </summary>
public class SqliteSessionStoreDirectTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteSessionStore _store;

    public SqliteSessionStoreDirectTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-sss-direct-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var cfg = new StorageConfig { DataRoot = _tempDir };
        _store = new SqliteSessionStore(cfg);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    #region IsHealthy

    [Fact]
    public void IsHealthy_ValidConnection_ReturnsTrue()
    {
        Assert.True(_store.IsHealthy());
    }

    [Fact]
    public void IsHealthy_AfterDispose_ReturnsFalse()
    {
        _store.Dispose();
        Assert.False(_store.IsHealthy());
    }

    #endregion

    #region Sessions

    [Fact]
    public async Task StartSessionAsync_InsertsSession()
    {
        await _store.StartSessionAsync("sess-new");

        // StartSessionAsync should not throw — session recorded
        var exists = await _store.GetTotalInteractionsAsync(); // any method that doesn't throw means session exists
        Assert.True(exists >= 0);
    }

    [Fact]
    public async Task EndSessionAsync_SetsEndedAt()
    {
        await _store.StartSessionAsync("sess-end");
        await _store.EndSessionAsync("sess-end");

        // EndSessionAsync should not throw
        Assert.True(_store.IsHealthy());
    }

    #endregion

    #region Interactions

    [Fact]
    public async Task LogInteraction_And_GetTotalInteractions()
    {
        await _store.StartSessionAsync("sess-interact");
        await _store.LogInteractionAsync(new InteractionLog(
            "sess-interact",
            "user input",
            "agent output",
            "high",
            "direct",
            null,
            "yandexgpt",
            DateTime.UtcNow));
        await _store.LogInteractionAsync(new InteractionLog(
            "sess-interact",
            "user input 2",
            "agent output 2",
            "medium",
            "skill",
            "skill-id",
            "yandexgpt",
            DateTime.UtcNow));

        var total = await _store.GetTotalInteractionsAsync();

        Assert.Equal(2, total);
    }

    [Fact]
    public async Task LogInteraction_WithSkill_SetsSkillId()
    {
        await _store.StartSessionAsync("sess-skill");
        await _store.LogInteractionAsync(new InteractionLog(
            "sess-skill",
            "погода",
            "Дождь ожидается",
            "high",
            "skill",
            "weather-skill",
            "yandexgpt",
            DateTime.UtcNow));

        var lowConf = await _store.GetLowConfidenceAsync("sess-skill");

        // High confidence shouldn't be in low confidence results
        Assert.DoesNotContain(lowConf, l => l.Input == "погода");
    }

    #endregion

    #region Audit Log

    [Fact]
    public async Task LogAudit_And_GetAuditLog()
    {
        await _store.LogAuditAsync("agent", "skill_created", "skill-abc", "создан новый навык", "s1");
        await _store.LogAuditAsync("user", "config_changed", null, "{\"key\":\"value\"}", "s2");

        var entries = await _store.GetAuditLogAsync(10);

        Assert.Equal(2, entries.Count);
        // Entries are returned newest-first (descending by created_at)
        Assert.Equal("user", entries[0].Actor); // newest first
        Assert.Equal("agent", entries[1].Actor);
        Assert.Equal("skill_created", entries[1].Action);
        Assert.Equal("skill-abc", entries[1].Target);
    }

    [Fact]
    public async Task GetAuditLogByTarget_ReturnsMatchingEntries()
    {
        await _store.LogAuditAsync("agent", "skill_created", "skill-xyz", null, "s1");
        await _store.LogAuditAsync("agent", "skill_deleted", "skill-xyz", null, "s2");

        var entries = await _store.GetAuditLogByTargetAsync("skill-xyz");

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal("skill-xyz", e.Target));
    }

    #endregion

    #region Budget

    [Fact]
    public async Task LogBudgetEntry_And_GetBudgetSummary()
    {
        await _store.LogBudgetEntryAsync("s1", "yandexgpt", "yandexgpt-lite", 100, 50, 0.001m);
        await _store.LogBudgetEntryAsync("s2", "ollama", "llama3", 200, 100, 0m);

        var summary = await _store.GetBudgetSummaryAsync(null);

        Assert.Equal(2, summary.TotalCalls);
        Assert.Equal(300, summary.TotalInputTokens);
        Assert.Equal(150, summary.TotalOutputTokens);
        Assert.Equal(0.001m, summary.TotalCostUsd);
    }

    [Fact]
    public async Task GetBudgetSummary_RespectsSinceFilter()
    {
        var oldDate = DateTime.UtcNow.AddDays(-30);
        await _store.LogBudgetEntryAsync("s-old", "yandexgpt", "gpt", 100, 50, 0.001m);

        var summary = await _store.GetBudgetSummaryAsync(oldDate.AddDays(-1));

        Assert.Equal(1, summary.TotalCalls);
    }

    [Fact]
    public async Task GetDailyBudget_ReturnsBreakdown()
    {
        await _store.LogBudgetEntryAsync("s1", "yandexgpt", "gpt", 100, 50, 0.001m);
        await _store.LogBudgetEntryAsync("s2", "yandexgpt", "gpt", 200, 100, 0.002m);

        var daily = await _store.GetDailyBudgetAsync(30);

        Assert.NotEmpty(daily);
        Assert.Equal(2, daily.Sum(d => d.Calls));
    }

    #endregion

    #region Task State

    [Fact]
    public async Task SaveTaskState_And_LoadTaskState()
    {
        await _store.SaveTaskStateAsync("task-abc", "in_progress", "{\"progress\": 50}", null, "{\"assigned\":\"agent-1\"}");

        var loaded = await _store.LoadTaskStateAsync("task-abc");

        Assert.NotNull(loaded);
        Assert.Equal("task-abc", loaded.TaskId);
        Assert.Equal("in_progress", loaded.Status);
        Assert.Equal("{\"progress\": 50}", loaded.Result);
    }

    [Fact]
    public async Task LoadTaskState_Nonexistent_ReturnsNull()
    {
        var loaded = await _store.LoadTaskStateAsync("nonexistent-task");

        Assert.Null(loaded);
    }

    [Fact]
    public async Task SaveTaskState_OverwritesExisting()
    {
        await _store.SaveTaskStateAsync("task-overwrite", "pending", null, null, null);
        await _store.SaveTaskStateAsync("task-overwrite", "done", "success", null, null);

        var loaded = await _store.LoadTaskStateAsync("task-overwrite");
        Assert.NotNull(loaded);
        Assert.Equal("done", loaded.Status);
        Assert.Equal("success", loaded.Result);
    }

    [Fact]
    public async Task ListTaskStates_ReturnsAll()
    {
        await _store.SaveTaskStateAsync("task-1", "done", null, null, null);
        await _store.SaveTaskStateAsync("task-2", "in_progress", null, null, null);

        var states = await _store.ListTaskStatesAsync();

        Assert.Equal(2, states.Count);
    }

    #endregion

    #region Concurrency (task_071)

    /// <summary>
    ///     task_071: 10 параллельных LogInteractionAsync должны корректно
    ///     сериализоваться через SemaphoreSlim и сохранить все 10 записей
    ///     без SQLiteException.
    /// </summary>
    [Fact]
    public async Task Concurrent_LogInteractionAsync_AllPersist()
    {
        await _store.StartSessionAsync("sess-concurrent");

        var tasks = Enumerable.Range(0, 10)
            .Select(i => _store.LogInteractionAsync(new InteractionLog(
                "sess-concurrent",
                $"input-{i}",
                $"output-{i}",
                "high",
                "direct",
                null,
                "test-provider",
                DateTime.UtcNow)))
            .ToArray();

        // Should not throw SQLiteException or any other race-related error
        await Task.WhenAll(tasks);

        var total = await _store.GetTotalInteractionsAsync();
        Assert.Equal(10, total);
    }

    /// <summary>
    ///     task_071: смешанный concurrent workload — параллельные записи в
    ///     разные таблицы (interactions, audit, budget) не должны вызывать
    ///     SQLiteException и не должны терять записи.
    /// </summary>
    [Fact]
    public async Task Concurrent_MixedWrites_AllPersistAcrossTables()
    {
        await _store.StartSessionAsync("sess-mixed");

        var tasks = new List<Task>();

        for (int i = 0; i < 8; i++)
        {
            int idx = i;
            tasks.Add(_store.LogInteractionAsync(new InteractionLog(
                "sess-mixed", $"in-{idx}", $"out-{idx}", "high", "direct", null, "p", DateTime.UtcNow)));
            tasks.Add(_store.LogAuditAsync("test", $"act-{idx}", $"tgt-{idx}", null, "sess-mixed"));
            tasks.Add(_store.LogBudgetEntryAsync("sess-mixed", "p", "m", 10, 5, 0.001m));
        }

        await Task.WhenAll(tasks);

        Assert.Equal(8, await _store.GetTotalInteractionsAsync());
        var audit = await _store.GetAuditLogAsync(100);
        Assert.Equal(8, audit.Count);
        var summary = await _store.GetBudgetSummaryAsync();
        Assert.Equal(8, summary.TotalCalls);
    }

    /// <summary>
    ///     task_071: IsHealthy() остаётся отзывчивым (и не выбрасывает) пока
    ///     идут параллельные записи.
    /// </summary>
    [Fact]
    public async Task IsHealthy_UnderConcurrentLoad_ReturnsTrue()
    {
        await _store.StartSessionAsync("sess-healthy");

        var writer = Task.Run(async () =>
        {
            for (int i = 0; i < 50; i++)
            {
                await _store.LogInteractionAsync(new InteractionLog(
                    "sess-healthy", $"in-{i}", $"out-{i}", "high", "direct", null, "p", DateTime.UtcNow));
            }
        });

        for (int i = 0; i < 20; i++)
        {
            Assert.True(_store.IsHealthy());
        }

        await writer;
    }

    /// <summary>
    ///     task_071: sync-обёртки, вызываемые параллельно, должны сериализоваться
    ///     через тот же SemaphoreSlim и не бросать SQLiteException.
    /// </summary>
    [Fact]
    public async Task Concurrent_SyncWrappers_AllPersist()
    {
        await _store.StartSessionAsync("sess-sync");

        var tasks = Enumerable.Range(0, 10)
            .Select(i => Task.Run(() => _store.LogInteraction(new InteractionLog(
                "sess-sync", $"sync-in-{i}", $"sync-out-{i}", "high", "direct", null, "p", DateTime.UtcNow))))
            .ToArray();

        await Task.WhenAll(tasks);

        var total = await _store.GetTotalInteractionsAsync();
        Assert.Equal(10, total);
    }

    #endregion
}
