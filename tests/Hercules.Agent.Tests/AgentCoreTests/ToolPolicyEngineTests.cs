using Hercules.Config;
using Hercules.Tools;
using Hercules.Tools.Policy;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.AgentCoreTests;

/// <summary>
///     Unit tests for <see cref="ToolPolicyEngine" />.
/// </summary>
public class ToolPolicyEngineTests : IDisposable
{
    private readonly ToolPolicyConfig _defaultConfig;
    private readonly ToolPermissionSet _permissions;
    private readonly ToolPolicyEngine _engine;

    public ToolPolicyEngineTests()
    {
        _defaultConfig = new ToolPolicyConfig
        {
            DryRun = false,
            AllowUnknownTools = false,
            MinSideEffectLevelForApproval = SideEffectLevel.External,
            DeniedTools = new List<string>(),
            AgentPermissions = "Read|Write|Network|Memory",
            DefaultTimeoutSeconds = 30
        };
        _permissions = ToolPermissionSet.Default;
        var loggerMock = new Mock<ILogger<ToolPolicyEngine>>();
        _engine = new ToolPolicyEngine(_defaultConfig, _permissions, loggerMock.Object);
    }

    public void Dispose() { }

    #region Registration

    [Fact]
    public void Register_Descriptor_AppearsInRegistry()
    {
        var descriptor = new ToolDescriptor
        {
            Name = "test-tool",
            SideEffectLevel = SideEffectLevel.Local,
            RequiredPermissions = ToolPermission.Write
        };

        _engine.Register(descriptor);

        var result = _engine.GetDescriptor("test-tool");
        Assert.NotNull(result);
        Assert.Equal("test-tool", result.Name);
        Assert.Equal(SideEffectLevel.Local, result.SideEffectLevel);
    }

    [Fact]
    public void Register_ITool_InfersDescriptorFromName()
    {
        var tool = new Mock<ITool>();
        tool.Setup(t => t.Name).Returns("http-fetch");
        tool.Setup(t => t.Description).Returns("HTTP fetch");
        tool.Setup(t => t.ParametersSchema).Returns((string?)null);

        _engine.Register(tool.Object);

        var result = _engine.GetDescriptor("http-fetch");
        Assert.NotNull(result);
        Assert.Equal(SideEffectLevel.External, result.SideEffectLevel);
        Assert.True((result.RequiredPermissions & ToolPermission.Network) == ToolPermission.Network);
    }

    [Fact]
    public void Register_IToolWithDescriptor_UsesProvidedDescriptor()
    {
        var customDescriptor = new ToolDescriptor
        {
            Name = "custom-tool",
            SideEffectLevel = SideEffectLevel.Critical,
            RequiredPermissions = ToolPermission.Financial
        };

        ITool tool = new CustomToolWithDescriptor("custom-tool", "Custom tool", customDescriptor);

        _engine.Register(tool);

        var result = _engine.GetDescriptor("custom-tool");
        Assert.NotNull(result);
        Assert.Equal(SideEffectLevel.Critical, result.SideEffectLevel);
    }

    private sealed class CustomToolWithDescriptor : ITool, IToolDescriptorProvider
    {
        private readonly ToolDescriptor _descriptor;

        public CustomToolWithDescriptor(string name, string description, ToolDescriptor descriptor)
        {
            Name = name;
            Description = description;
            _descriptor = descriptor;
        }

        public string Name { get; }
        public string Description { get; }
        public string? ParametersSchema => null;
        public ToolDescriptor GetPolicyDescriptor() => _descriptor;
        public Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default) =>
            Task.FromResult(ToolResult.Ok("ok"));
    }

    #endregion

    #region Dry-run mode

    [Fact]
    public async Task Evaluate_DryRun_ReturnsAllowedWithoutBlocking()
    {
        var cfg = new ToolPolicyConfig { DryRun = true, DeniedTools = new List<string> { "http" } };
        var loggerMock = new Mock<ILogger<ToolPolicyEngine>>();
        var engine = new ToolPolicyEngine(cfg, _permissions, loggerMock.Object);

        var ctx = new PolicyContext { ToolName = "http" };

        var result = await engine.EvaluateAsync(ctx);

        Assert.True(result.IsAllowed);
        Assert.True(result.DryRun);
    }

    #endregion

    #region Deny list

    [Fact]
    public async Task Evaluate_DeniedTool_ReturnsDenied()
    {
        var cfg = new ToolPolicyConfig
        {
            DeniedTools = new List<string> { "dangerous-shell", "http" }
        };
        var loggerMock = new Mock<ILogger<ToolPolicyEngine>>();
        var engine = new ToolPolicyEngine(cfg, _permissions, loggerMock.Object);

        var result = await engine.EvaluateAsync(new PolicyContext { ToolName = "dangerous-shell" });

        Assert.True(result.IsDenied);
        Assert.Contains("deny list", result.DeniedReason);
    }

    [Fact]
    public async Task Evaluate_DeniedToolGlobStar_DeniesAll()
    {
        var cfg = new ToolPolicyConfig { DeniedTools = new List<string> { "*" } };
        var loggerMock = new Mock<ILogger<ToolPolicyEngine>>();
        var engine = new ToolPolicyEngine(cfg, _permissions, loggerMock.Object);

        var result = await engine.EvaluateAsync(new PolicyContext { ToolName = "anything" });

        Assert.True(result.IsDenied);
    }

    [Fact]
    public async Task Evaluate_DeniedToolGlobSuffix_DeniesMatchingSuffix()
    {
        var cfg = new ToolPolicyConfig { DeniedTools = new List<string> { "shell*" } };
        var loggerMock = new Mock<ILogger<ToolPolicyEngine>>();
        var engine = new ToolPolicyEngine(cfg, _permissions, loggerMock.Object);

        var result = await engine.EvaluateAsync(new PolicyContext { ToolName = "shell-exec" });

        Assert.True(result.IsDenied);
    }

    #endregion

    #region Unknown tool

    [Fact]
    public async Task Evaluate_UnknownTool_AllowUnknownFalse_ReturnsUnknownTool()
    {
        var result = await _engine.EvaluateAsync(new PolicyContext { ToolName = "totally-unknown" });

        Assert.Equal(PolicyDecision.UnknownTool, result.Decision);
    }

    [Fact]
    public async Task Evaluate_UnknownTool_AllowUnknownTrue_ReturnsAllowed()
    {
        var cfg = new ToolPolicyConfig { AllowUnknownTools = true };
        var loggerMock = new Mock<ILogger<ToolPolicyEngine>>();
        var engine = new ToolPolicyEngine(cfg, _permissions, loggerMock.Object);

        var result = await engine.EvaluateAsync(new PolicyContext { ToolName = "unknown" });

        Assert.True(result.IsAllowed);
    }

    #endregion

    #region Side-effect level and permissions

    [Fact]
    public async Task Evaluate_ToolBelowApprovalThreshold_Allowed()
    {
        _engine.Register(new ToolDescriptor
        {
            Name = "read-tool",
            SideEffectLevel = SideEffectLevel.Read,
            RequiredPermissions = ToolPermission.Read
        });

        var result = await _engine.EvaluateAsync(new PolicyContext { ToolName = "read-tool" });

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task Evaluate_ToolAtApprovalThreshold_RequiresApproval()
    {
        var cfg = new ToolPolicyConfig
        {
            MinSideEffectLevelForApproval = SideEffectLevel.External
        };
        var loggerMock = new Mock<ILogger<ToolPolicyEngine>>();
        var engine = new ToolPolicyEngine(cfg, _permissions, loggerMock.Object);

        engine.Register(new ToolDescriptor
        {
            Name = "http-fetch",
            SideEffectLevel = SideEffectLevel.External,
            RequiredPermissions = ToolPermission.Network
        });

        var result = await engine.EvaluateAsync(new PolicyContext { ToolName = "http-fetch" });

        Assert.True(result.RequiresApproval);
    }

    [Fact]
    public async Task Evaluate_CriticalTool_AlwaysRequiresApproval()
    {
        // Use a permission the test engine grants (Network) so we reach the Critical check
        _engine.Register(new ToolDescriptor
        {
            Name = "financial-op",
            SideEffectLevel = SideEffectLevel.Critical,
            RequiredPermissions = ToolPermission.Network
        });

        var result = await _engine.EvaluateAsync(new PolicyContext { ToolName = "financial-op" });

        Assert.True(result.RequiresApproval);
    }

    [Fact]
    public async Task Evaluate_ToolMissingPermission_ReturnsDenied()
    {
        var restrictedPerms = new ToolPermissionSet(ToolPermission.Read); // no Write
        var loggerMock = new Mock<ILogger<ToolPolicyEngine>>();
        var engine = new ToolPolicyEngine(_defaultConfig, restrictedPerms, loggerMock.Object);

        engine.Register(new ToolDescriptor
        {
            Name = "file-write",
            SideEffectLevel = SideEffectLevel.Local,
            RequiredPermissions = ToolPermission.Write
        });

        var result = await engine.EvaluateAsync(new PolicyContext { ToolName = "file-write" });

        Assert.True(result.IsDenied);
        Assert.Contains("Missing permissions", result.DeniedReason);
    }

    [Fact]
    public async Task Evaluate_ToolNoPermissions_ReturnsAllowed()
    {
        _engine.Register(new ToolDescriptor
        {
            Name = "info-tool",
            SideEffectLevel = SideEffectLevel.None,
            RequiredPermissions = ToolPermission.None
        });

        var result = await _engine.EvaluateAsync(new PolicyContext { ToolName = "info-tool" });

        Assert.True(result.IsAllowed);
    }

    #endregion

    #region Permission set

    [Fact]
    public void PermissionSet_Has_ReturnsTrueForGranted()
    {
        var ps = new ToolPermissionSet(ToolPermission.Read | ToolPermission.Write);
        Assert.True(ps.Has(ToolPermission.Read));
        Assert.True(ps.Has(ToolPermission.Write));
        Assert.False(ps.Has(ToolPermission.Network));
    }

    [Fact]
    public void PermissionSet_Missing_ReturnsMissingFlags()
    {
        var ps = new ToolPermissionSet(ToolPermission.Read);
        var missing = ps.Missing(ToolPermission.Read | ToolPermission.Write);
        Assert.Equal(ToolPermission.Write, missing);
    }

    [Fact]
    public void PermissionSet_ParseFromString_ParsesPipeSeparated()
    {
        var perms = ToolPermissionExtensions.ParseFromString("Read|Write|Network");
        Assert.True((perms & ToolPermission.Read) == ToolPermission.Read);
        Assert.True((perms & ToolPermission.Write) == ToolPermission.Write);
        Assert.True((perms & ToolPermission.Network) == ToolPermission.Network);
    }

    [Fact]
    public void PermissionSet_ToHumanReadable_ReturnsPipeSeparated()
    {
        var perms = ToolPermission.Read | ToolPermission.Write;
        var readable = perms.ToHumanReadable();
        Assert.Contains("read", readable);
        Assert.Contains("write", readable);
    }

    #endregion
}
