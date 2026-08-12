using Hercules.Audit;
using Hercules.Config;
using Hercules.Redaction;
using Hercules.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Audit;

/// <summary>
///     Тесты AuditService: LogToolPolicyDecision, LogToolExecution, Query, actor filtering.
/// </summary>
public class AuditServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteSessionStore _store;
    private readonly AuditLogService _auditLog;
    private readonly Mock<ILogger<AuditService>> _auditLoggerMock;
    private readonly Mock<ILogger<RedactionService>> _redactionLoggerMock;
    private readonly AuditService _svc;

    public AuditServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-audit-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new SqliteSessionStore(new StorageConfig { DataRoot = _tempDir });
        _auditLog = new AuditLogService(_store);
        _auditLoggerMock = new Mock<ILogger<AuditService>>();
        _redactionLoggerMock = new Mock<ILogger<RedactionService>>();

        var cfg = new AuditConfig { Enabled = true, PayloadHashEnabled = true };
        var hashSvc = new PayloadHashService();
        var redactionSvc = new RedactionService(cfg, _redactionLoggerMock.Object);
        _svc = new AuditService(_auditLog, cfg, redactionSvc, hashSvc, _auditLoggerMock.Object);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task LogToolPolicyDecision_WritesToAuditLog()
    {
        await _svc.LogToolPolicyDecisionAsync(
            "system", "http_fetch", "Allowed", "Network",
            "{\"url\":\"https://api.example.com\"}", "s1");

        var entries = await _svc.QueryAsync(limit: 10);
        Assert.NotEmpty(entries);
        var entry = entries[0];
        Assert.Equal("system", entry.Actor);
        Assert.Equal("tool_policy_decision", entry.Action);
        Assert.Equal("http_fetch", entry.Target);
        Assert.Equal("Allowed", entry.PolicyDecision);
        Assert.Equal("Network", entry.PermissionUsed);
        Assert.Equal("success", entry.Result);
    }

    [Fact]
    public async Task LogToolPolicyDecision_DeniedDecision_RecordsBlockedResult()
    {
        await _svc.LogToolPolicyDecisionAsync(
            "system", "delete_file", "Denied", "Delete",
            "{\"path\":\"/etc/passwd\"}", "s1");

        var entries = await _svc.QueryAsync(limit: 10);
        Assert.Equal("blocked", entries[0].Result);
    }

    [Fact]
    public async Task LogToolExecution_RecordsSuccess()
    {
        await _svc.LogToolExecutionAsync(
            "agent", "http_fetch", "success", null, "s1");

        var entries = await _svc.QueryAsync(limit: 10);
        Assert.Equal("tool_executed", entries[0].Action);
        Assert.Equal("success", entries[0].Result);
    }

    [Fact]
    public async Task LogToolExecution_RecordsError()
    {
        await _svc.LogToolExecutionAsync(
            "agent", "http_fetch", "failure", "Connection refused", "s1");

        var entries = await _svc.QueryAsync(limit: 10);
        Assert.Equal("failure", entries[0].Result);
        Assert.NotNull(entries[0].Details);
    }

    [Fact]
    public async Task LogSkillAction_RecordsAction()
    {
        await _svc.LogSkillActionAsync("agent", "created", "skill-abc", null, "s1");

        var entries = await _svc.QueryAsync(limit: 10);
        Assert.Equal("agent", entries[0].Actor);
        Assert.Equal("skill_created", entries[0].Action);
        Assert.Equal("skill-abc", entries[0].Target);
    }

    [Fact]
    public async Task LogConfigChange_RecordsAction()
    {
        await _svc.LogConfigChangeAsync("user", "Llm.Provider", "yandexgpt", "ollama-local", "s1");

        var entries = await _svc.QueryAsync(limit: 10);
        Assert.Equal("config_changed", entries[0].Action);
        Assert.Equal("Llm.Provider", entries[0].Target);
    }

    [Fact]
    public async Task LogApprovalAction_RecordsApproval()
    {
        await _svc.LogApprovalActionAsync("user", "req-123", "approved", "http_fetch", "Approved by user", "s1");

        var entries = await _svc.QueryAsync(limit: 10);
        Assert.Equal("approval_approved", entries[0].Action);
        Assert.Equal("req-123", entries[0].Target);
    }

    [Fact]
    public async Task LogToolPolicyDecision_ComputesPayloadHash()
    {
        await _svc.LogToolPolicyDecisionAsync(
            "system", "http_fetch", "Allowed", "Network", "{\"url\":\"https://api.example.com\"}", "s1");

        var entries = await _svc.QueryAsync(limit: 10);
        // Hash should be non-empty when payload is non-empty
        Assert.NotNull(entries[0].PayloadHash);
        Assert.NotEmpty(entries[0].PayloadHash!);
    }

    [Fact]
    public async Task DisabledAudit_SkipsLogging()
    {
        var disabledCfg = new AuditConfig { Enabled = false };
        var hashSvc = new PayloadHashService();
        var svc = new AuditService(_auditLog, disabledCfg, null, hashSvc, _auditLoggerMock.Object);

        await svc.LogToolPolicyDecisionAsync(
            "system", "http_fetch", "Allowed", "Network", "{\"url\":\"x\"}", "s1");

        var entries = await _svc.QueryAsync(limit: 10);
        Assert.Empty(entries);
    }

    [Fact]
    public async Task ActorFilter_ExcludesUnwantedActors()
    {
        var cfg = new AuditConfig { Enabled = true, LogActorActions = new List<string> { "agent" } };
        var hashSvc = new PayloadHashService();
        var svc = new AuditService(_auditLog, cfg, null, hashSvc, _auditLoggerMock.Object);

        await svc.LogAsync("user", "some_action", null, null, "s1");
        await svc.LogAsync("agent", "agent_action", null, null, "s1");

        var entries = await svc.QueryAsync(limit: 10);
        Assert.Single(entries);
        Assert.Equal("agent", entries[0].Actor);
    }

    [Fact]
    public async Task LogToolPolicyDecision_RedactsApiKeyInArgs()
    {
        var cfg = new AuditConfig { Enabled = true };
        var hashSvc = new PayloadHashService();
        var redactionSvc = new RedactionService(cfg, _redactionLoggerMock.Object);
        var svc = new AuditService(_auditLog, cfg, redactionSvc, hashSvc, _auditLoggerMock.Object);

        await svc.LogToolPolicyDecisionAsync(
            "system", "api_tool", "Allowed", "Network",
            "Bearer sk-1234567890abcdefghijklmnop", "s1");

        var entries = await svc.QueryAsync(limit: 10);
        // Bearer token should be redacted to "Bearer ***"
        Assert.DoesNotContain("sk-1234567890", entries[0].Details!);
    }

    [Fact]
    public async Task LogToolPolicyDecision_Denied_RedactsArgs()
    {
        var cfg = new AuditConfig { Enabled = true };
        var hashSvc = new PayloadHashService();
        var redactionSvc = new RedactionService(cfg, _redactionLoggerMock.Object);
        var svc = new AuditService(_auditLog, cfg, redactionSvc, hashSvc, _auditLoggerMock.Object);

        // Uses secret_ prefix which matches the API key regex \b(sk|pk|token|secret|key|auth)[_-][a-zA-Z0-9]{10,}
        await svc.LogToolPolicyDecisionAsync(
            "system", "dangerous_tool", "Denied", null,
            "secret_verysecretvalue123", "s1");

        var entries = await svc.QueryAsync(limit: 10);
        // The API key pattern matches "secret_verysecretvalue123"
        Assert.DoesNotContain("verysecretvalue123", entries[0].Details!);
    }
}
