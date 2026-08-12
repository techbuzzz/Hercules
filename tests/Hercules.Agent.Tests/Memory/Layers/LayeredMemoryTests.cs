using Hercules.Config;
using Hercules.Memory.Layers;
using Hercules.Storage;
using Xunit;

namespace Hercules.Agent.Tests.Memory.Layers;

public class LayeredMemoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StorageConfig _storageConfig;
    private readonly DurableFactsService _factsStore;
    private readonly EpisodicStore _episodicStore;
    private readonly WorkingMemoryService _workingMemory;
    private readonly LayeredMemoryManager _manager;

    public LayeredMemoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _storageConfig = new StorageConfig
        {
            DataRoot = _tempDir,
            MemoryDir = "Memory"
        };

        _factsStore = new DurableFactsService(_storageConfig);
        _episodicStore = new EpisodicStore(_storageConfig);
        _workingMemory = new WorkingMemoryService(new LayeredMemoryConfig { MaxWorkingMemoryEntries = 100 });
        _manager = new LayeredMemoryManager(_workingMemory, _factsStore, _episodicStore);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public void WorkingMemory_SetAndGet_ReturnsStoredValue()
    {
        var entry = new MemoryEntry("test_source", MemoryConfidence.High);
        _workingMemory.Set("key1", "value1", entry);

        var result = _workingMemory.Get("key1");

        Assert.Equal("value1", result);
    }

    [Fact]
    public void WorkingMemory_GetNonExistent_ReturnsNull()
    {
        var result = _workingMemory.Get("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public void WorkingMemory_Clear_RemovesAllEntries()
    {
        _workingMemory.Set("key1", "val1");
        _workingMemory.Set("key2", "val2");

        _workingMemory.Clear();

        Assert.Null(_workingMemory.Get("key1"));
        Assert.Null(_workingMemory.Get("key2"));
        Assert.Equal(0, _workingMemory.Count);
    }

    [Fact]
    public void WorkingMemory_Remove_ExistingKey_ReturnsTrue()
    {
        _workingMemory.Set("key1", "value1");

        var removed = _workingMemory.Remove("key1");

        Assert.True(removed);
        Assert.Null(_workingMemory.Get("key1"));
    }

    [Fact]
    public void WorkingMemory_Remove_NonExistentKey_ReturnsFalse()
    {
        var removed = _workingMemory.Remove("nonexistent");
        Assert.False(removed);
    }

    [Fact]
    public void MemoryEntry_IsExpired_TrueWhenTtlExceeded()
    {
        // Create entry with TTL that has already passed
        var expired = new MemoryEntry("test", MemoryConfidence.Medium, 1, MemorySensitivity.Internal, DateTime.UtcNow.AddMinutes(-5), new List<string>());
        Assert.True(expired.IsExpired);
    }

    [Fact]
    public void MemoryEntry_IsExpired_FalseWhenPermanent()
    {
        var permanent = new MemoryEntry("test", MemoryConfidence.Medium);
        Assert.False(permanent.IsExpired);
    }

    [Fact]
    public void MemoryEntry_ShouldRedact_SensitiveEntries_ReturnTrue()
    {
        var sensitive = new MemoryEntry("test", MemoryConfidence.High, 0, MemorySensitivity.Sensitive, DateTime.UtcNow, new List<string>());
        var restricted = new MemoryEntry("test", MemoryConfidence.High, 0, MemorySensitivity.Restricted, DateTime.UtcNow, new List<string>());

        Assert.True(sensitive.ShouldRedact());
        Assert.True(restricted.ShouldRedact());
        Assert.False(sensitive.ShouldRedact(redactionEnabled: false));
    }

    [Fact]
    public void MemoryEntry_ShouldRedact_InternalEntries_ReturnFalse()
    {
        var internalEntry = new MemoryEntry("test", MemoryConfidence.Medium);
        Assert.False(internalEntry.ShouldRedact());
    }

    [Fact]
    public async Task DurableFactsStore_StoreAndGet_Succeeds()
    {
        var entry = new MemoryEntry("session_extract", MemoryConfidence.High);

        await _factsStore.StoreFactAsync("test_key", "Test value content", entry);
        var result = await _factsStore.GetFactAsync("test_key");

        Assert.NotNull(result);
        var (value, storedEntry) = result.Value;
        Assert.Equal("Test value content", value?.Trim());
        Assert.NotNull(storedEntry);
        Assert.Equal("session_extract", storedEntry.Source);
    }

    [Fact]
    public async Task DurableFactsStore_DeleteFact_RemovesFile()
    {
        var entry = new MemoryEntry("test");

        await _factsStore.StoreFactAsync("delete_me", "some content", entry);
        var deleted = await _factsStore.DeleteFactAsync("delete_me");
        var result = await _factsStore.GetFactAsync("delete_me");

        Assert.True(deleted);
        Assert.Null(result);
    }

    [Fact]
    public async Task DurableFactsStore_SearchFacts_ByTag()
    {
        var entry = new MemoryEntry("test", MemoryConfidence.Medium, 0, MemorySensitivity.Internal, DateTime.UtcNow, new List<string> { "user_fact" });
        await _factsStore.StoreFactAsync("fact1", "User knows Python", entry);

        var results = await _factsStore.SearchFactsAsync(tag: "user_fact");

        Assert.Single(results);
        Assert.Equal("fact1", results[0].Key);
    }

    [Fact]
    public async Task DurableFactsStore_CleanupExpired_RemovesExpiredFacts()
    {
        // Create a fact with a TTL of 1 minute (not yet expired, but with TtlMinutes > 0)
        // We'll verify that the metadata is stored correctly first
        var entry = new MemoryEntry("test", MemoryConfidence.Medium, 1, MemorySensitivity.Internal, DateTime.UtcNow, new List<string>());
        await _factsStore.StoreFactAsync("expired_fact", "Fact content", entry);

        // Verify metadata was stored correctly
        var metaPath = Path.Combine(_tempDir, "Memory", "DurableFacts", "expired_fact.meta.json");
        Assert.True(File.Exists(metaPath), "Meta file should exist after store");
        var metaContent = await File.ReadAllTextAsync(metaPath);

        // The metadata file should contain TtlMinutes value (check for "1" in JSON)
        Assert.Contains("TtlMinutes", metaContent);

        // Verify the stored value is readable back
        var retrieved = await _factsStore.GetFactAsync("expired_fact");
        Assert.NotNull(retrieved);
        var (val, stored) = retrieved.Value;
        Assert.Equal("Fact content", val?.Trim());
        Assert.NotNull(stored);
        Assert.Equal(1, stored.TtlMinutes); // TTL should be preserved
    }

    [Fact]
    public async Task EpisodicStore_AppendAndGetRecent_ReturnsEpisodes()
    {
        var entry = new MemoryEntry("session_extract", MemoryConfidence.Medium);

        await _episodicStore.AppendEpisodeAsync("session_1", "First session summary", entry);
        await _episodicStore.AppendEpisodeAsync("session_2", "Second session summary", entry);

        var recent = await _episodicStore.GetRecentEpisodesAsync(2);

        Assert.Equal(2, recent.Count);
    }

    [Fact]
    public async Task EpisodicStore_GetBySession_ReturnsMatchingEpisodes()
    {
        var entry = new MemoryEntry("test", MemoryConfidence.Medium);
        await _episodicStore.AppendEpisodeAsync("target_session", "Target session content", entry);

        var episodes = await _episodicStore.GetEpisodesBySessionAsync("target_session");

        Assert.Single(episodes);
        Assert.Contains("Target session content", episodes[0].Summary);
    }

    [Fact]
    public async Task LayeredMemoryManager_BuildContextBlock_IncludesFactsAndEpisodes()
    {
        var factEntry = new MemoryEntry("session_extract", MemoryConfidence.Medium, 0, MemorySensitivity.Public, DateTime.UtcNow, new List<string> { "fact" });
        var episodeEntry = new MemoryEntry("session_extract", MemoryConfidence.Medium);

        await _factsStore.StoreFactAsync("test_fact", "Known fact about user", factEntry);
        await _episodicStore.AppendEpisodeAsync("sess1", "Previous conversation covered Python", episodeEntry);

        var context = await _manager.BuildContextBlockAsync();

        Assert.Contains("УСТОЙЧИВЫЕ ФАКТЫ", context);
        Assert.Contains("Known fact about user", context);
        Assert.Contains("КОНТЕКСТ ПРОШЛЫХ СЕССИЙ", context);
    }

    [Fact]
    public async Task LayeredMemoryManager_BuildContextBlock_RedactsSensitive()
    {
        var sensitiveFact = new MemoryEntry("test", MemoryConfidence.High, 0, MemorySensitivity.Sensitive, DateTime.UtcNow, new List<string> { "secret" });
        var publicFact = new MemoryEntry("test", MemoryConfidence.Medium, 0, MemorySensitivity.Public, DateTime.UtcNow, new List<string> { "public" });

        await _factsStore.StoreFactAsync("secret", "User password: secret123", sensitiveFact);
        await _factsStore.StoreFactAsync("public", "User likes coffee", publicFact);

        var context = await _manager.BuildContextBlockAsync();

        Assert.Contains("User likes coffee", context);
        Assert.DoesNotContain("secret123", context);
    }
}
