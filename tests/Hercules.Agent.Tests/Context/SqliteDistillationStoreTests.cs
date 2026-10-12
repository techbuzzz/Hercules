using Hercules.Config;
using Hercules.Context.Distillation;
using Hercules.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Context;

/// <summary>
///     Тесты SqliteDistillationStore (task_102): CRUD для summaries и key-facts.
/// </summary>
public class SqliteDistillationStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteSessionStore _sessions;
    private readonly SqliteDistillationStore _store;

    public SqliteDistillationStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules_distill_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var storageConfig = new StorageConfig
        {
            DataRoot = _tempDir,
            MemoryDir = "Memory"
        };
        _sessions = new SqliteSessionStore(storageConfig);
        _store = new SqliteDistillationStore(_sessions, NullLogger<SqliteDistillationStore>.Instance);
    }

    public void Dispose()
    {
        _store.Dispose();
        _sessions.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task SaveSummaryAsync_ThenGet_ReturnsPersisted()
    {
        var summary = new DistillationSummary(
            Id: 0,
            SessionId: "sess1",
            FromIndex: 0,
            ToIndex: 9,
            Summary: "**Topics:** alpha, beta\n- Some sentence here.",
            MessageCount: 10,
            TokenEstimate: 12,
            CreatedAt: DateTime.UtcNow);

        var id = await _store.SaveSummaryAsync(summary);
        Assert.True(id > 0);

        var loaded = await _store.GetSummariesAsync("sess1");
        Assert.Single(loaded);
        Assert.Equal("sess1", loaded[0].SessionId);
        Assert.Equal(0, loaded[0].FromIndex);
        Assert.Equal(9, loaded[0].ToIndex);
        Assert.Contains("alpha", loaded[0].Summary);
    }

    [Fact]
    public async Task GetSummariesAsync_OrderedByFromIndex_Ascending()
    {
        for (int i = 0; i < 3; i++)
        {
            await _store.SaveSummaryAsync(new DistillationSummary(
                Id: 0, SessionId: "sess", FromIndex: i * 10, ToIndex: i * 10 + 9,
                Summary: $"summary-{i}", MessageCount: 10, TokenEstimate: 5,
                CreatedAt: DateTime.UtcNow));
        }

        var loaded = await _store.GetSummariesAsync("sess");
        Assert.Equal(3, loaded.Count);
        Assert.Equal(0, loaded[0].FromIndex);
        Assert.Equal(10, loaded[1].FromIndex);
        Assert.Equal(20, loaded[2].FromIndex);
    }

    [Fact]
    public async Task GetSummariesAsync_EmptyForUnknownSession()
    {
        var loaded = await _store.GetSummariesAsync("does-not-exist");
        Assert.Empty(loaded);
    }

    [Fact]
    public async Task DeleteSummariesAsync_RemovesAllForSession()
    {
        await _store.SaveSummaryAsync(new DistillationSummary(0, "sess1", 0, 9, "x", 10, 5, DateTime.UtcNow));
        await _store.SaveSummaryAsync(new DistillationSummary(0, "sess1", 10, 19, "y", 10, 5, DateTime.UtcNow));
        await _store.SaveSummaryAsync(new DistillationSummary(0, "sess2", 0, 9, "z", 10, 5, DateTime.UtcNow));

        await _store.DeleteSummariesAsync("sess1");

        Assert.Empty(await _store.GetSummariesAsync("sess1"));
        Assert.Single(await _store.GetSummariesAsync("sess2"));
    }

    [Fact]
    public async Task SaveKeyFactsAsync_UpsertReplacesScore()
    {
        var fact1 = new KeyFact("alpha beta", Score: 10.0, SourceCount: 3, FirstSeen: DateTime.UtcNow);
        await _store.SaveKeyFactsAsync("sess1", new[] { fact1 });

        var loaded = await _store.GetKeyFactsAsync("sess1", 10);
        Assert.Single(loaded);
        Assert.Equal("alpha beta", loaded[0].FactText);
        Assert.Equal(10.0, loaded[0].Score);
        Assert.Equal(3, loaded[0].SourceCount);

        // upsert with new score
        var fact1Updated = new KeyFact("alpha beta", Score: 99.0, SourceCount: 5, FirstSeen: DateTime.UtcNow);
        await _store.SaveKeyFactsAsync("sess1", new[] { fact1Updated });

        var loaded2 = await _store.GetKeyFactsAsync("sess1", 10);
        Assert.Single(loaded2);
        Assert.Equal(99.0, loaded2[0].Score);
        Assert.Equal(5, loaded2[0].SourceCount);
    }

    [Fact]
    public async Task GetKeyFactsAsync_OrdersByScoreDesc()
    {
        var facts = new[]
        {
            new KeyFact("low", 1.0, 1, DateTime.UtcNow),
            new KeyFact("high", 50.0, 5, DateTime.UtcNow),
            new KeyFact("mid", 10.0, 2, DateTime.UtcNow)
        };
        await _store.SaveKeyFactsAsync("sess1", facts);

        var loaded = await _store.GetKeyFactsAsync("sess1", 10);
        Assert.Equal(3, loaded.Count);
        Assert.Equal("high", loaded[0].FactText);
        Assert.Equal("mid", loaded[1].FactText);
        Assert.Equal("low", loaded[2].FactText);
    }

    [Fact]
    public async Task GetKeyFactsAsync_LimitsResult()
    {
        var facts = Enumerable.Range(0, 10)
            .Select(i => new KeyFact($"fact-{i}", (double)i, 1, DateTime.UtcNow))
            .ToArray();
        await _store.SaveKeyFactsAsync("sess1", facts);

        var loaded = await _store.GetKeyFactsAsync("sess1", 3);
        Assert.Equal(3, loaded.Count);
        // top 3 by score
        Assert.Equal("fact-9", loaded[0].FactText);
        Assert.Equal("fact-8", loaded[1].FactText);
        Assert.Equal("fact-7", loaded[2].FactText);
    }

    [Fact]
    public async Task DeleteKeyFactsAsync_RemovesAllForSession()
    {
        await _store.SaveKeyFactsAsync("sess1", new[]
        {
            new KeyFact("a", 1, 1, DateTime.UtcNow),
            new KeyFact("b", 2, 1, DateTime.UtcNow)
        });
        await _store.DeleteKeyFactsAsync("sess1");
        Assert.Empty(await _store.GetKeyFactsAsync("sess1", 10));
    }
}
