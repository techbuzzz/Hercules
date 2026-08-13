using Hercules.Budget;
using Hercules.Config;
using Hercules.Mesh.Schema;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Phase4Tests;

/// <summary>
///     Tests for <see cref="DelegationBoundaryService"/> — hop count, fan-out width,
///     cumulative tool calls, cost, and wall-clock time limits.
///     Specification: docs/ROADMAP-RU.md Phase 4 task_048.
/// </summary>
public class DelegationBoundaryTests
{
    private readonly Mock<ILogger<DelegationBoundaryService>> _loggerMock;
    private readonly DelegationBoundaryConfig _defaultConfig;

    public DelegationBoundaryTests()
    {
        _loggerMock = new Mock<ILogger<DelegationBoundaryService>>();
        _defaultConfig = new DelegationBoundaryConfig
        {
            Enabled = true,
            EnforcementMode = "SoftWarn",
            MaxHopCount = 3,
            MaxFanOutWidth = 5,
            MaxCumulativeToolCalls = 50,
            MaxCumulativeCostUsd = 5.00m,
            MaxCumulativeWallClockMs = 300_000
        };
    }

    private DelegationBoundaryService CreateService(DelegationBoundaryConfig? config = null)
        => new(config ?? _defaultConfig, _loggerMock.Object);

    // --- CheckOutgoing: Enabled=false ---

    [Fact]
    public void CheckOutgoing_WhenDisabled_ReturnsOk()
    {
        var svc = CreateService(new DelegationBoundaryConfig { Enabled = false });

        var result = svc.CheckOutgoing("agent1", fanOutWidth: 10,
            estimatedCostUsd: 100m, estimatedToolCalls: 100, estimatedWallClockMs: 1_000_000,
            auth: null, rootRequestId: null);

        Assert.True(result.IsAllowed);
        Assert.Empty(result.Violations);
    }

    // --- CheckOutgoing: Hop count ---

    [Fact]
    public void CheckOutgoing_WhenHopCountBelowLimit_ReturnsOk()
    {
        var svc = CreateService();
        var auth = AuthContext.Bearer("tok", delegationDepth: 2);

        var result = svc.CheckOutgoing("agent1", fanOutWidth: 1,
            estimatedCostUsd: 0.01m, estimatedToolCalls: 1, estimatedWallClockMs: 1000,
            auth: auth, rootRequestId: "req1");

        Assert.True(result.IsAllowed);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void CheckOutgoing_WhenHopCountAtLimit_ReturnsOk()
    {
        var svc = CreateService(); // MaxHopCount=3
        var auth = AuthContext.Bearer("tok", delegationDepth: 2); // next=3, within limit

        var result = svc.CheckOutgoing("agent1", fanOutWidth: 1,
            estimatedCostUsd: 0.01m, estimatedToolCalls: 1, estimatedWallClockMs: 1000,
            auth: auth, rootRequestId: "req1");

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void CheckOutgoing_WhenHopCountExceedsLimit_SoftWarn_Allows()
    {
        var svc = CreateService(new DelegationBoundaryConfig { Enabled = true, EnforcementMode = "SoftWarn", MaxHopCount = 3 });
        var auth = AuthContext.Bearer("tok", delegationDepth: 3); // next=4, exceeds

        var result = svc.CheckOutgoing("agent1", fanOutWidth: 1,
            estimatedCostUsd: 0.01m, estimatedToolCalls: 1, estimatedWallClockMs: 1000,
            auth: auth, rootRequestId: "req1");

        Assert.True(result.IsAllowed); // SoftWarn: allowed with warning
        Assert.Single(result.Violations);
        Assert.Equal(DelegationBoundaryType.HopCount, result.Violations[0].Type);
    }

    [Fact]
    public void CheckOutgoing_WhenHopCountExceedsLimit_HardCap_Blocks()
    {
        var svc = CreateService(new DelegationBoundaryConfig { Enabled = true, EnforcementMode = "HardCap", MaxHopCount = 3 });
        var auth = AuthContext.Bearer("tok", delegationDepth: 3); // next=4, exceeds

        var result = svc.CheckOutgoing("agent1", fanOutWidth: 1,
            estimatedCostUsd: 0.01m, estimatedToolCalls: 1, estimatedWallClockMs: 1000,
            auth: auth, rootRequestId: "req1");

        Assert.False(result.IsAllowed);
        Assert.True(result.IsHardViolation);
        Assert.Single(result.Violations);
    }

    // --- CheckOutgoing: Fan-out width ---

    [Fact]
    public void CheckOutgoing_WhenFanOutWidthBelowLimit_ReturnsOk()
    {
        var svc = CreateService(); // MaxFanOutWidth=5

        var result = svc.CheckOutgoing("agent1", fanOutWidth: 3,
            estimatedCostUsd: 0.01m, estimatedToolCalls: 1, estimatedWallClockMs: 1000,
            auth: null, rootRequestId: null);

        Assert.True(result.IsAllowed);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void CheckOutgoing_WhenFanOutWidthExceedsLimit_HardCap_Blocks()
    {
        var svc = CreateService(new DelegationBoundaryConfig { Enabled = true, EnforcementMode = "HardCap", MaxFanOutWidth = 5 });

        var result = svc.CheckOutgoing("agent1", fanOutWidth: 10,
            estimatedCostUsd: 0.01m, estimatedToolCalls: 1, estimatedWallClockMs: 1000,
            auth: null, rootRequestId: null);

        Assert.False(result.IsAllowed);
        Assert.True(result.IsHardViolation);
        Assert.Single(result.Violations);
        Assert.Equal(DelegationBoundaryType.FanOutWidth, result.Violations[0].Type);
    }

    // --- CheckOutgoing: Cumulative cost ---

    [Fact]
    public void CheckOutgoing_WhenCumulativeCostBelowLimit_ReturnsOk()
    {
        var svc = CreateService(); // MaxCumulativeCostUsd=5.00
        var auth = AuthContext.Bearer("tok", rootRequestId: "req1");
        svc.RecordHopCompletion("agent1", 5, 1.00m, 5000, "req1");

        var result = svc.CheckOutgoing("agent2", fanOutWidth: 1,
            estimatedCostUsd: 2.00m, estimatedToolCalls: 5, estimatedWallClockMs: 1000,
            auth: auth, rootRequestId: "req1");

        Assert.True(result.IsAllowed); // 1.00 + 2.00 = 3.00 < 5.00
    }

    [Fact]
    public void CheckOutgoing_WhenCumulativeCostExceedsLimit_HardCap_Blocks()
    {
        var svc = CreateService(new DelegationBoundaryConfig { Enabled = true, EnforcementMode = "HardCap", MaxCumulativeCostUsd = 5.00m });
        var auth = AuthContext.Bearer("tok", rootRequestId: "req1");
        svc.RecordHopCompletion("agent1", 5, 3.00m, 5000, "req1");

        var result = svc.CheckOutgoing("agent2", fanOutWidth: 1,
            estimatedCostUsd: 3.00m, estimatedToolCalls: 5, estimatedWallClockMs: 1000,
            auth: auth, rootRequestId: "req1");

        Assert.False(result.IsAllowed); // 3.00 + 3.00 = 6.00 > 5.00
    }

    // --- CheckOutgoing: Cumulative tool calls ---

    [Fact]
    public void CheckOutgoing_WhenCumulativeToolCallsBelowLimit_ReturnsOk()
    {
        var svc = CreateService(); // MaxCumulativeToolCalls=50
        var auth = AuthContext.Bearer("tok", rootRequestId: "req1");
        svc.RecordHopCompletion("agent1", 30, 0.50m, 5000, "req1");

        var result = svc.CheckOutgoing("agent2", fanOutWidth: 1,
            estimatedCostUsd: 0.01m, estimatedToolCalls: 10, estimatedWallClockMs: 1000,
            auth: auth, rootRequestId: "req1");

        Assert.True(result.IsAllowed); // 30 + 10 = 40 < 50
    }

    [Fact]
    public void CheckOutgoing_WhenCumulativeToolCallsExceedsLimit_HardCap_Blocks()
    {
        var svc = CreateService(new DelegationBoundaryConfig { Enabled = true, EnforcementMode = "HardCap", MaxCumulativeToolCalls = 50 });
        var auth = AuthContext.Bearer("tok", rootRequestId: "req1");
        svc.RecordHopCompletion("agent1", 40, 0.50m, 5000, "req1");

        var result = svc.CheckOutgoing("agent2", fanOutWidth: 1,
            estimatedCostUsd: 0.01m, estimatedToolCalls: 15, estimatedWallClockMs: 1000,
            auth: auth, rootRequestId: "req1");

        Assert.False(result.IsAllowed); // 40 + 15 = 55 > 50
    }

    // --- CheckOutgoing: Cumulative wall-clock ---

    [Fact]
    public void CheckOutgoing_WhenCumulativeWallClockBelowLimit_ReturnsOk()
    {
        var svc = CreateService(); // MaxCumulativeWallClockMs=300_000
        var auth = AuthContext.Bearer("tok", rootRequestId: "req1");
        svc.RecordHopCompletion("agent1", 5, 0.50m, 100_000, "req1");

        var result = svc.CheckOutgoing("agent2", fanOutWidth: 1,
            estimatedCostUsd: 0.01m, estimatedToolCalls: 5, estimatedWallClockMs: 100_000,
            auth: auth, rootRequestId: "req1");

        Assert.True(result.IsAllowed); // 100000 + 100000 = 200000 < 300000
    }

    [Fact]
    public void CheckOutgoing_WhenCumulativeWallClockExceedsLimit_HardCap_Blocks()
    {
        var svc = CreateService(new DelegationBoundaryConfig { Enabled = true, EnforcementMode = "HardCap", MaxCumulativeWallClockMs = 300_000 });
        var auth = AuthContext.Bearer("tok", rootRequestId: "req1");
        svc.RecordHopCompletion("agent1", 5, 0.50m, 250_000, "req1");

        var result = svc.CheckOutgoing("agent2", fanOutWidth: 1,
            estimatedCostUsd: 0.01m, estimatedToolCalls: 5, estimatedWallClockMs: 100_000,
            auth: auth, rootRequestId: "req1");

        Assert.False(result.IsAllowed); // 250000 + 100000 = 350000 > 300000
    }

    // --- CheckIncoming ---

    [Fact]
    public void CheckIncoming_WhenDisabled_ReturnsOk()
    {
        var svc = CreateService(new DelegationBoundaryConfig { Enabled = false });
        var auth = AuthContext.Bearer("tok", delegationDepth: 10);

        var result = svc.CheckIncoming(auth);

        Assert.True(result.IsAllowed);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void CheckIncoming_WhenHopCountExceedsLimit_HardCap_Rejects()
    {
        var svc = CreateService(new DelegationBoundaryConfig { Enabled = true, EnforcementMode = "HardCap", MaxHopCount = 3 });
        var auth = AuthContext.Bearer("tok", delegationDepth: 5); // exceeds 3

        var result = svc.CheckIncoming(auth);

        Assert.False(result.IsAllowed);
        Assert.Single(result.Violations);
        Assert.Equal(DelegationBoundaryType.HopCount, result.Violations[0].Type);
    }

    [Fact]
    public void CheckIncoming_WhenCumulativeCostExceeded_HardCap_Rejects()
    {
        var svc = CreateService(new DelegationBoundaryConfig { Enabled = true, EnforcementMode = "HardCap", MaxCumulativeCostUsd = 2.00m });
        var auth = AuthContext.Bearer("tok", delegationDepth: 1, rootRequestId: "req1");
        svc.RecordHopCompletion("agent1", 10, 3.00m, 5000, "req1"); // exceeds limit

        var result = svc.CheckIncoming(auth);

        Assert.False(result.IsAllowed);
    }

    // --- RecordHopCompletion ---

    [Fact]
    public void RecordHopCompletion_UpdatesChainContext()
    {
        var svc = CreateService();

        svc.RecordHopCompletion("agent1", 10, 0.50m, 5000, "req1");
        svc.RecordHopCompletion("agent2", 5, 0.25m, 3000, "req1");

        var ctx = svc.GetChainContext("req1");
        Assert.NotNull(ctx);
        Assert.Equal(2, ctx.HopCount);
        Assert.Equal(15, ctx.CumulativeToolCalls);
        Assert.Equal(0.75m, ctx.CumulativeCostUsd);
        Assert.Equal(8000, ctx.CumulativeWallClockMs);
        Assert.Contains("agent1", ctx.AgentChain);
        Assert.Contains("agent2", ctx.AgentChain);
    }

    [Fact]
    public void RecordHopCompletion_WhenDisabled_DoesNotThrow()
    {
        var svc = CreateService(new DelegationBoundaryConfig { Enabled = false });

        var ex = Record.Exception(() => svc.RecordHopCompletion("agent1", 10, 0.50m, 5000, "req1"));
        Assert.Null(ex);
    }

    // --- CreateNextHopAuthContext ---

    [Fact]
    public void CreateNextHopAuthContext_IncrementsDepth()
    {
        var svc = CreateService();
        var auth = AuthContext.Bearer("tok", delegationDepth: 1, rootRequestId: "root-req");

        var next = svc.CreateNextHopAuthContext(auth, "root-req");

        Assert.Equal(2, next.DelegationDepth);
        Assert.Equal("root-req", next.RootRequestId);
    }

    [Fact]
    public void CreateNextHopAuthContext_WithNoAuth_CreatesDepthZero()
    {
        var svc = CreateService();

        var next = svc.CreateNextHopAuthContext(null, "root-req");

        Assert.Equal(0, next.DelegationDepth);
        Assert.Equal("root-req", next.RootRequestId);
    }

    // --- CreateRejectionResponse ---

    [Fact]
    public void CreateRejectionResponse_FormatsReasonFromViolations()
    {
        var svc = CreateService();
        var violations = new List<DelegationBoundaryViolation>
        {
            new(DelegationBoundaryType.HopCount, 3, 5, "Hop count 5 exceeds max 3"),
            new(DelegationBoundaryType.FanOutWidth, 5, 10, "Fan-out width 10 exceeds max 5")
        };
        var result = new DelegationBoundaryCheckResult(IsAllowed: false, IsHardViolation: true, Violations: violations);

        var response = svc.CreateRejectionResponse("req1", "agent1", result, "trace1");

        Assert.Equal("rejected", response.Status);
        Assert.Equal("req1", response.RequestId);
        Assert.Equal("agent1", response.Agent);
        Assert.Contains("Hop count", response.Error);
        Assert.Contains("Fan-out width", response.Error);
        Assert.Equal("trace1", response.TraceId);
    }

    // --- MaxHopCount=0 means unlimited ---

    [Fact]
    public void CheckOutgoing_WhenMaxHopCountZero_AllowsAnyDepth()
    {
        var svc = CreateService(new DelegationBoundaryConfig { Enabled = true, EnforcementMode = "HardCap", MaxHopCount = 0 });
        var auth = AuthContext.Bearer("tok", delegationDepth: 100);

        var result = svc.CheckOutgoing("agent1", fanOutWidth: 1,
            estimatedCostUsd: 0.01m, estimatedToolCalls: 1, estimatedWallClockMs: 1000,
            auth: auth, rootRequestId: "req1");

        Assert.True(result.IsAllowed);
        Assert.Empty(result.Violations);
    }

    // --- MaxFanOutWidth=0 means unlimited ---

    [Fact]
    public void CheckOutgoing_WhenMaxFanOutWidthZero_AllowsAnyWidth()
    {
        var svc = CreateService(new DelegationBoundaryConfig { Enabled = true, EnforcementMode = "HardCap", MaxFanOutWidth = 0 });

        var result = svc.CheckOutgoing("agent1", fanOutWidth: 1000,
            estimatedCostUsd: 0.01m, estimatedToolCalls: 1, estimatedWallClockMs: 1000,
            auth: null, rootRequestId: null);

        Assert.True(result.IsAllowed);
    }
}
