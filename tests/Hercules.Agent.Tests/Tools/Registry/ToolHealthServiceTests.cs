using Hercules.Config;
using Hercules.Tools;
using Hercules.Tools.Registry;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Tools.Registry;

/// <summary>
///     Тесты ToolHealthService: health check lifecycle, failure tracking, status transitions.
/// </summary>
public class ToolHealthServiceTests
{
    private readonly Mock<ILogger<ToolHealthService>> _loggerMock;

    public ToolHealthServiceTests()
    {
        _loggerMock = new Mock<ILogger<ToolHealthService>>();
    }

    private ToolHealthService CreateService(
        IToolRegistryService? registry = null,
        ToolRegistryConfig? config = null)
    {
        return new ToolHealthService(
            registry ?? CreateDummyRegistry(),
            config ?? new ToolRegistryConfig(),
            _loggerMock.Object);
    }

    private static ToolRegistryService CreateDummyRegistry(
        ToolRegistryConfig? config = null)
    {
        var loggerMock = new Mock<ILogger<ToolRegistryService>>();
        return new ToolRegistryService(
            Enumerable.Empty<Hercules.Tools.ITool>(),
            config ?? new ToolRegistryConfig(),
            null,
            loggerMock.Object);
    }

    /// <summary>
    ///     Creates a registry with a pre-registered tool entry for testing health service.
    /// </summary>
    private static (ToolRegistryService Registry, ToolHealthService Service) CreateWithEntry(
        ToolRegistryConfig? config = null,
        string toolName = "anytool")
    {
        var loggerRegMock = new Mock<ILogger<ToolRegistryService>>();
        var reg = new ToolRegistryService(
            Enumerable.Empty<Hercules.Tools.ITool>(),
            config ?? new ToolRegistryConfig(),
            null,
            loggerRegMock.Object);

        // Register an entry so RecordFailure/RecordSuccess can update it
        reg.RegisterEntry(new ToolRegistryEntry
        {
            Name = toolName,
            Category = ToolCategory.Internal,
            Enabled = true
        });

        var loggerSvcMock = new Mock<ILogger<ToolHealthService>>();
        var svc = new ToolHealthService(reg, config ?? new ToolRegistryConfig(), loggerSvcMock.Object);
        return (reg, svc);
    }

    // --- RecordFailure ---

    [Fact]
    public void RecordFailure_IncrementsConsecutiveFailures()
    {
        var (registry, svc) = CreateWithEntry(
            new ToolRegistryConfig { ConsecutiveFailureThreshold = 3 });

        svc.RecordFailure("anytool", "error 1");
        svc.RecordFailure("anytool", "error 2");

        var entry = registry.GetEntry("anytool");
        Assert.NotNull(entry);
        Assert.Equal(2, entry.HealthState.ConsecutiveFailures);
        Assert.Equal(ToolHealthStatus.Unhealthy, entry.HealthState.Status);
    }

    [Fact]
    public void RecordFailure_ExceedsThreshold_SetsUnhealthyStatus()
    {
        var (registry, svc) = CreateWithEntry(
            new ToolRegistryConfig { ConsecutiveFailureThreshold = 3 });

        svc.RecordFailure("anytool", "fail 1");
        svc.RecordFailure("anytool", "fail 2");
        svc.RecordFailure("anytool", "fail 3"); // 3rd failure = threshold reached

        var entry = registry.GetEntry("anytool");
        Assert.NotNull(entry);
        Assert.Equal(3, entry.HealthState.ConsecutiveFailures);
        Assert.Equal(ToolHealthStatus.Unhealthy, entry.HealthState.Status);
        Assert.NotNull(entry.HealthState.LastError);
    }

    [Fact]
    public void RecordFailure_NonExistentTool_DoesNotThrow()
    {
        var registry = CreateDummyRegistry();
        var svc = CreateService(registry);

        var ex = Record.Exception(() => svc.RecordFailure("nonexistent", "error"));
        Assert.Null(ex);
    }

    // --- RecordSuccess ---

    [Fact]
    public void RecordSuccess_ResetsConsecutiveFailures()
    {
        var (registry, svc) = CreateWithEntry(
            new ToolRegistryConfig { ConsecutiveFailureThreshold = 3 });

        svc.RecordFailure("anytool", "error");
        svc.RecordFailure("anytool", "error");
        svc.RecordSuccess("anytool");

        var entry = registry.GetEntry("anytool");
        Assert.NotNull(entry);
        Assert.Equal(0, entry.HealthState.ConsecutiveFailures);
        Assert.Equal(ToolHealthStatus.Healthy, entry.HealthState.Status);
    }

    [Fact]
    public void RecordSuccess_UnhealthyTool_TransitionsToHealthy()
    {
        var (registry, svc) = CreateWithEntry(
            new ToolRegistryConfig { ConsecutiveFailureThreshold = 2 });

        svc.RecordFailure("anytool", "fail 1");
        svc.RecordFailure("anytool", "fail 2"); // threshold reached → Unhealthy

        var entryBefore = registry.GetEntry("anytool");
        Assert.Equal(ToolHealthStatus.Unhealthy, entryBefore!.HealthState.Status);

        svc.RecordSuccess("anytool");

        var entryAfter = registry.GetEntry("anytool");
        Assert.Equal(ToolHealthStatus.Healthy, entryAfter!.HealthState.Status);
        Assert.Equal(0, entryAfter.HealthState.ConsecutiveFailures);
    }

    [Fact]
    public void RecordSuccess_NoPriorFailures_DoesNotChangeHealthy()
    {
        var registry = CreateDummyRegistry();
        var svc = CreateService(registry);

        svc.RecordSuccess("anytool");

        var entry = registry.GetEntry("anytool");
        // Entry doesn't exist in registry since we didn't register anything
        Assert.Null(entry);
    }

    // --- CheckToolAsync ---

    [Fact]
    public void CheckToolAsync_NonExistentTool_DoesNotThrow()
    {
        var registry = CreateDummyRegistry();
        var svc = CreateService(registry);

        var ex = Record.Exception(() => svc.CheckToolAsync("nonexistent", CancellationToken.None).GetAwaiter().GetResult());
        Assert.Null(ex);
    }

    // --- Integration: Health state machine ---

    [Fact]
    public void HealthStateMachine_Initial_Unknown()
    {
        var (registry, svc) = CreateWithEntry(
            new ToolRegistryConfig { ConsecutiveFailureThreshold = 2 });

        // First failure sets Unhealthy (not yet at threshold)
        svc.RecordFailure("anytool", "error 1");

        var entry = registry.GetEntry("anytool");
        Assert.NotNull(entry);
        Assert.Equal(ToolHealthStatus.Unhealthy, entry.HealthState.Status); // 1 failure already unhealthy
        Assert.Equal(1, entry.HealthState.ConsecutiveFailures);
    }

    [Fact]
    public void HealthStateMachine_ThresholdBreached_StaysUnhealthy()
    {
        var (registry, svc) = CreateWithEntry(
            new ToolRegistryConfig { ConsecutiveFailureThreshold = 3 });

        svc.RecordFailure("anytool", "e1");
        svc.RecordFailure("anytool", "e2");
        svc.RecordFailure("anytool", "e3"); // threshold = 3

        var entry = registry.GetEntry("anytool");
        Assert.NotNull(entry);
        Assert.Equal(ToolHealthStatus.Unhealthy, entry.HealthState.Status);
        Assert.Equal(3, entry.HealthState.ConsecutiveFailures);
    }

    // --- CheckAllToolsAsync ---

    [Fact]
    public async Task CheckAllToolsAsync_EmptyRegistry_DoesNotThrow()
    {
        var registry = CreateDummyRegistry();
        var svc = CreateService(registry);

        var ex = await Record.ExceptionAsync(() => svc.CheckAllToolsAsync(CancellationToken.None));
        Assert.Null(ex);
    }
}
