using System.Diagnostics;
using System.Text.Json;
using Hercules.Config;
using Hercules.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Reflection;

/// <summary>
///     Tests for task_084 — ProposalStore snapshot cache + FileSystemWatcher invalidation.
///     We exercise only the public surface (Save / ListAll / GetRecent / CountToday /
///     GetBySkill / Delete) without assuming any specific debounce timing on the watcher.
/// </summary>
public class ProposalStoreCachingTests : IDisposable
{
    private static readonly JsonSerializerOptions StoreJsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _tempDir;
    private readonly ProposalStore _store;

    public ProposalStoreCachingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"proposal_store_cache_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var cfg = new StorageConfig
        {
            DataRoot = _tempDir,
            SkillsDir = "Skills"
        };

        _store = new ProposalStore(cfg, NullLogger<ProposalStore>.Instance);
    }

    public void Dispose()
    {
        _store.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Constructor_LoadsExistingFilesIntoSnapshot()
    {
        // Arrange: drop a proposal file directly on disk before constructing the store.
        var id = Guid.NewGuid().ToString("N")[..12];
        var existing = new Proposal
        {
            Id = id,
            SkillId = "preloaded",
            SkillName = "Preloaded",
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        var dir = Path.Combine(_tempDir, "Skills", ".proposals");
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, $"{id}.json"),
            JsonSerializer.Serialize(existing, StoreJsonOpts));

        // Act: construct a fresh store
        using var store = new ProposalStore(
            new StorageConfig { DataRoot = _tempDir, SkillsDir = "Skills" },
            NullLogger<ProposalStore>.Instance);

        // Assert
        var all = store.ListAll();
        Assert.Single(all);
        Assert.Equal(id, all[0].Id);
        Assert.Equal("preloaded", all[0].SkillId);
    }

    [Fact]
    public void Save_PopulatesSnapshotImmediately()
    {
        var p = NewProposal("save-immediate");

        _store.Save(p);

        // Snapshot is updated eagerly on Save() — no watcher debounce wait.
        var all = _store.ListAll();
        Assert.Single(all);
        Assert.Equal(p.Id, all[0].Id);
    }

    [Fact]
    public void ListAll_AfterMultipleSaves_ReturnsAllOrdered()
    {
        _store.Save(NewProposal("a", minutesAgo: 10));
        _store.Save(NewProposal("b", minutesAgo: 5));
        _store.Save(NewProposal("c", minutesAgo: 1));

        var all = _store.ListAll();

        Assert.Equal(3, all.Count);
        Assert.Equal("c", all[0].Id); // most recent first
        Assert.Equal("b", all[1].Id);
        Assert.Equal("a", all[2].Id);
    }

    [Fact]
    public void GetBySkill_FiltersFromCache()
    {
        _store.Save(NewProposal("x1", skillId: "skill-a"));
        _store.Save(NewProposal("x2", skillId: "skill-b"));
        _store.Save(NewProposal("x3", skillId: "skill-a"));

        var forSkillA = _store.GetBySkill("skill-a");

        Assert.Equal(2, forSkillA.Count);
        Assert.All(forSkillA, p => Assert.Equal("skill-a", p.SkillId));
    }

    [Fact]
    public void GetRecent_RespectsLimit()
    {
        for (var i = 0; i < 5; i++)
        {
            _store.Save(NewProposal($"r{i}", minutesAgo: 5 - i));
        }

        var recent = _store.GetRecent(2);

        Assert.Equal(2, recent.Count);
        Assert.Equal("r4", recent[0].Id);
        Assert.Equal("r3", recent[1].Id);
    }

    [Fact]
    public void CountToday_OnlyIncludesTodayUtc()
    {
        _store.Save(NewProposal("today1", minutesAgo: 1));
        _store.Save(NewProposal("today2", minutesAgo: 30));
        _store.Save(NewProposal("yesterday", minutesAgo: 60 * 25)); // > 24h ago

        var count = _store.CountToday();

        Assert.Equal(2, count);
    }

    [Fact]
    public void Delete_RemovesFromSnapshot()
    {
        var p = NewProposal("to-delete");
        _store.Save(p);
        Assert.Single(_store.ListAll());

        var deleted = _store.Delete(p.Id);

        Assert.True(deleted);
        Assert.Empty(_store.ListAll());
    }

    [Fact]
    public void ListAllAsync_BypassesCacheAndRefreshes()
    {
        // Drop a file on disk directly so it's not in the snapshot
        var id = Guid.NewGuid().ToString("N")[..12];
        var dir = Path.Combine(_tempDir, "Skills", ".proposals");
        var path = Path.Combine(dir, $"{id}.json");
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(NewProposal(id), StoreJsonOpts));

        // Cache miss before refresh
        Assert.Empty(_store.ListAll());

        var refreshed = _store.ListAllAsync().GetAwaiter().GetResult();

        Assert.Single(refreshed);
        Assert.Equal(id, refreshed[0].Id);
        // Subsequent synchronous read uses the updated snapshot
        Assert.Single(_store.ListAll());
    }

    [Fact]
    public async Task FileSystemWatcher_InvalidatesCache_OnExternalAdd()
    {
        // External file create (simulating another process / the OS) should
        // trigger cache invalidation within the debounce window.
        var id = Guid.NewGuid().ToString("N")[..12];
        var dir = Path.Combine(_tempDir, "Skills", ".proposals");
        var path = Path.Combine(dir, $"{id}.json");

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(NewProposal(id), StoreJsonOpts));

        // Wait up to 5s for the watcher + debounce (250ms) + refresh to settle.
        var sw = Stopwatch.StartNew();
        var saw = false;
        while (sw.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (_store.ListAll().Any(p => p.Id == id))
            {
                saw = true;
                break;
            }
            await Task.Delay(100);
        }

        Assert.True(saw, "Watcher did not invalidate cache after external file create");
    }

    [Fact]
    public async Task FileSystemWatcher_InvalidatesCache_OnExternalDelete()
    {
        var p = NewProposal("watcher-delete");
        _store.Save(p);
        Assert.Single(_store.ListAll());

        var path = Path.Combine(_tempDir, "Skills", ".proposals", $"{p.Id}.json");
        File.Delete(path);

        var sw = Stopwatch.StartNew();
        var empty = false;
        while (sw.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (_store.ListAll().Count == 0)
            {
                empty = true;
                break;
            }
            await Task.Delay(100);
        }

        Assert.True(empty, "Watcher did not invalidate cache after external file delete");
    }

    [Fact]
    public void ListAll_DoesNotLeakMutatedListsToCaller()
    {
        // Defensive: callers that mutate the returned proposal's list fields
        // must not affect what subsequent reads return.
        var p = NewProposal("clone");
        p.ProposedPhrases.Add("alpha");
        _store.Save(p);

        // Snapshot before mutation
        var baseline = _store.ListAll();
        Assert.Equal(2, baseline[0].ProposedPhrases.Count);
        Assert.Contains("x", baseline[0].ProposedPhrases);
        Assert.Contains("alpha", baseline[0].ProposedPhrases);

        // Mutate the returned clone (not the snapshot)
        var first = _store.ListAll();
        first[0].ProposedPhrases.Add("rogue");
        first[0].ProposedPhrases.Clear();

        // Snapshot must be unchanged
        var second = _store.ListAll();
        Assert.Equal(2, second[0].ProposedPhrases.Count);
        Assert.Contains("x", second[0].ProposedPhrases);
        Assert.Contains("alpha", second[0].ProposedPhrases);
    }

    [Fact]
    public void ReadAccess_IsLockFree_UnderConcurrentWriters()
    {
        // Stress: many concurrent saves + reads should not throw, deadlock, or
        // produce torn reads. We only check no exceptions and final count matches.
        var ids = Enumerable.Range(0, 50).Select(i => NewProposal($"c{i}")).ToList();
        var readTasks = Enumerable.Range(0, 4).Select(async _ =>
        {
            for (var i = 0; i < 50; i++)
            {
                _store.ListAll();
                _store.CountToday();
                await Task.Yield();
            }
        }).ToArray();

        var writeTasks = ids.Select(p => Task.Run(() => _store.Save(p))).ToArray();
        Task.WhenAll(readTasks.Concat(writeTasks)).GetAwaiter().GetResult();

        var all = _store.ListAll();
        Assert.Equal(ids.Count, all.Count);
    }

    private static Proposal NewProposal(string id, int minutesAgo = 0, string skillId = "skill") => new()
    {
        Id = id,
        SkillId = skillId,
        SkillName = skillId,
        AnalysisSummary = "test",
        ProposedPrompt = "p",
        ProposedPhrases = new List<string> { "x" },
        CurrentVersion = 1,
        RollbackVersion = 0,
        CreatedAt = DateTime.UtcNow.AddMinutes(-minutesAgo),
        Status = ProposalStatus.Proposed
    };
}
