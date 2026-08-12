using Hercules.Config;
using Hercules.Storage;
using Xunit;

namespace Hercules.Agent.Tests.Storage;

/// <summary>
///     Тесты SqliteSessionStore: новые методы task_003
///     (task_states, skill_evaluations).
/// </summary>
public class SqliteSessionStoreHybridTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteSessionStore _store;

    public SqliteSessionStoreHybridTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-hybrid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new SqliteSessionStore(new StorageConfig { DataRoot = _tempDir });
    }

    public void Dispose()
    {
        _store.Dispose();
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    // ---- Task state ----

    [Fact]
    public async Task SaveTaskStateAsync_ThenLoad_ReturnsState()
    {
        await _store.SaveTaskStateAsync("task-1", "in_progress", "half-done", null, "{\"priority\":1}");

        var state = await _store.LoadTaskStateAsync("task-1");

        Assert.NotNull(state);
        Assert.Equal("task-1", state.TaskId);
        Assert.Equal("in_progress", state.Status);
        Assert.Equal("half-done", state.Result);
        Assert.Equal("{\"priority\":1}", state.Metadata);
    }

    [Fact]
    public async Task SaveTaskStateAsync_Update_OverwritesExisting()
    {
        await _store.SaveTaskStateAsync("task-1", "in_progress", "v1", null, null);
        await _store.SaveTaskStateAsync("task-1", "done", "v2", null, null);

        var state = await _store.LoadTaskStateAsync("task-1");

        Assert.NotNull(state);
        Assert.Equal("done", state.Status);
        Assert.Equal("v2", state.Result);
    }

    [Fact]
    public async Task SaveTaskStateAsync_WithError_StoredCorrectly()
    {
        await _store.SaveTaskStateAsync("task-2", "failed", null, "timeout", null);

        var state = await _store.LoadTaskStateAsync("task-2");

        Assert.NotNull(state);
        Assert.Equal("failed", state.Status);
        Assert.Equal("timeout", state.Error);
    }

    [Fact]
    public async Task LoadTaskStateAsync_UnknownId_ReturnsNull()
    {
        var state = await _store.LoadTaskStateAsync("nonexistent");

        Assert.Null(state);
    }

    [Fact]
    public async Task ListTaskStatesAsync_ReturnsAll()
    {
        await _store.SaveTaskStateAsync("task-1", "done", null, null, null);
        await _store.SaveTaskStateAsync("task-2", "in_progress", null, null, null);
        await _store.SaveTaskStateAsync("task-3", "pending", null, null, null);

        var all = await _store.ListTaskStatesAsync();

        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task ListTaskStatesAsync_WithStatusFilter_ReturnsFiltered()
    {
        await _store.SaveTaskStateAsync("task-1", "done", null, null, null);
        await _store.SaveTaskStateAsync("task-2", "in_progress", null, null, null);
        await _store.SaveTaskStateAsync("task-3", "done", null, null, null);

        var done = await _store.ListTaskStatesAsync("done");

        Assert.Equal(2, done.Count);
        Assert.All(done, s => Assert.Equal("done", s.Status));
    }

    // ---- Skill evaluation history ----

    [Fact]
    public async Task SaveEvaluationResultAsync_ThenGetHistory_ReturnsRecord()
    {
        await _store.SaveEvaluationResultAsync("skill-abc", 0.85, true, "[{\"test\":\"ok\"}]", "yandexgpt");

        var history = await _store.GetSkillEvaluationHistoryAsync("skill-abc");

        Assert.Single(history);
        Assert.Equal("skill-abc", history[0].SkillId);
        Assert.Equal(0.85, history[0].Score);
        Assert.True(history[0].Passed);
        Assert.Contains("ok", history[0].TestResults!);
    }

    [Fact]
    public async Task SaveEvaluationResultAsync_Failed_StoredWithPassedFalse()
    {
        await _store.SaveEvaluationResultAsync("skill-bad", 0.3, false, null, "ollama-cloud");

        var history = await _store.GetSkillEvaluationHistoryAsync("skill-bad");

        Assert.Single(history);
        Assert.False(history[0].Passed);
        Assert.Equal(0.3, history[0].Score);
    }

    [Fact]
    public async Task GetSkillEvaluationHistoryAsync_MultipleEntries_ReturnsNewestFirst()
    {
        await _store.SaveEvaluationResultAsync("skill-multi", 0.5, false, null, "p1");
        await _store.SaveEvaluationResultAsync("skill-multi", 0.8, true, null, "p2");
        await _store.SaveEvaluationResultAsync("skill-multi", 0.9, true, null, "p3");

        var history = await _store.GetSkillEvaluationHistoryAsync("skill-multi", 10);

        Assert.Equal(3, history.Count);
        Assert.Equal(0.9, history[0].Score);  // newest
        Assert.Equal(0.5, history[2].Score);  // oldest
    }

    [Fact]
    public async Task GetSkillEvaluationHistoryAsync_UnknownSkill_ReturnsEmpty()
    {
        var history = await _store.GetSkillEvaluationHistoryAsync("nonexistent");

        Assert.Empty(history);
    }

    [Fact]
    public async Task GetSkillEvaluationHistoryAsync_Limit_Respected()
    {
        for (int i = 0; i < 10; i++)
        {
            await _store.SaveEvaluationResultAsync("skill-many", 0.5 + i * 0.05, true, null, "p");
        }

        var history = await _store.GetSkillEvaluationHistoryAsync("skill-many", 3);

        Assert.Equal(3, history.Count);
    }
}
