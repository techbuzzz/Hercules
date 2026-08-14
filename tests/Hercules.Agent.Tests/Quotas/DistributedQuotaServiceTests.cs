using Hercules.Config;
using Hercules.Mesh.Abstractions;
using Hercules.Mesh.InProcess;
using Hercules.Quotas;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Quotas;

/// <summary>
///     Тесты <see cref="DistributedQuotaService"/> — декоратор над <see cref="IQuotaService"/>,
///     который дополнительно пишет rate-limit counters в <see cref="IMeshStateStore"/>.
///     Спецификация: task_072.
/// </summary>
public class DistributedQuotaServiceTests
{
    private static QuotasConfig MakeConfig(bool distributed = true) => new()
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
        RateLimitWindowSeconds = 60,
        DistributedEnabled = distributed,
        DistributedKeyPrefix = "quota:"
    };

    [Fact]
    public void RecordUsage_BumpsDistributedCounter_WhenRateLimitType()
    {
        var cfg = MakeConfig();
        var store = new InProcessMeshStateStore();
        var inner = new QuotaService(cfg, new Mock<ILogger<QuotaService>>().Object);
        var sut = new DistributedQuotaService(
            inner, store, cfg, new Mock<ILogger<DistributedQuotaService>>().Object);

        sut.RecordUsage(QuotaScope.Agent, "agent-1", QuotaLimitType.CallsPerMinutePerAgent, 1);
        sut.RecordUsage(QuotaScope.Agent, "agent-1", QuotaLimitType.CallsPerMinutePerAgent, 1);
        sut.RecordUsage(QuotaScope.Agent, "agent-1", QuotaLimitType.CallsPerMinutePerAgent, 1);

        // IncrementAsync is fire-and-forget — give it a moment to flush.
        var stored = PollUntilAsync(async () =>
            await store.GetAsync("quota:Agent:agent-1:CallsPerMinutePerAgent").ConfigureAwait(false),
            v => v is not null && v.Data == "3").GetAwaiter().GetResult();

        Assert.NotNull(stored);
        Assert.Equal("3", stored!.Data);
    }

    [Fact]
    public void RecordUsage_DoesNotBumpDistributedCounter_ForNonRateLimitType()
    {
        var cfg = MakeConfig();
        var store = new InProcessMeshStateStore();
        var inner = new QuotaService(cfg, new Mock<ILogger<QuotaService>>().Object);
        var sut = new DistributedQuotaService(
            inner, store, cfg, new Mock<ILogger<DistributedQuotaService>>().Object);

        sut.RecordUsage(QuotaScope.Agent, "agent-1", QuotaLimitType.TokensPerDayPerAgent, 1000);

        // Wait briefly to allow any (incorrectly) fired IncrementAsync to complete.
        Thread.Sleep(200);
        var stored = store.GetAsync("quota:Agent:agent-1:TokensPerDayPerAgent").GetAwaiter().GetResult();
        Assert.Null(stored);
    }

    [Fact]
    public void GetRateLimitInfo_ReadsDistributedCounter_AndComputesRemaining()
    {
        var cfg = MakeConfig();
        var store = new InProcessMeshStateStore();
        // Pre-seed the distributed counter at 80 (limit=100) so remaining should be 20.
        store.IncrementAsync("quota:Agent:agent-1:CallsPerMinutePerAgent", 80).GetAwaiter().GetResult();

        var inner = new QuotaService(cfg, new Mock<ILogger<QuotaService>>().Object);
        var sut = new DistributedQuotaService(
            inner, store, cfg, new Mock<ILogger<DistributedQuotaService>>().Object);

        var info = sut.GetRateLimitInfo(QuotaScope.Agent, "agent-1", QuotaLimitType.CallsPerMinutePerAgent);

        Assert.NotNull(info);
        Assert.Equal("100", info!.LimitHeader);
        Assert.Equal(20, info.Remaining);
    }

    [Fact]
    public void CheckQuotas_DelegatedToInner()
    {
        var cfg = MakeConfig();
        var store = new InProcessMeshStateStore();
        var inner = new QuotaService(cfg, new Mock<ILogger<QuotaService>>().Object);
        var sut = new DistributedQuotaService(
            inner, store, cfg, new Mock<ILogger<DistributedQuotaService>>().Object);

        // No usage yet → no violations.
        var result = sut.CheckQuotas(QuotaScope.Agent, "agent-1");
        Assert.False(result.HasHardViolation);
        Assert.False(result.HasSoftWarning);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void Disabled_DoesNotCallStore()
    {
        var cfg = MakeConfig(distributed: false);
        var storeMock = new Mock<IMeshStateStore>();
        storeMock.Setup(s => s.BackendKind).Returns("mock");
        var inner = new QuotaService(cfg, new Mock<ILogger<QuotaService>>().Object);
        var sut = new DistributedQuotaService(
            inner, storeMock.Object, cfg, new Mock<ILogger<DistributedQuotaService>>().Object);

        for (int i = 0; i < 5; i++)
        {
            sut.RecordUsage(QuotaScope.Agent, "agent-1", QuotaLimitType.CallsPerMinutePerAgent, 1);
        }

        Thread.Sleep(200);
        storeMock.Verify(
            s => s.IncrementAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void StoreThrows_FallsBackToLocal()
    {
        var cfg = MakeConfig();
        var storeMock = new Mock<IMeshStateStore>();
        storeMock.Setup(s => s.BackendKind).Returns("mock");
        storeMock
            .Setup(s => s.IncrementAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store down"));

        var inner = new QuotaService(cfg, new Mock<ILogger<QuotaService>>().Object);
        var sut = new DistributedQuotaService(
            inner, storeMock.Object, cfg, new Mock<ILogger<DistributedQuotaService>>().Object);

        // Should not throw even if store is down.
        sut.RecordUsage(QuotaScope.Agent, "agent-1", QuotaLimitType.CallsPerMinutePerAgent, 1);

        // Give the fire-and-forget increment a chance to fail and log.
        Thread.Sleep(300);

        // Local counter should still reflect the usage.
        var status = sut.GetStatus(QuotaScope.Agent, "agent-1", QuotaLimitType.CallsPerMinutePerAgent);
        Assert.NotNull(status);
        Assert.Equal(1, status!.Current);
    }

    private static async Task<T?> PollUntilAsync<T>(Func<Task<T?>> fetch, Func<T?, bool> predicate, int maxAttempts = 50, int delayMs = 20) where T : class
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            var v = await fetch().ConfigureAwait(false);
            if (predicate(v))
                return v;
            await Task.Delay(delayMs).ConfigureAwait(false);
        }
        return await fetch().ConfigureAwait(false);
    }
}
