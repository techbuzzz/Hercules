using System.Text.Json;
using Hercules.WorkflowServer.Models;
using Hercules.WorkflowServer.Storage;
using Xunit;

namespace Hercules.Agent.Tests.WorkflowServer;

/// <summary>
///     task_104: unit-тесты для <see cref="SqliteWorkflowDefinitionStore"/>.
///     Каждый тест использует свой temp-каталог чтобы изолировать БД.
/// </summary>
public class SqliteWorkflowDefinitionStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteWorkflowDefinitionStore _store;

    public SqliteWorkflowDefinitionStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hercules-wfs-store-" + Guid.NewGuid().ToString("N"));
        _store = new SqliteWorkflowDefinitionStore(_tempDir);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task SaveAsync_NewDefinition_AssignsIdAndPersists()
    {
        var def = NewDefinition("flow-1");

        var saved = await _store.SaveAsync(def);

        Assert.False(string.IsNullOrEmpty(saved.Id));
        Assert.NotEqual(default, saved.CreatedAt);
        Assert.NotEqual(default, saved.UpdatedAt);
    }

    [Fact]
    public async Task SaveAsync_ThenGet_RoundTrips()
    {
        var def = NewDefinition("flow-rt");
        var saved = await _store.SaveAsync(def);

        var loaded = await _store.GetAsync(saved.Id);

        Assert.NotNull(loaded);
        Assert.Equal(saved.Id, loaded.Id);
        Assert.Equal("flow-rt", loaded.Name);
        Assert.Equal(2, loaded.Version);
        Assert.Equal("graph", loaded.Description);
        // GraphJson: round-trip через JsonElement — сравниваем raw JSON.
        Assert.Equal(saved.GraphJson.GetRawText(), loaded.GraphJson.GetRawText());
    }

    [Fact]
    public async Task SaveAsync_Update_ReplacesExisting()
    {
        var def = NewDefinition("flow-upd");
        var saved = await _store.SaveAsync(def);

        saved.Description = "updated";
        var updated = await _store.SaveAsync(saved);

        var loaded = await _store.GetAsync(saved.Id);
        Assert.Equal("updated", loaded.Description);
        // ID должен сохраниться.
        Assert.Equal(saved.Id, updated.Id);
    }

    [Fact]
    public async Task ListAsync_ReturnsSummariesWithoutGraphJson()
    {
        await _store.SaveAsync(NewDefinition("a"));
        await _store.SaveAsync(NewDefinition("b"));

        var list = await _store.ListAsync();

        Assert.Equal(2, list.Count);
        // WorkflowSummary — нет GraphJson поля, проверяем что list не падает.
        Assert.Contains(list, x => x.Name == "a");
        Assert.Contains(list, x => x.Name == "b");
    }

    [Fact]
    public async Task GetAsync_Nonexistent_ReturnsNull()
    {
        var loaded = await _store.GetAsync("does-not-exist");

        Assert.Null(loaded);
    }

    [Fact]
    public async Task DeleteAsync_Existing_ReturnsTrue()
    {
        var def = NewDefinition("to-delete");
        var saved = await _store.SaveAsync(def);

        var deleted = await _store.DeleteAsync(saved.Id);

        Assert.True(deleted);
        Assert.Null(await _store.GetAsync(saved.Id));
    }

    [Fact]
    public async Task DeleteAsync_Nonexistent_ReturnsFalse()
    {
        var deleted = await _store.DeleteAsync("nope");

        Assert.False(deleted);
    }

    [Fact]
    public void IsHealthy_OnFreshStore_ReturnsTrue()
    {
        Assert.True(_store.IsHealthy());
    }

    private static WorkflowDefinition NewDefinition(string name) => new()
    {
        Name = name,
        Version = 2,
        Description = "graph",
        GraphJson = JsonDocument.Parse("""{"nodes":[{"id":"start","type":"StartNode"}],"edges":[]}""").RootElement,
    };
}
