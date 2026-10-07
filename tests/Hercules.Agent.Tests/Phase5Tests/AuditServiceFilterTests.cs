using Hercules.Audit;
using Hercules.Config;
using Hercules.Redaction;
using Hercules.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Phase5Tests;

/// <summary>
///     [task_087] Unit tests for AuditService.QueryAsync filter implementation.
///     Before task_087, <c>QueryAsync</c> silently dropped every filter except
///     <c>target</c>; the audit dashboard could not narrow down by actor,
///     action, sessionId, toolName, result, or time window. These tests
///     exercise each filter individually and combined, against a real
///     SqliteSessionStore so the SQL WHERE clause is verified end-to-end.
/// </summary>
public sealed class AuditServiceFilterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteSessionStore _store;
    private readonly AuditLogService _auditLog;
    private readonly AuditService _svc;

    public AuditServiceFilterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-audit-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new SqliteSessionStore(new StorageConfig { DataRoot = _tempDir });
        _auditLog = new AuditLogService(_store);

        var cfg = new AuditConfig { Enabled = true, PayloadHashEnabled = false };
        var redactionSvc = new RedactionService(cfg, NullLogger<RedactionService>.Instance);
        _svc = new AuditService(_auditLog, cfg, redactionSvc, new PayloadHashService(), NullLogger<AuditService>.Instance);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Query_RespectsActorFilter()
    {
        // [task_087] Two actors — only the matching one should be returned.
        await _svc.LogAsync("agent", "tool_executed", "fetch", "ok", "s1");
        await _svc.LogAsync("user",  "login",         "auth",  "ok", "s1");

        var fromAgent = await _svc.QueryAsync(actor: "agent", limit: 10);
        Assert.Single(fromAgent);
        Assert.Equal("agent", fromAgent[0].Actor);

        var fromUser = await _svc.QueryAsync(actor: "user", limit: 10);
        Assert.Single(fromUser);
        Assert.Equal("user", fromUser[0].Actor);
    }

    [Fact]
    public async Task Query_RespectsActionFilter()
    {
        await _svc.LogAsync("agent", "skill_created", "skill-a", null, "s1");
        await _svc.LogAsync("agent", "skill_deleted", "skill-b", null, "s1");

        var created = await _svc.QueryAsync(action: "skill_created", limit: 10);
        Assert.Single(created);
        Assert.Equal("skill_created", created[0].Action);
        Assert.Equal("skill-a", created[0].Target);
    }

    [Fact]
    public async Task Query_RespectsToolNameFilter()
    {
        // [task_087] LogToolExecutionAsync writes ToolName into the audit row.
        await _svc.LogToolExecutionAsync("agent", "http_fetch",  "success", null, "s1");
        await _svc.LogToolExecutionAsync("agent", "sql_query",   "success", null, "s1");
        await _svc.LogToolExecutionAsync("agent", "http_fetch",  "failure", "boom", "s1");

        var httpOnly = await _svc.QueryAsync(toolName: "http_fetch", limit: 10);
        Assert.Equal(2, httpOnly.Count);
        Assert.All(httpOnly, e => Assert.Equal("http_fetch", e.ToolName));
    }

    [Fact]
    public async Task Query_RespectsResultFilter()
    {
        // [task_087] Result filter is the workhorse of SLO dashboards.
        await _svc.LogToolExecutionAsync("agent", "http_fetch", "success", null, "s1");
        await _svc.LogToolExecutionAsync("agent", "http_fetch", "failure", "boom", "s1");
        await _svc.LogToolExecutionAsync("agent", "sql_query",  "timeout", null, "s1");

        var failures = await _svc.QueryAsync(result: "failure", limit: 10);
        Assert.Single(failures);
        Assert.Equal("http_fetch", failures[0].ToolName);
        Assert.Equal("failure", failures[0].Result);
    }

    [Fact]
    public async Task Query_RespectsSessionIdFilter()
    {
        await _svc.LogAsync("agent", "tool_executed", "fetch", "ok", "s-alpha");
        await _svc.LogAsync("agent", "tool_executed", "fetch", "ok", "s-beta");
        await _svc.LogAsync("agent", "tool_executed", "fetch", "ok", "s-alpha");

        var alpha = await _svc.QueryAsync(sessionId: "s-alpha", limit: 10);
        Assert.Equal(2, alpha.Count);
        Assert.All(alpha, e => Assert.Equal("s-alpha", e.SessionId));
    }

    [Fact]
    public async Task Query_RespectsFromAndToTimeWindow()
    {
        // Seed one entry, then sleep, then seed another — verifies the
        // SQL string comparison on the ISO 8601 created_at column.
        await _svc.LogAsync("agent", "first", null, null, "s1");
        await Task.Delay(50);
        var pivot = DateTime.UtcNow;
        await Task.Delay(50);
        await _svc.LogAsync("agent", "second", null, null, "s1");

        var onlyAfter = await _svc.QueryAsync(from: pivot, limit: 10);
        Assert.Single(onlyAfter);
        Assert.Equal("second", onlyAfter[0].Action);

        var onlyBefore = await _svc.QueryAsync(to: pivot, limit: 10);
        Assert.Single(onlyBefore);
        Assert.Equal("first", onlyBefore[0].Action);
    }

    [Fact]
    public async Task Query_CombinesAllFilters()
    {
        // [task_087] End-to-end: every filter applied simultaneously.
        await _svc.LogToolExecutionAsync("agent", "http_fetch", "failure", "boom", "s1");
        await _svc.LogToolExecutionAsync("agent", "http_fetch", "success", null,  "s1");
        await _svc.LogToolExecutionAsync("user",  "http_fetch", "failure", "boom", "s1");
        await _svc.LogToolExecutionAsync("agent", "sql_query",  "failure", null,  "s1");

        var combined = await _svc.QueryAsync(
            actor: "agent", action: "tool_executed",
            toolName: "http_fetch", result: "failure",
            sessionId: "s1", limit: 10);
        Assert.Single(combined);
        Assert.Equal("agent", combined[0].Actor);
        Assert.Equal("http_fetch", combined[0].ToolName);
        Assert.Equal("failure", combined[0].Result);
    }

    [Fact]
    public async Task Query_NoFilters_ReturnsRecent()
    {
        // [task_087] Backward compatibility — calling without filters still
        // returns the most recent rows (was the previous behaviour).
        await _svc.LogAsync("agent", "a1", null, null, "s1");
        await _svc.LogAsync("agent", "a2", null, null, "s1");
        await _svc.LogAsync("agent", "a3", null, null, "s1");

        var all = await _svc.QueryAsync(limit: 10);
        Assert.Equal(3, all.Count);
        // Newest first
        Assert.Equal("a3", all[0].Action);
        Assert.Equal("a1", all[2].Action);
    }

    [Fact]
    public async Task Query_RespectsLimit()
    {
        for (var i = 0; i < 5; i++)
        {
            await _svc.LogAsync("agent", $"a{i}", null, null, "s1");
        }

        var firstTwo = await _svc.QueryAsync(actor: "agent", limit: 2);
        Assert.Equal(2, firstTwo.Count);
    }

    [Fact]
    public void AuditLogQuery_DefaultsLimitToHundred()
    {
        // [task_087] Public default contract for AuditLogQuery.
        var q = new AuditLogQuery();
        Assert.Equal(100, q.EffectiveLimit);
    }

    [Fact]
    public void AuditLogQuery_NonPositiveLimit_ClampsToHundred()
    {
        // [task_087] Defensive: zero/negative Limit must not blow up the SQL query.
        Assert.Equal(100, new AuditLogQuery(Limit: 0).EffectiveLimit);
        Assert.Equal(100, new AuditLogQuery(Limit: -1).EffectiveLimit);
        Assert.Equal(50,  new AuditLogQuery(Limit: 50).EffectiveLimit);
    }
}
