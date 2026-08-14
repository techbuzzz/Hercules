using Hercules.Config;
using Hercules.Quotas;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Quotas;

/// <summary>
///     Тесты QuotaService: CheckQuotas, RecordUsage, Begin/EndConcurrency, GetStatus.
/// </summary>
public class QuotaServiceTests
{
    private readonly QuotasConfig _cfg;
    private readonly Mock<ILogger<QuotaService>> _loggerMock;
    private readonly QuotaService _service;

    public QuotaServiceTests()
    {
        _cfg = new QuotasConfig
        {
            Enabled = true,
            MaxConcurrentRequestsPerAgent = 5,
            MaxCallsPerMinutePerAgent = 100,
            MaxTokensPerDayPerAgent = 1000000,
            MaxStorageMbPerAgent = 500,
            MaxMessagesPerDayPerAgent = 10000,
            MaxCallsPerMinutePerSkill = 30,
            MaxConcurrentPerSkill = 5,
            MaxRequestsPerMinutePerUser = 20,
            MaxRequestsPerDayPerUser = 500,
            MaxAgentsPerTenant = 50,
            MaxCallsPerMinutePerTenant = 1000,
            MaxCostPerDayPerTenantUsd = 100,
            RateLimitWindowSeconds = 60
        };
        _loggerMock = new Mock<ILogger<QuotaService>>();
        _service = new QuotaService(_cfg, _loggerMock.Object);
    }

    [Fact]
    public void CheckQuotas_WhenDisabled_ReturnsEmpty()
    {
        var disabledService = new QuotaService(new QuotasConfig { Enabled = false }, _loggerMock.Object);
        var result = disabledService.CheckQuotas(QuotaScope.Agent, "hercules");
        Assert.False(result.HasHardViolation);
        Assert.False(result.HasSoftWarning);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void CheckQuotas_WhenNoUsage_ReturnsNoViolations()
    {
        var result = _service.CheckQuotas(QuotaScope.Agent, "hercules");
        Assert.False(result.HasHardViolation);
        Assert.False(result.HasSoftWarning);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void BeginConcurrency_IncrementsCounter()
    {
        _service.BeginConcurrency(QuotaScope.Agent, "hercules");
        var status = _service.GetStatus(QuotaScope.Agent, "hercules", QuotaLimitType.ConcurrentRequestsPerAgent);
        Assert.NotNull(status);
        Assert.Equal(1, status.Current);
    }

    [Fact]
    public void EndConcurrency_DecrementsCounter()
    {
        _service.BeginConcurrency(QuotaScope.Agent, "hercules");
        _service.BeginConcurrency(QuotaScope.Agent, "hercules");
        _service.EndConcurrency(QuotaScope.Agent, "hercules");
        var status = _service.GetStatus(QuotaScope.Agent, "hercules", QuotaLimitType.ConcurrentRequestsPerAgent);
        Assert.NotNull(status);
        Assert.Equal(1, status.Current);
    }

    [Fact]
    public void CheckQuotas_ExceedsConcurrentLimit_ReturnsHardViolation()
    {
        // Begin one more than allowed
        for (int i = 0; i < _cfg.MaxConcurrentRequestsPerAgent; i++)
        {
            _service.BeginConcurrency(QuotaScope.Agent, "hercules");
        }
        // Now we're at the limit - add one more to exceed
        _service.BeginConcurrency(QuotaScope.Agent, "hercules");

        var result = _service.CheckQuotas(QuotaScope.Agent, "hercules");

        Assert.True(result.HasHardViolation);
        var violation = result.Violations.FirstOrDefault(v => v.Type == QuotaLimitType.ConcurrentRequestsPerAgent);
        Assert.NotNull(violation);
        Assert.Equal("hard_cap", violation.EnforcementMode);
    }

    [Fact]
    public void RecordUsage_UpdatesCounters()
    {
        _service.RecordUsage(QuotaScope.Agent, "hercules", QuotaLimitType.TokensPerDayPerAgent, 1000);

        var counters = _service.GetCounters(QuotaScope.Agent, "hercules");
        Assert.Equal(1000, counters.TokensUsedToday);
    }

    [Fact]
    public void RecordUsage_Accumulates()
    {
        _service.RecordUsage(QuotaScope.Agent, "hercules", QuotaLimitType.TokensPerDayPerAgent, 500);
        _service.RecordUsage(QuotaScope.Agent, "hercules", QuotaLimitType.TokensPerDayPerAgent, 300);

        var counters = _service.GetCounters(QuotaScope.Agent, "hercules");
        Assert.Equal(800, counters.TokensUsedToday);
    }

    [Fact]
    public void CheckQuotas_ExceedsTokensLimit_ReturnsSoftViolation()
    {
        // Simulate exceeding tokens per day
        _service.RecordUsage(QuotaScope.Agent, "hercules", QuotaLimitType.TokensPerDayPerAgent, _cfg.MaxTokensPerDayPerAgent + 1);

        var result = _service.CheckQuotas(QuotaScope.Agent, "hercules");

        Assert.True(result.HasSoftWarning);
        var violation = result.Violations.FirstOrDefault(v => v.Type == QuotaLimitType.TokensPerDayPerAgent);
        Assert.NotNull(violation);
        Assert.Equal("soft_warn", violation.EnforcementMode);
    }

    [Fact]
    public void GetStatus_ReturnsCorrectLimits()
    {
        var statuses = _service.GetStatus(QuotaScope.Agent, "hercules");

        Assert.Contains(statuses, s => s.Type == QuotaLimitType.ConcurrentRequestsPerAgent);
        Assert.Contains(statuses, s => s.Type == QuotaLimitType.CallsPerMinutePerAgent);
        Assert.Contains(statuses, s => s.Type == QuotaLimitType.TokensPerDayPerAgent);
        Assert.Contains(statuses, s => s.Type == QuotaLimitType.StorageMbPerAgent);
        Assert.Contains(statuses, s => s.Type == QuotaLimitType.MessagesPerDayPerAgent);
    }

    [Fact]
    public void GetRateLimitInfo_ReturnsCorrectInfo()
    {
        var info = _service.GetRateLimitInfo(QuotaScope.Agent, "hercules", QuotaLimitType.CallsPerMinutePerAgent);

        Assert.NotNull(info);
        Assert.Equal(_cfg.MaxCallsPerMinutePerAgent.ToString(), info.LimitHeader);
    }

    [Fact]
    public void GetCounters_ReturnsCounters()
    {
        var counters = _service.GetCounters(QuotaScope.Agent, "hercules");
        Assert.NotNull(counters);
    }

    [Fact]
    public void ResetDailyCounters_ClearsCounters()
    {
        _service.RecordUsage(QuotaScope.Agent, "hercules", QuotaLimitType.TokensPerDayPerAgent, 1000);
        _service.ResetDailyCounters(QuotaScope.Agent, "hercules");

        var counters = _service.GetCounters(QuotaScope.Agent, "hercules");
        Assert.Equal(0, counters.TokensUsedToday);
    }

    // Skill scope tests
    [Fact]
    public void CheckQuotas_SkillScope_ReturnsSkillLimits()
    {
        var statuses = _service.GetStatus(QuotaScope.Skill, "my-skill");

        Assert.Contains(statuses, s => s.Type == QuotaLimitType.CallsPerMinutePerSkill);
        Assert.Contains(statuses, s => s.Type == QuotaLimitType.ConcurrentPerSkill);
    }

    // User scope tests
    [Fact]
    public void CheckQuotas_UserScope_ReturnsUserLimits()
    {
        var statuses = _service.GetStatus(QuotaScope.User, "user-123");

        Assert.Contains(statuses, s => s.Type == QuotaLimitType.RequestsPerMinutePerUser);
        Assert.Contains(statuses, s => s.Type == QuotaLimitType.RequestsPerDayPerUser);
    }

    // Tenant scope tests
    [Fact]
    public void CheckQuotas_TenantScope_ReturnsTenantLimits()
    {
        var statuses = _service.GetStatus(QuotaScope.Tenant, "tenant-abc");

        Assert.Contains(statuses, s => s.Type == QuotaLimitType.CallsPerMinutePerTenant);
        Assert.Contains(statuses, s => s.Type == QuotaLimitType.CostPerDayPerTenant);
    }

    [Fact]
    public void RecordUsage_CostPerTenant_TracksCents()
    {
        _service.RecordUsage(QuotaScope.Tenant, "tenant-abc", QuotaLimitType.CostPerDayPerTenant, 1500); // 15 dollars in cents

        var counters = _service.GetCounters(QuotaScope.Tenant, "tenant-abc");
        Assert.Equal(1500, counters.CostUsedTodayCents);
    }

    [Fact]
    public void BeginConcurrency_SkillScope_IncrementsSkillExecutions()
    {
        _service.BeginConcurrency(QuotaScope.Skill, "my-skill");

        var counters = _service.GetCounters(QuotaScope.Skill, "my-skill");
        Assert.Equal(1, counters.ActiveSkillExecutions);
    }

    [Fact]
    public void EndConcurrency_SkillScope_DecrementsSkillExecutions()
    {
        _service.BeginConcurrency(QuotaScope.Skill, "my-skill");
        _service.BeginConcurrency(QuotaScope.Skill, "my-skill");
        _service.EndConcurrency(QuotaScope.Skill, "my-skill");

        var counters = _service.GetCounters(QuotaScope.Skill, "my-skill");
        Assert.Equal(1, counters.ActiveSkillExecutions);
    }
}
