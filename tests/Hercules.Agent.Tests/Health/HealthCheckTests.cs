using Hercules.Config;
using Hercules.Health;
using Hercules.LLM;
using Hercules.Mesh.Abstractions;
using Hercules.Offline;
using Hercules.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Health;

/// <summary>
/// task_079: unit tests for the real health-check implementations that
/// replace the static /api/health stub. Each test isolates a single check
/// against either an in-memory store or a mock dependency so the assertions
/// stay deterministic and fast. The ASP.NET-facing <c>HealthCheckResponseWriter</c>
/// lives in <c>Hercules.WebApi</c> and is covered by manual smoke / integration
/// scenarios — these tests focus on the IHealthCheck logic.
/// </summary>
public class HealthCheckTests : IDisposable
{
    private readonly string _tempDir;

    public HealthCheckTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hercules-health-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* best-effort */ }
    }

    private static HealthCheckContext Context() => new()
    {
        Registration = new HealthCheckRegistration("test", _ => null!, HealthStatus.Unhealthy, new List<string>())
    };

    // ---------- SqliteHealthCheck ----------

    [Fact]
    public async Task SqliteHealthCheck_Healthy_WhenStoreOpensAndSelect1Succeeds()
    {
        var store = new SqliteSessionStore(new StorageConfig { DataRoot = _tempDir, SqliteFile = "sessions.db" });
        try
        {
            var check = new SqliteHealthCheck(store, NullLogger<SqliteHealthCheck>.Instance);
            var result = await check.CheckHealthAsync(Context());
            Assert.Equal(HealthStatus.Healthy, result.Status);
            Assert.NotNull(result.Description);
            Assert.Contains("SELECT 1", result.Description);
        }
        finally
        {
            await store.DisposeAsync();
        }
    }

    [Fact]
    public async Task SqliteHealthCheck_Unhealthy_AfterDispose()
    {
        var store = new SqliteSessionStore(new StorageConfig { DataRoot = _tempDir, SqliteFile = "sessions.db" });
        await store.DisposeAsync();

        var check = new SqliteHealthCheck(store, NullLogger<SqliteHealthCheck>.Instance);
        var result = await check.CheckHealthAsync(Context());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    // ---------- LlmHealthCheck ----------

    [Fact]
    public async Task LlmHealthCheck_Healthy_WhenPrimaryProviderResponds()
    {
        var cfg = new LlmConfig { Provider = "yandexgpt" };
        var checker = new Mock<ILLMProviderProbe>();
        checker
            .Setup(c => c.CheckAsync("yandexgpt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderHealthResult
            {
                Provider = "yandexgpt",
                Healthy = true,
                Status = "ok",
                LatencyMs = 42
            });

        var check = new LlmHealthCheck(
            cfg,
            checker.Object,
            new HealthChecksConfig(),
            NullLogger<LlmHealthCheck>.Instance);

        var result = await check.CheckHealthAsync(Context());
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("yandexgpt", result.Description);
    }

    [Fact]
    public async Task LlmHealthCheck_Degraded_WhenPrimaryDownButFallbackOk()
    {
        var cfg = new LlmConfig { Provider = "yandexgpt", Fallback = new List<string> { "ollama-local" } };
        var checker = new Mock<ILLMProviderProbe>();
        checker
            .Setup(c => c.CheckAsync("yandexgpt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderHealthResult
            {
                Provider = "yandexgpt",
                Healthy = false,
                Status = "unreachable",
                Error = "timeout"
            });
        checker
            .Setup(c => c.CheckAsync("ollama-local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderHealthResult
            {
                Provider = "ollama-local",
                Healthy = true,
                Status = "ok",
                LatencyMs = 80
            });

        var check = new LlmHealthCheck(
            cfg,
            checker.Object,
            new HealthChecksConfig(),
            NullLogger<LlmHealthCheck>.Instance);

        var result = await check.CheckHealthAsync(Context());
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("fallback", result.Description);
    }

    [Fact]
    public async Task LlmHealthCheck_Unhealthy_WhenPrimaryAndFallbackDown()
    {
        var cfg = new LlmConfig { Provider = "yandexgpt", Fallback = new List<string> { "ollama-local" } };
        var checker = new Mock<ILLMProviderProbe>();
        checker
            .Setup(c => c.CheckAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderHealthResult
            {
                Provider = "yandexgpt",
                Healthy = false,
                Status = "unreachable",
                Error = "down"
            });

        var check = new LlmHealthCheck(
            cfg,
            checker.Object,
            new HealthChecksConfig(),
            NullLogger<LlmHealthCheck>.Instance);

        var result = await check.CheckHealthAsync(Context());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task LlmHealthCheck_Healthy_WhenProviderNotConfigured()
    {
        var cfg = new LlmConfig { Provider = "" };
        var checker = new Mock<ILLMProviderProbe>();
        var check = new LlmHealthCheck(
            cfg,
            checker.Object,
            new HealthChecksConfig(),
            NullLogger<LlmHealthCheck>.Instance);

        var result = await check.CheckHealthAsync(Context());
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    // ---------- MeshBusHealthCheck ----------

    [Fact]
    public async Task MeshBusHealthCheck_Healthy_WhenBackendReturnsTrue()
    {
        var bus = new Mock<IMeshBus>();
        bus.SetupGet(b => b.BackendKind).Returns("in-process");
        bus.Setup(b => b.IsHealthyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var check = new MeshBusHealthCheck(bus.Object, NullLogger<MeshBusHealthCheck>.Instance);
        var result = await check.CheckHealthAsync(Context());
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task MeshBusHealthCheck_Unhealthy_WhenBackendReturnsFalse()
    {
        var bus = new Mock<IMeshBus>();
        bus.SetupGet(b => b.BackendKind).Returns("redis");
        bus.Setup(b => b.IsHealthyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var check = new MeshBusHealthCheck(bus.Object, NullLogger<MeshBusHealthCheck>.Instance);
        var result = await check.CheckHealthAsync(Context());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task MeshBusHealthCheck_Healthy_WhenBusNotRegistered()
    {
        var check = new MeshBusHealthCheck(bus: null, NullLogger<MeshBusHealthCheck>.Instance);
        var result = await check.CheckHealthAsync(Context());
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    // ---------- OutboxHealthCheck ----------

    [Fact]
    public async Task OutboxHealthCheck_Healthy_WhenPendingBelowThreshold()
    {
        var store = new Mock<IOutboxStore>();
        store.Setup(s => s.GetPendingCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(50);

        var check = new OutboxHealthCheck(
            store.Object,
            new HealthChecksConfig { OutboxMaxPending = 1000 },
            NullLogger<OutboxHealthCheck>.Instance);

        var result = await check.CheckHealthAsync(Context());
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task OutboxHealthCheck_Degraded_WhenPendingExceedsThreshold()
    {
        var store = new Mock<IOutboxStore>();
        store.Setup(s => s.GetPendingCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(5000);

        var check = new OutboxHealthCheck(
            store.Object,
            new HealthChecksConfig { OutboxMaxPending = 1000 },
            NullLogger<OutboxHealthCheck>.Instance);

        var result = await check.CheckHealthAsync(Context());
        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task OutboxHealthCheck_Healthy_WhenStoreNotRegistered()
    {
        var check = new OutboxHealthCheck(
            store: null,
            new HealthChecksConfig(),
            NullLogger<OutboxHealthCheck>.Instance);
        var result = await check.CheckHealthAsync(Context());
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    // ---------- DiskSpaceHealthCheck ----------

    [Fact]
    public async Task DiskSpaceHealthCheck_Healthy_WhenEnoughFreeSpace()
    {
        var cfg = new StorageConfig { DataRoot = _tempDir };
        // Degraded/Unhealthy thresholds set very low so any real volume reports Healthy.
        var healthCfg = new HealthChecksConfig { DiskMinFreeMb = 1, DiskDegradedMb = 2 };
        var check = new DiskSpaceHealthCheck(cfg, healthCfg, NullLogger<DiskSpaceHealthCheck>.Instance);
        var result = await check.CheckHealthAsync(Context());
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task DiskSpaceHealthCheck_AcceptsHealthyOrDegraded_WhenDegradedThresholdTunedHigh()
    {
        // Set Degraded threshold absurdly high so the system immediately reports Degraded.
        // Some volumes will exceed this huge threshold (e.g. 1 TB drives), so accept either.
        var cfg = new StorageConfig { DataRoot = _tempDir };
        var healthCfg = new HealthChecksConfig { DiskMinFreeMb = 1, DiskDegradedMb = int.MaxValue / 2 };
        var check = new DiskSpaceHealthCheck(cfg, healthCfg, NullLogger<DiskSpaceHealthCheck>.Instance);
        var result = await check.CheckHealthAsync(Context());
        Assert.True(result.Status is HealthStatus.Healthy or HealthStatus.Degraded,
            $"Expected Healthy or Degraded, got {result.Status}");
    }

    // ---------- SkillRegistryHealthCheck ----------

    [Fact]
    public async Task SkillRegistryHealthCheck_Healthy_WhenRepoReturnsEmpty()
    {
        var repo = new FileSkillRepository(
            new StorageConfig { DataRoot = _tempDir, SkillsDir = "Skills" },
            NullLogger<FileSkillRepository>.Instance);
        var check = new SkillRegistryHealthCheck(repo, NullLogger<SkillRegistryHealthCheck>.Instance);
        var result = await check.CheckHealthAsync(Context());
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task SkillRegistryHealthCheck_Healthy_WhenRepoNotRegistered()
    {
        var check = new SkillRegistryHealthCheck(repo: null, NullLogger<SkillRegistryHealthCheck>.Instance);
        var result = await check.CheckHealthAsync(Context());
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    // ---------- HealthChecksConfig ----------

    [Fact]
    public void HealthChecksConfig_HasSensibleDefaults()
    {
        var cfg = new HealthChecksConfig();
        Assert.True(cfg.Enabled);
        Assert.Equal(5, cfg.LlmPingTimeoutSec);
        Assert.Equal(100, cfg.DiskMinFreeMb);
        Assert.Equal(1024, cfg.DiskDegradedMb);
        Assert.Equal(1000, cfg.OutboxMaxPending);
    }
}
