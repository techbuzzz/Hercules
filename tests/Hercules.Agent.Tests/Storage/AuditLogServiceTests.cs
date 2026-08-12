using Hercules.Config;
using Hercules.Storage;
using Xunit;

namespace Hercules.Agent.Tests.Storage;

/// <summary>
///     Тесты AuditLogService: LogAsync, GetRecentAsync, GetByTargetAsync.
/// </summary>
public class AuditLogServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteSessionStore _store;
    private readonly AuditLogService _svc;

    public AuditLogServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-audit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var cfg = new StorageConfig { DataRoot = _tempDir };
        _store = new SqliteSessionStore(cfg);
        _svc = new AuditLogService(_store);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task LogAsync_ThenGetRecent_ReturnsEntry()
    {
        await _svc.LogAsync("agent", "skill_created", "skill-abc", null, "s1");

        var entries = await _svc.GetRecentAsync();

        Assert.Single(entries);
        Assert.Equal("agent", entries[0].Actor);
        Assert.Equal("skill_created", entries[0].Action);
        Assert.Equal("skill-abc", entries[0].Target);
        Assert.Equal("s1", entries[0].SessionId);
    }

    [Fact]
    public async Task LogAsync_MultipleEntries_GetRecent_ReturnsNewestFirst()
    {
        await _svc.LogAsync("agent", "skill_created", "skill-1", null, "s1");
        await _svc.LogAsync("user", "config_changed", null, "{\"key\":\"value\"}", "s2");
        await _svc.LogAsync("system", "session_started", "s3", null, "s3");

        var entries = await _svc.GetRecentAsync(3);

        Assert.Equal(3, entries.Count);
        Assert.Equal("session_started", entries[0].Action);  // newest
        Assert.Equal("skill_created", entries[2].Action);   // oldest
    }

    [Fact]
    public async Task GetRecentAsync_WithLimit_RespectsLimit()
    {
        for (int i = 0; i < 10; i++)
        {
            await _svc.LogAsync("agent", $"action_{i}", null, null, null);
        }

        var entries = await _svc.GetRecentAsync(5);

        Assert.Equal(5, entries.Count);
    }

    [Fact]
    public async Task GetByTargetAsync_ReturnsOnlyMatchingEntries()
    {
        await _svc.LogAsync("agent", "skill_created", "skill-abc", null, "s1");
        await _svc.LogAsync("agent", "skill_updated", "skill-abc", null, "s1");
        await _svc.LogAsync("agent", "skill_created", "skill-xyz", null, "s2");

        var entries = await _svc.GetByTargetAsync("skill-abc");

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal("skill-abc", e.Target));
    }

    [Fact]
    public async Task LogAsync_WithNullTarget_AndDetails_StoredCorrectly()
    {
        await _svc.LogAsync("user", "budget_queried", null, "{\"cost\":0.001}", null);

        var entries = await _svc.GetRecentAsync();

        Assert.Single(entries);
        Assert.Null(entries[0].Target);
        Assert.NotNull(entries[0].Details);
        Assert.Contains("cost", entries[0].Details);
    }

    [Fact]
    public async Task GetAuditLog_EmptyStore_ReturnsEmptyList()
    {
        var entries = await _svc.GetRecentAsync();

        Assert.Empty(entries);
    }
}
