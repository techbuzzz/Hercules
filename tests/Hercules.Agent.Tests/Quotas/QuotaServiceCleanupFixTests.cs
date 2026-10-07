using Hercules.Config;
using Hercules.Quotas;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Quotas;

/// <summary>
///     Regression tests для task_072 — fixes вокруг QuotaService:
///     * bucket не растёт неограниченно после periodic sweep (был ConcurrentBag,
///       который никогда не чистился из-за dummy-key swap);
///     * MakeStatus возвращает реальный QuotaScope, а не хардкод QuotaScope.Agent;
///     * DistributedQuotaService корректно зовёт IMeshStateStore.IncrementAsync
///       и использует distributed counter в GetRateLimitInfo.
/// </summary>
public class QuotaServiceCleanupFixTests
{
    private static QuotasConfig MakeConfig() => new()
    {
        Enabled = true,
        MaxConcurrentRequestsPerAgent = 5,
        MaxCallsPerMinutePerAgent = 100,
        MaxTokensPerDayPerAgent = 1_000_000,
        MaxCallsPerMinutePerSkill = 30,
        MaxConcurrentPerSkill = 5,
        MaxRequestsPerMinutePerUser = 20,
        MaxRequestsPerDayPerUser = 500,
        MaxCallsPerMinutePerTenant = 1000,
        MaxCostPerDayPerTenantUsd = 100m,
        RateLimitWindowSeconds = 60
    };

    [Fact]
    public void GetStatus_SkillScope_ReportsSkillScope()
    {
        // bug: MakeStatus hardcoded QuotaScope.Agent for every entry, so
        // GetStatus(QuotaScope.Skill, ...) returned statuses with Scope=Agent.
        var cfg = MakeConfig();
        var logger = new Mock<ILogger<QuotaService>>().Object;
        var sut = new QuotaService(cfg, logger);

        var statuses = sut.GetStatus(QuotaScope.Skill, "skill-1");

        Assert.NotEmpty(statuses);
        Assert.All(statuses, s => Assert.Equal(QuotaScope.Skill, s.Scope));
    }

    [Fact]
    public void GetStatus_UserScope_ReportsUserScope()
    {
        var cfg = MakeConfig();
        var logger = new Mock<ILogger<QuotaService>>().Object;
        var sut = new QuotaService(cfg, logger);

        var statuses = sut.GetStatus(QuotaScope.User, "user-1");

        Assert.NotEmpty(statuses);
        Assert.All(statuses, s => Assert.Equal(QuotaScope.User, s.Scope));
    }

    [Fact]
    public void GetStatus_TenantScope_ReportsTenantScope()
    {
        var cfg = MakeConfig();
        var logger = new Mock<ILogger<QuotaService>>().Object;
        var sut = new QuotaService(cfg, logger);

        var statuses = sut.GetStatus(QuotaScope.Tenant, "tenant-1");

        Assert.NotEmpty(statuses);
        Assert.All(statuses, s => Assert.Equal(QuotaScope.Tenant, s.Scope));
    }

    [Fact]
    public void GetStatus_AgentScope_ReportsAgentScope()
    {
        var cfg = MakeConfig();
        var logger = new Mock<ILogger<QuotaService>>().Object;
        var sut = new QuotaService(cfg, logger);

        var statuses = sut.GetStatus(QuotaScope.Agent, "agent-1");

        Assert.NotEmpty(statuses);
        Assert.All(statuses, s => Assert.Equal(QuotaScope.Agent, s.Scope));
    }

    [Fact]
    public void RecordUsage_AfterManyInserts_AndSweep_BucketStaysBounded()
    {
        // Replays the original bug: 1000 inserts + sweep should keep the bucket bounded
        // by the number of still-in-window events, not the number of total inserts.
        var cfg = MakeConfig();
        cfg.RateLimitWindowSeconds = 1; // tight window so the periodic sweep prunes almost everything
        var logger = new Mock<ILogger<QuotaService>>().Object;
        var sut = new QuotaService(cfg, logger);

        for (int i = 0; i < 1000; i++)
        {
            sut.RecordUsage(QuotaScope.Agent, "agent-1", QuotaLimitType.CallsPerMinutePerAgent, 1);
        }

        // Wait so the entries fall outside the 1-second window.
        Thread.Sleep(1100);

        var swept = sut.SweepAllBuckets();
        var status = sut.GetStatus(QuotaScope.Agent, "agent-1", QuotaLimitType.CallsPerMinutePerAgent);

        Assert.NotNull(status);
        // After the window expires and we sweep, the bucket should be empty (or near-empty).
        Assert.True(status.Current <= 1,
            $"Bucket did not get pruned: {status.Current} entries still present after sweep ({swept} swept)");
    }

    [Fact]
    public void SweepAllBuckets_ReturnsNonZero_AfterExpiry()
    {
        var cfg = MakeConfig();
        cfg.RateLimitWindowSeconds = 1;
        var logger = new Mock<ILogger<QuotaService>>().Object;
        var sut = new QuotaService(cfg, logger);

        for (int i = 0; i < 10; i++)
        {
            sut.RecordUsage(QuotaScope.Agent, "agent-1", QuotaLimitType.CallsPerMinutePerAgent, 1);
        }

        Thread.Sleep(1100);
        var swept = sut.SweepAllBuckets();
        Assert.True(swept >= 1, $"Expected at least 1 swept entry, got {swept}");
    }

    [Fact]
    public void SweepAllBuckets_NoExpiry_ReturnsZero()
    {
        var cfg = MakeConfig();
        cfg.RateLimitWindowSeconds = 60; // wide window
        var logger = new Mock<ILogger<QuotaService>>().Object;
        var sut = new QuotaService(cfg, logger);

        for (int i = 0; i < 5; i++)
        {
            sut.RecordUsage(QuotaScope.Agent, "agent-1", QuotaLimitType.CallsPerMinutePerAgent, 1);
        }

        var swept = sut.SweepAllBuckets();
        Assert.Equal(0, swept);
    }
}
