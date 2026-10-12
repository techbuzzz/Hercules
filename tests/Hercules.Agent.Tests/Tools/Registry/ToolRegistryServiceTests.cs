using Hercules.Config;
using Hercules.Tools;
using Hercules.Tools.Policy;
using Hercules.Tools.Registry;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Tools.Registry;

/// <summary>
///     Тесты ToolRegistryService: allow/deny patterns, health state, enable/disable, categories.
/// </summary>
public class ToolRegistryServiceTests
{
    private readonly Mock<ILogger<ToolRegistryService>> _loggerMock;

    public ToolRegistryServiceTests()
    {
        _loggerMock = new Mock<ILogger<ToolRegistryService>>();
    }

    private ToolRegistryService CreateService(
        IEnumerable<ITool>? tools = null,
        ToolRegistryConfig? config = null,
        ToolPolicyEngine? policyEngine = null)
    {
        return new ToolRegistryService(
            tools ?? Enumerable.Empty<ITool>(),
            config ?? new ToolRegistryConfig(),
            policyEngine,
            _loggerMock.Object);
    }

    // --- Health state updates ---

    [Fact]
    public void UpdateHealthState_ExistingTool_UpdatesState()
    {
        var tools = new ITool[] { new DummyTool("http") };
        var svc = CreateService(tools);
        var state = new ToolHealthState(ToolHealthStatus.Healthy, DateTime.UtcNow, null, 0);

        svc.UpdateHealthState("http", state);

        var entry = svc.GetEntry("http");
        Assert.NotNull(entry);
        Assert.Equal(ToolHealthStatus.Healthy, entry.HealthState.Status);
    }

    [Fact]
    public void UpdateHealthState_NonExistentTool_DoesNotThrow()
    {
        var svc = CreateService();
        var state = new ToolHealthState(ToolHealthStatus.Healthy, DateTime.UtcNow, null, 0);

        var ex = Record.Exception(() => svc.UpdateHealthState("nonexistent", state));
        Assert.Null(ex);
    }

    // --- Enable/disable ---

    [Fact]
    public void SetEnabled_ExistingTool_ChangesEnabledState()
    {
        var tools = new ITool[] { new DummyTool("http") };
        var svc = CreateService(tools);

        svc.SetEnabled("http", false);

        var entry = svc.GetEntry("http");
        Assert.NotNull(entry);
        Assert.False(entry.Enabled);
        Assert.Equal(ToolHealthStatus.Disabled, entry.HealthState.Status);
    }

    [Fact]
    public void SetEnabled_NonExistentTool_DoesNotThrow()
    {
        var svc = CreateService();

        var ex = Record.Exception(() => svc.SetEnabled("nonexistent", false));
        Assert.Null(ex);
    }

    [Fact]
    public void SetEnabled_ReEnableTool_ResetsHealthToUnknown()
    {
        var tools = new ITool[] { new DummyTool("http") };
        var svc = CreateService(tools);

        svc.SetEnabled("http", false);
        svc.SetEnabled("http", true);

        var entry = svc.GetEntry("http");
        Assert.NotNull(entry);
        Assert.True(entry.Enabled);
        Assert.Equal(ToolHealthStatus.Unknown, entry.HealthState.Status);
    }

    // --- Allow/deny patterns ---

    [Fact]
    public void IsAllowed_DefaultConfig_AllowsAllRegisteredTools()
    {
        var tools = new ITool[] { new DummyTool("http"), new DummyTool("fs") };
        var svc = CreateService(tools);

        Assert.True(svc.IsAllowed("http"));
        Assert.True(svc.IsAllowed("fs"));
    }

    [Fact]
    public void IsAllowed_DeniedPattern_BlocksTool()
    {
        var tools = new ITool[] { new DummyTool("http"), new DummyTool("exec") };
        var config = new ToolRegistryConfig { DeniedPatterns = new List<string> { "exec" } };
        var svc = CreateService(tools, config);

        Assert.True(svc.IsAllowed("http"));
        Assert.False(svc.IsAllowed("exec"));
    }

    [Fact]
    public void IsAllowed_AllowedPatternsSubset_BlocksUnmatched()
    {
        var tools = new ITool[] { new DummyTool("http"), new DummyTool("exec") };
        var config = new ToolRegistryConfig
        {
            AllowedPatterns = new List<string> { "http" },
            DeniedPatterns = new List<string>()
        };
        var svc = CreateService(tools, config);

        Assert.True(svc.IsAllowed("http"));
        Assert.False(svc.IsAllowed("exec"));
    }

    [Fact]
    public void IsAllowed_DenyStar_BlocksAll()
    {
        var tools = new ITool[] { new DummyTool("http") };
        var config = new ToolRegistryConfig { DeniedPatterns = new List<string> { "*" } };
        var svc = CreateService(tools, config);

        Assert.False(svc.IsAllowed("http"));
    }

    [Fact]
    public void IsAllowed_DenySubstring_BlocksMatching()
    {
        var tools = new ITool[] { new DummyTool("http_tool"), new DummyTool("http_api") };
        var config = new ToolRegistryConfig { DeniedPatterns = new List<string> { "*_tool" } };
        var svc = CreateService(tools, config);

        Assert.False(svc.IsAllowed("http_tool"));
        Assert.True(svc.IsAllowed("http_api"));
    }

    [Fact]
    public void IsAllowed_DeniedToolNotRegistered_ReturnsFalse()
    {
        var svc = CreateService();
        Assert.False(svc.IsAllowed("nonexistent"));
    }

    [Fact]
    public void IsAllowed_DisabledTool_ReturnsFalse()
    {
        var tools = new ITool[] { new DummyTool("http") };
        var svc = CreateService(tools);
        svc.SetEnabled("http", false);

        Assert.False(svc.IsAllowed("http"));
    }

    // --- GetAllowedTools ---

    [Fact]
    public void GetAllowedTools_ReturnsOnlyAllowedEnabled()
    {
        var tools = new ITool[]
        {
            new DummyTool("http"),
            new DummyTool("exec"),
            new DummyTool("shell"),
        };
        var config = new ToolRegistryConfig
        {
            AllowedPatterns = new List<string> { "http", "shell" }
        };
        var svc = CreateService(tools, config);
        svc.SetEnabled("shell", false);

        var allowed = svc.GetAllowedTools().ToList();

        Assert.Single(allowed);
        Assert.Contains("http", allowed);
    }

    [Fact]
    public void GetAllowedTools_EmptyAllowedPatterns_ReturnsAll()
    {
        var tools = new ITool[] { new DummyTool("http") };
        var config = new ToolRegistryConfig { AllowedPatterns = new List<string>() };
        var svc = CreateService(tools, config);

        var allowed = svc.GetAllowedTools().ToList();

        Assert.Single(allowed);
    }

    // --- Category filtering ---

    [Fact]
    public void GetByCategory_ReturnsMatchingTools()
    {
        var tools = new ITool[] { new DummyTool("http"), new DummyTool("read_file") };
        var svc = CreateService(tools);

        var httpTools = svc.GetByCategory(ToolCategory.Http).ToList();

        Assert.Single(httpTools);
        Assert.Equal("http", httpTools[0].Name);
    }

    // --- RegisterEntry ---

    [Fact]
    public void RegisterEntry_NewTool_AddsToRegistry()
    {
        var svc = CreateService();
        var entry = new ToolRegistryEntry
        {
            Name = "custom_tool",
            Category = ToolCategory.FileSystem,
            Description = "Custom tool",
            Enabled = true
        };

        svc.RegisterEntry(entry);

        var retrieved = svc.GetEntry("custom_tool");
        Assert.NotNull(retrieved);
        Assert.Equal("custom_tool", retrieved.Name);
        Assert.Equal(ToolCategory.FileSystem, retrieved.Category);
    }

    [Fact]
    public void RegisterEntry_DuplicateName_MergesEntry()
    {
        var tools = new ITool[] { new DummyTool("http") };
        var svc = CreateService(tools);
        var newEntry = new ToolRegistryEntry
        {
            Name = "http",
            Category = ToolCategory.Http,
            Enabled = true
        };

        svc.RegisterEntry(newEntry);

        var entries = svc.GetAllEntries().ToList();
        Assert.Single(entries);
    }

    [Fact]
    public void RegisterEntry_DeniedPattern_SkipsRegistration()
    {
        var config = new ToolRegistryConfig { DeniedPatterns = new List<string> { "secret_*" } };
        var svc = CreateService(config: config);
        var entry = new ToolRegistryEntry { Name = "secret_tool", Category = ToolCategory.Internal };

        svc.RegisterEntry(entry);

        Assert.Null(svc.GetEntry("secret_tool"));
    }

    [Fact]
    public void RegisterEntry_RegistryDisabled_SkipsRegistration()
    {
        var config = new ToolRegistryConfig { Enabled = false };
        var svc = CreateService(config: config);
        var entry = new ToolRegistryEntry { Name = "any_tool", Category = ToolCategory.Internal };

        svc.RegisterEntry(entry);

        Assert.Null(svc.GetEntry("any_tool"));
    }

    // --- UnregisterEntry (task_100: MCP hot-reload) ---

    [Fact]
    public void UnregisterEntry_ExistingTool_RemovesAndReturnsTrue()
    {
        var tools = new ITool[] { new DummyTool("http"), new DummyTool("fs") };
        var svc = CreateService(tools);

        var removed = svc.UnregisterEntry("http");

        Assert.True(removed);
        Assert.Null(svc.GetEntry("http"));
        Assert.NotNull(svc.GetEntry("fs"));
    }

    [Fact]
    public void UnregisterEntry_NonExistent_ReturnsFalse()
    {
        var svc = CreateService();

        var removed = svc.UnregisterEntry("nonexistent");

        Assert.False(removed);
    }

    [Fact]
    public void UnregisterEntry_EmptyOrNullName_ReturnsFalse()
    {
        var tools = new ITool[] { new DummyTool("http") };
        var svc = CreateService(tools);

        Assert.False(svc.UnregisterEntry(""));
        Assert.False(svc.UnregisterEntry("   "));
        Assert.False(svc.UnregisterEntry(null!));
    }

    [Fact]
    public void UnregisterEntry_CaseInsensitive()
    {
        var tools = new ITool[] { new DummyTool("http") };
        var svc = CreateService(tools);

        var removed = svc.UnregisterEntry("HTTP");

        Assert.True(removed);
        Assert.Null(svc.GetEntry("http"));
    }

    // --- Helpers ---

    private sealed class DummyTool : ITool
    {
        public DummyTool(string name) => Name = name;
        public string Name { get; }
        public string Description => $"Dummy tool: {Name}";
        public string? ParametersSchema => null;
        public Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
            => Task.FromResult(ToolResult.Ok("ok"));
    }
}
