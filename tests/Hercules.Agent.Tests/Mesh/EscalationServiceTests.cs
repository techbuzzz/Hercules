using Hercules.Config;
using Hercules.Mesh.Escalation;
using Hercules.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Mesh;

public class EscalationServiceTests : IDisposable
{
    private readonly EscalationConfig _config;
    private readonly Mock<ILogger<EscalationService>> _loggerMock;
    private readonly SqliteSessionStore _store;
    private readonly EscalationService _svc;

    public EscalationServiceTests()
    {
        _config = new EscalationConfig { Enabled = true, DefaultTtlMinutes = 30 };
        _loggerMock = new Mock<ILogger<EscalationService>>();
        _store = new SqliteSessionStore(new StorageConfig
        {
            DataRoot = Path.Combine(Path.GetTempPath(), $"hercules_esc_tests_{Guid.NewGuid():N}"),
            SqliteFile = "test.db"
        });
        _svc = new EscalationService(_config, _store, _loggerMock.Object);
    }

    public void Dispose()
    {
        _store.Dispose();
    }

    [Fact]
    public async Task EscalateAsync_CreatesPendingEscalation()
    {
        var ctx = new EscalationContext
        {
            RequestId = "req1",
            AgentId = "hercules",
            SessionId = "sess1",
            Type = EscalationType.LowConfidence,
            Severity = EscalationSeverity.Medium,
            ActionPlan = "Return low-confidence response",
            Context = "Confidence was low",
            ToolOrIntentName = "direct",
            RequestedBy = "agent"
        };

        var result = await _svc.EscalateAsync(ctx);

        Assert.NotEmpty(result.EscalationId);
        Assert.StartsWith("esc_", result.EscalationId);
        Assert.Equal("req1", result.RequestId);
        Assert.Equal("sess1", result.SessionId);
        Assert.Equal(EscalationType.LowConfidence, result.Type);
        Assert.Equal(EscalationSeverity.Medium, result.Severity);
        Assert.Equal(EscalationStatus.Pending, result.Status);
    }

    [Fact]
    public async Task EscalateAsync_Disabled_ReturnsAutoApproved()
    {
        var disabledConfig = new EscalationConfig { Enabled = false };
        var disabled = new EscalationService(disabledConfig, _store, _loggerMock.Object);

        var ctx = new EscalationContext
        {
            RequestId = "req1",
            AgentId = "hercules",
            SessionId = "sess1",
            Type = EscalationType.BudgetExceeded,
            Severity = EscalationSeverity.High,
            ActionPlan = "Block request",
            Context = "Budget exceeded",
            RequestedBy = "agent"
        };

        var result = await disabled.EscalateAsync(ctx);

        Assert.StartsWith("esc_auto_", result.EscalationId);
        Assert.Equal(EscalationStatus.Approved, result.Status);
    }

    [Fact]
    public async Task ApproveAsync_SetsStatusToApproved()
    {
        var ctx = new EscalationContext
        {
            RequestId = "req1",
            AgentId = "hercules",
            SessionId = "sess1",
            Type = EscalationType.PolicyDenial,
            Severity = EscalationSeverity.High,
            ActionPlan = "Block policy violation",
            Context = "Policy denied",
            RequestedBy = "agent"
        };

        var created = await _svc.EscalateAsync(ctx);
        var ok = await _svc.ApproveAsync(created.EscalationId, "operator");

        Assert.True(ok);
        Assert.True(_svc.IsApproved(created.EscalationId));
        Assert.False(_svc.IsDenied(created.EscalationId));
    }

    [Fact]
    public async Task DenyAsync_SetsStatusToDenied()
    {
        var ctx = new EscalationContext
        {
            RequestId = "req2",
            AgentId = "hercules",
            SessionId = "sess1",
            Type = EscalationType.DestructiveOperation,
            Severity = EscalationSeverity.Critical,
            ActionPlan = "Block destructive action",
            Context = "Destructive operation",
            RequestedBy = "agent"
        };

        var created = await _svc.EscalateAsync(ctx);
        var ok = await _svc.DenyAsync(created.EscalationId, "admin");

        Assert.True(ok);
        Assert.True(_svc.IsDenied(created.EscalationId));
        Assert.False(_svc.IsApproved(created.EscalationId));
    }

    [Fact]
    public async Task BatchApproveAsync_ApprovesMultipleEscalations()
    {
        var ctx1 = new EscalationContext
        {
            RequestId = "req3", AgentId = "hercules", SessionId = "sess1",
            Type = EscalationType.LowConfidence, Severity = EscalationSeverity.Low,
            ActionPlan = "a1", Context = "c1", RequestedBy = "agent"
        };
        var ctx2 = new EscalationContext
        {
            RequestId = "req4", AgentId = "hercules", SessionId = "sess1",
            Type = EscalationType.BudgetExceeded, Severity = EscalationSeverity.Medium,
            ActionPlan = "a2", Context = "c2", RequestedBy = "agent"
        };

        var e1 = await _svc.EscalateAsync(ctx1);
        var e2 = await _svc.EscalateAsync(ctx2);

        var count = await _svc.BatchApproveAsync(new[] { e1.EscalationId, e2.EscalationId }, "batch-operator");

        Assert.Equal(2, count);
        Assert.True(_svc.IsApproved(e1.EscalationId));
        Assert.True(_svc.IsApproved(e2.EscalationId));
    }

    [Fact]
    public async Task GetPending_FiltersBySeverity()
    {
        var low = new EscalationContext
        {
            RequestId = "req5", AgentId = "hercules", SessionId = "sess1",
            Type = EscalationType.LowConfidence, Severity = EscalationSeverity.Low,
            ActionPlan = "low", Context = "c", RequestedBy = "agent"
        };
        var high = new EscalationContext
        {
            RequestId = "req6", AgentId = "hercules", SessionId = "sess1",
            Type = EscalationType.PolicyDenial, Severity = EscalationSeverity.High,
            ActionPlan = "high", Context = "c", RequestedBy = "agent"
        };

        await _svc.EscalateAsync(low);
        await _svc.EscalateAsync(high);

        var all = _svc.GetPending("sess1");
        Assert.Equal(2, all.Count);

        var highOnly = _svc.GetPending("sess1", EscalationSeverity.High);
        Assert.Single(highOnly);
        Assert.Equal(EscalationSeverity.High, highOnly[0].Severity);
    }
}
