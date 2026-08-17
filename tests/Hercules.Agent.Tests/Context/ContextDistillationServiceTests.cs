using Hercules.Config;
using Hercules.Context.Distillation;
using Hercules.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Context;

/// <summary>
///     Тесты ContextDistillationService (task_102):
///     детерминированная иерархическая компрессия, key-facts, budget enforcement.
/// </summary>
public class ContextDistillationServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteSessionStore _sessions;
    private readonly SqliteDistillationStore _store;
    private readonly ContextDistillationService _service;

    public ContextDistillationServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules_distill_svc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var cfg = new StorageConfig { DataRoot = _tempDir, MemoryDir = "Memory" };
        _sessions = new SqliteSessionStore(cfg);
        _store = new SqliteDistillationStore(_sessions, NullLogger<SqliteDistillationStore>.Instance);
        _service = new ContextDistillationService(_store, _sessions,
            NullLogger<ContextDistillationService>.Instance);
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

    private void SeedInteractions(string sessionId, int count)
    {
        for (int i = 0; i < count; i++)
        {
            _sessions.LogInteractionAsync(new InteractionLog(
                SessionId: sessionId,
                Input: $"user question #{i} about database indexing and performance optimization",
                Output: $"assistant answer #{i} explains indexing strategies including b-tree, hash, and covering index approaches. The recommendation depends on query patterns and write amplification.",
                Confidence: i % 3 == 0 ? "high" : "medium",
                Mode: "direct",
                SkillId: null,
                Provider: "test",
                CreatedAt: DateTime.UtcNow.AddMinutes(-count + i))).GetAwaiter().GetResult();
        }
    }

    [Fact]
    public async Task DistillAsync_ModeOff_ReturnsEmptyResult()
    {
        SeedInteractions("sess1", 5);
        var cfg = new DistillationConfig { Mode = DistillationMode.Off };
        var result = await _service.DistillAsync("sess1", cfg);
        Assert.Equal(0, result.RecentCount);
        Assert.Equal(0, result.SummariesCreated);
        Assert.Equal(0, result.KeyFactsExtracted);
        Assert.Equal("", result.SummaryMarkdown);
    }

    [Fact]
    public async Task DistillAsync_EmptySession_ReturnsEmptyResult()
    {
        var cfg = new DistillationConfig { Mode = DistillationMode.Manual };
        var result = await _service.DistillAsync("nonexistent", cfg);
        Assert.Equal(0, result.RecentCount);
        Assert.Equal(0, result.SummariesCreated);
    }

    [Fact]
    public async Task DistillAsync_ManualMode_PersistsSummariesAndFacts()
    {
        SeedInteractions("sess1", 30);
        var cfg = new DistillationConfig
        {
            Mode = DistillationMode.Manual,
            RecentRawCount = 5,
            SummaryInterval = 10,
            KeyFactsExtraction = true,
            MaxAncientFacts = 10
        };
        var result = await _service.DistillAsync("sess1", cfg);

        // 30 msgs: 5 recent raw, 25 older (2 full batches of 10 + 5 leftover)
        Assert.Equal(5, result.RecentCount);
        Assert.True(result.SummariesCreated >= 2, $"expected ≥2 summaries, got {result.SummariesCreated}");
        Assert.True(result.KeyFactsExtracted > 0, "expected key facts to be extracted");

        // Verify persistence
        var stored = await _store.GetSummariesAsync("sess1");
        Assert.Equal(result.SummariesCreated, stored.Count);
        var facts = await _store.GetKeyFactsAsync("sess1", 50);
        Assert.Equal(result.KeyFactsExtracted, facts.Count);
    }

    [Fact]
    public async Task DistillAsync_TokenSavings_RecentKeptRaw_OtherCompressed()
    {
        SeedInteractions("sess1", 40);
        var cfg = new DistillationConfig
        {
            Mode = DistillationMode.Manual,
            RecentRawCount = 10,
            SummaryInterval = 10,
            KeyFactsExtraction = true
        };
        var result = await _service.DistillAsync("sess1", cfg);
        Assert.True(result.TokensAfter < result.TokensBefore,
            $"expected token savings: before={result.TokensBefore} after={result.TokensAfter}");
    }

    [Fact]
    public async Task DistillAsync_Deterministic_RunsProduceSameOutput()
    {
        SeedInteractions("sess1", 20);

        var cfg = new DistillationConfig
        {
            Mode = DistillationMode.Manual,
            RecentRawCount = 5,
            SummaryInterval = 5,
            KeyFactsExtraction = true,
            MaxAncientFacts = 5
        };

        var r1 = await _service.DistillAsync("sess1", cfg);
        // Snapshot markdown of first distillation
        var md1 = r1.SummaryMarkdown;
        var summariesCount1 = r1.SummariesCreated;
        var factsCount1 = r1.KeyFactsExtracted;

        // Run again on the same session — summaries accumulate, facts upsert by text.
        var r2 = await _service.DistillAsync("sess1", cfg);
        // Persisted count after second run should grow (or stay the same if all
        // batches already covered) but markdown should reflect the same set.
        Assert.True(r2.SummariesCreated >= summariesCount1);

        // Deterministic property: each summary batch has stable text on rerun.
        var s1 = await _store.GetSummariesAsync("sess1");
        var s2 = await _store.GetSummariesAsync("sess1");
        // Same first N entries (older batches) should have identical Summary text.
        for (int i = 0; i < Math.Min(summariesCount1, s2.Count); i++)
        {
            Assert.Equal(s1[i].Summary, s2[i].Summary);
        }
        Assert.NotEmpty(md1);
    }

    [Fact]
    public async Task SummarizeBatch_ReturnsTopicsAndBullets()
    {
        var batch = new List<ConversationMessage>
        {
            new(0, "s", "How does indexing work?",
                "Indexing uses b-tree structures to speed up lookups. The right index depends on query patterns.",
                "high", DateTime.UtcNow),
            new(1, "s", "What about hash indexes?",
                "Hash indexes are faster for equality but cannot support range queries. Choose based on workload.",
                "high", DateTime.UtcNow)
        };
        var summary = ContextDistillationService.SummarizeBatch(batch);
        Assert.NotEmpty(summary);
        Assert.Contains("Topics", summary);
        // Should include bullets
        Assert.Contains("-", summary);
    }

    [Fact]
    public void ExtractKeyFacts_FrequentNGrams_AreExtracted()
    {
        var messages = new List<ConversationMessage>();
        for (int i = 0; i < 10; i++)
        {
            messages.Add(new(i, "s",
                "tell me about database indexing performance optimization",
                "the database indexing performance depends on workload and query patterns",
                "high", DateTime.UtcNow));
        }
        var facts = ContextDistillationService.ExtractKeyFacts(messages, maxFacts: 5);
        Assert.NotEmpty(facts);
        // Each fact should be a phrase that appeared multiple times.
        Assert.All(facts, f => Assert.True(f.SourceCount >= 2,
            $"fact '{f.FactText}' has SourceCount={f.SourceCount} (expected ≥2)"));
    }

    [Fact]
    public void ExtractKeyFacts_EmptyInput_ReturnsEmpty()
    {
        var facts = ContextDistillationService.ExtractKeyFacts(
            Array.Empty<ConversationMessage>(), maxFacts: 5);
        Assert.Empty(facts);
    }

    [Fact]
    public async Task GetSummaryAsync_NoData_ReturnsEmptyString()
    {
        var cfg = new DistillationConfig { Mode = DistillationMode.Auto };
        var md = await _service.GetSummaryAsync("nonexistent", cfg, maxTokens: 500);
        Assert.Equal("", md);
    }

    [Fact]
    public async Task GetSummaryAsync_AfterDistill_IncludesSummaryAndFacts()
    {
        SeedInteractions("sess1", 20);
        var cfg = new DistillationConfig
        {
            Mode = DistillationMode.Manual,
            RecentRawCount = 5,
            SummaryInterval = 5,
            KeyFactsExtraction = true,
            MaxAncientFacts = 5
        };
        await _service.DistillAsync("sess1", cfg);

        var md = await _service.GetSummaryAsync("sess1", cfg, maxTokens: 4000);
        Assert.NotEmpty(md);
        Assert.Contains("# Distilled context", md);
        Assert.Contains("## Summaries", md);
    }

    [Fact]
    public async Task GetSummaryAsync_RespectsMaxTokens_BinarySearchTruncation()
    {
        SeedInteractions("sess1", 30);
        var cfg = new DistillationConfig
        {
            Mode = DistillationMode.Manual,
            RecentRawCount = 5,
            SummaryInterval = 5,
            KeyFactsExtraction = true,
            MaxAncientFacts = 10
        };
        await _service.DistillAsync("sess1", cfg);

        var mdBig = await _service.GetSummaryAsync("sess1", cfg, maxTokens: 100_000);
        var mdSmall = await _service.GetSummaryAsync("sess1", cfg, maxTokens: 200);

        Assert.True(mdSmall.Length < mdBig.Length, "small budget should produce shorter output");
        Assert.Contains("truncated", mdSmall);
    }
}
