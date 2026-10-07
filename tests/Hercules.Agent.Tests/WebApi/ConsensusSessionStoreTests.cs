using Hercules.Config;
using Hercules.WebApi.Consensus;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.WebApi;

/// <summary>
/// Stage 7.8 — consensus history.
/// <para>
/// The point of the history is reviewing what agents actually said and why an answer was
/// chosen, so these tests pin durability across a store restart, the newest-first
/// ordering, and the retention cap.
/// </para>
/// </summary>
public class ConsensusSessionStoreTests : IDisposable
{
    private readonly string _tempDir;

    public ConsensusSessionStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hercules-consensus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
    }

    private ConsensusSessionStore NewStore() =>
        new(new StorageConfig { DataRoot = _tempDir }, NullLogger<ConsensusSessionStore>.Instance);

    private static ConsensusSession Session(string id, string prompt, DateTimeOffset? at = null) => new(
        id,
        prompt,
        ["alpha", "beta"],
        [new ConsensusAnswer("alpha", "c1", "hold"), new ConsensusAnswer("beta", "c2", "ship")],
        "manual",
        "ship",
        null,
        at ?? DateTimeOffset.UtcNow);

    [Fact]
    public void Save_ThenList_ReturnsTheSession()
    {
        var store = NewStore();
        store.Save(Session("s1", "ship on Friday?"));

        var list = store.List();
        Assert.Single(list);
        Assert.Equal("ship on Friday?", list[0].Prompt);
    }

    [Fact]
    public void List_IsNewestFirst()
    {
        var store = NewStore();
        var now = DateTimeOffset.UtcNow;
        store.Save(Session("old", "older", now.AddMinutes(-10)));
        store.Save(Session("new", "newer", now));

        Assert.Equal(["new", "old"], store.List().Select(s => s.Id));
    }

    [Fact]
    public void Save_SameId_ReplacesRatherThanDuplicates()
    {
        var store = NewStore();
        store.Save(Session("s1", "first"));
        store.Save(Session("s1", "second") with { Result = "changed" });

        var list = store.List();
        Assert.Single(list);
        Assert.Equal("second", list[0].Prompt);
        Assert.Equal("changed", list[0].Result);
    }

    [Fact]
    public void List_IsCappedAtMaxSessionsDroppingOldest()
    {
        var store = NewStore();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < ConsensusSessionStore.MaxSessions + 5; i++)
        {
            store.Save(Session($"s{i}", $"prompt {i}", now.AddMinutes(i)));
        }

        var list = store.List();
        Assert.Equal(ConsensusSessionStore.MaxSessions, list.Count);
        // Newest kept, oldest dropped.
        Assert.Equal($"s{ConsensusSessionStore.MaxSessions + 4}", list[0].Id);
        Assert.DoesNotContain(list, s => s.Prompt == "prompt 0");
    }

    [Fact]
    public void History_SurvivesARestart()
    {
        // The whole reason history is persisted rather than held in memory.
        NewStore().Save(Session("s1", "remembered", new DateTimeOffset(2026, 10, 6, 9, 0, 0, TimeSpan.Zero)));

        var reopened = NewStore();
        var found = reopened.Get("s1");

        Assert.NotNull(found);
        Assert.Equal("remembered", found!.Prompt);
        Assert.Equal(2, found.Responses.Count);
        Assert.Equal("ship", found.Result);
    }

    [Fact]
    public void Get_UnknownId_ReturnsNull()
    {
        Assert.Null(NewStore().Get("nope"));
    }

    [Fact]
    public void CorruptFile_StartsEmptyRatherThanThrowing()
    {
        // The store creates its own directory on construction, so write the bad file
        // through a throwaway instance first.
        NewStore();
        File.WriteAllText(Path.Combine(_tempDir, "consensus", "sessions.json"), "{ not json");

        var store = NewStore();
        Assert.Empty(store.List());
    }
}