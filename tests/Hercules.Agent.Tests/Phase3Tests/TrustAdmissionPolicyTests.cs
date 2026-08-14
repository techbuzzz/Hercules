using Hercules.Config;
using Hercules.Mesh;
using Hercules.Mesh.Auth;
using Hercules.Mesh.Policy;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Phase3Tests;

public class TrustAdmissionPolicyTests
{
    private readonly Mock<ILogger<TrustAdmissionPolicyEngine>> _logger = new();

    private static IntentEnvelope SampleEnvelope([System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        return new IntentEnvelope
        {
            RequestId = $"req-{name}",
            Sender = "agent/caller",
            Recipient = "agent/target",
            Intent = "csharp-refactor",
            Payload = "{}",
            Version = "1.0"
        };
    }

    private static TrustAdmissionContext MakeContext(
        IntentEnvelope? envelope = null,
        TrustLevel trustLevel = TrustLevel.Unverified,
        DataClassification classification = DataClassification.Public,
        string? intent = null,
        string? riskLevel = null,
        string? callerSchemaVersion = null,
        string? targetMinSchemaVersion = null,
        ManifestResourceLimits? resourceLimits = null,
        IdentityResult? identity = null)
    {
        var env = envelope ?? SampleEnvelope();
        if (intent is not null) env.Intent = intent;

        return new TrustAdmissionContext
        {
            Envelope = env,
            TargetAgentId = "agent/target",
            CallerTrustLevel = trustLevel,
            DataClassification = classification,
            CallerIdentity = identity ?? IdentityResult.Anonymous(),
            RequestedRiskLevel = riskLevel,
            CallerSchemaVersion = callerSchemaVersion,
            TargetMinSchemaVersion = targetMinSchemaVersion,
            TargetResourceLimits = resourceLimits
        };
    }

    private static TrustAdmissionConfig DefaultConfig(string policyMode = "DryRun") =>
        new() { PolicyMode = policyMode, Enabled = true };

    // === Disabled mode ===

    [Fact]
    public void Evaluate_DisabledMode_AllowsAll()
    {
        var cfg = new TrustAdmissionConfig { Enabled = false, PolicyMode = "Enforce" };
        var engine = new TrustAdmissionPolicyEngine(cfg, _logger.Object);
        var ctx = MakeContext();

        var result = engine.Evaluate(ctx);

        Assert.True(result.IsAllowed);
        Assert.False(result.DenialReason is not null);
    }

    [Fact]
    public void Evaluate_DisabledMode_ReturnsCorrectMode()
    {
        var cfg = new TrustAdmissionConfig { Enabled = false };
        var engine = new TrustAdmissionPolicyEngine(cfg, _logger.Object);

        Assert.Equal(PolicyMode.Disabled, engine.Mode);
    }

    // === Dry-run mode ===

    [Fact]
    public void Evaluate_DryRunMode_AllowsButDryRunFlagSet()
    {
        var cfg = DefaultConfig("DryRun");
        var engine = new TrustAdmissionPolicyEngine(cfg, _logger.Object);
        var ctx = MakeContext();

        var result = engine.Evaluate(ctx);

        Assert.True(result.IsAllowed);
        Assert.True(result.DryRun);
    }

    [Fact]
    public void Evaluate_EnforceMode_DeniesWhenTrustTooLow()
    {
        var cfg = new TrustAdmissionConfig
        {
            PolicyMode = "Enforce",
            Enabled = true,
            AllowedTrustLevels = new List<string> { "Trusted", "Verified" }
        };
        var engine = new TrustAdmissionPolicyEngine(cfg, _logger.Object);
        var ctx = MakeContext(trustLevel: TrustLevel.Unverified);

        var result = engine.Evaluate(ctx);

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.DenialReason);
        Assert.Equal(TrustDenialReason.TrustLevelTooLow, result.DenialCode);
        Assert.False(result.DryRun);
    }

    // === Trust level ===

    [Fact]
    public void Evaluate_TrustLevelSufficient_Allows()
    {
        var cfg = new TrustAdmissionConfig
        {
            PolicyMode = "Enforce",
            Enabled = true,
            AllowedTrustLevels = new List<string> { "ProvisionallyTrusted" }
        };
        var engine = new TrustAdmissionPolicyEngine(cfg, _logger.Object);
        var ctx = MakeContext(trustLevel: TrustLevel.ProvisionallyTrusted);

        var result = engine.Evaluate(ctx);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void Evaluate_TrustLevelEmptyList_Allows()
    {
        var cfg = new TrustAdmissionConfig
        {
            PolicyMode = "Enforce",
            Enabled = true,
            AllowedTrustLevels = new List<string>()
        };
        var engine = new TrustAdmissionPolicyEngine(cfg, _logger.Object);
        var ctx = MakeContext(trustLevel: TrustLevel.Unverified);

        var result = engine.Evaluate(ctx);

        Assert.True(result.IsAllowed);
    }

    // === Intent allow-list ===

    [Fact]
    public void Evaluate_IntentNotInAllowList_Denies()
    {
        var cfg = new TrustAdmissionConfig
        {
            PolicyMode = "Enforce",
            Enabled = true,
            AllowedIntents = new List<string> { "code-review", "refactor" }
        };
        var engine = new TrustAdmissionPolicyEngine(cfg, _logger.Object);
        var ctx = MakeContext(intent: "skill-marketplace");

        var result = engine.Evaluate(ctx);

        Assert.False(result.IsAllowed);
        Assert.Equal(TrustDenialReason.IntentNotAllowed, result.DenialCode);
        Assert.Contains("skill-marketplace", result.DenialReason);
    }

    [Fact]
    public void Evaluate_IntentInAllowList_Allows()
    {
        var cfg = new TrustAdmissionConfig
        {
            PolicyMode = "Enforce",
            Enabled = true,
            AllowedIntents = new List<string> { "csharp-refactor", "code-review" }
        };
        var engine = new TrustAdmissionPolicyEngine(cfg, _logger.Object);
        var ctx = MakeContext(intent: "csharp-refactor");

        var result = engine.Evaluate(ctx);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void Evaluate_IntentAllowListEmpty_AllowsAll()
    {
        var cfg = new TrustAdmissionConfig
        {
            PolicyMode = "Enforce",
            Enabled = true,
            AllowedIntents = new List<string>()
        };
        var engine = new TrustAdmissionPolicyEngine(cfg, _logger.Object);
        var ctx = MakeContext(intent: "any-intent");

        Assert.True(engine.Evaluate(ctx).IsAllowed);
    }

    // === Data classification ===

    [Fact]
    public void Evaluate_ClassificationTooHigh_Denies()
    {
        var cfg = new TrustAdmissionConfig
        {
            PolicyMode = "Enforce",
            Enabled = true,
            AllowedClassifications = new List<string> { "Public", "Internal" }
        };
        var engine = new TrustAdmissionPolicyEngine(cfg, _logger.Object);
        var ctx = MakeContext(classification: DataClassification.Confidential);

        var result = engine.Evaluate(ctx);

        Assert.False(result.IsAllowed);
        Assert.Equal(TrustDenialReason.ClassificationTooHigh, result.DenialCode);
    }

    [Fact]
    public void Evaluate_ClassificationWithinLimit_Allows()
    {
        var cfg = new TrustAdmissionConfig
        {
            PolicyMode = "Enforce",
            Enabled = true,
            AllowedClassifications = new List<string> { "Public", "Internal", "Confidential" }
        };
        var engine = new TrustAdmissionPolicyEngine(cfg, _logger.Object);
        var ctx = MakeContext(classification: DataClassification.Internal);

        Assert.True(engine.Evaluate(ctx).IsAllowed);
    }

    // === Schema version ===

    [Fact]
    public void Evaluate_SchemaMismatch_AllowedWhenMismatchPermitted()
    {
        var cfg = new TrustAdmissionConfig
        {
            PolicyMode = "Enforce",
            Enabled = true,
            AllowSchemaMismatch = true
        };
        var engine = new TrustAdmissionPolicyEngine(cfg, _logger.Object);
        var ctx = MakeContext(callerSchemaVersion: "0.5", targetMinSchemaVersion: "1.0");

        Assert.True(engine.Evaluate(ctx).IsAllowed);
    }

    [Fact]
    public void Evaluate_SchemaMismatch_DeniedWhenMismatchNotPermitted()
    {
        var cfg = new TrustAdmissionConfig
        {
            PolicyMode = "Enforce",
            Enabled = true,
            AllowSchemaMismatch = false
        };
        var engine = new TrustAdmissionPolicyEngine(cfg, _logger.Object);
        var ctx = MakeContext(callerSchemaVersion: "0.5", targetMinSchemaVersion: "1.0");

        var result = engine.Evaluate(ctx);

        Assert.False(result.IsAllowed);
        Assert.Equal(TrustDenialReason.SchemaVersionMismatch, result.DenialCode);
    }

    [Fact]
    public void Evaluate_SchemaCompatible_Allows()
    {
        var cfg = new TrustAdmissionConfig
        {
            PolicyMode = "Enforce",
            Enabled = true,
            AllowSchemaMismatch = false
        };
        var engine = new TrustAdmissionPolicyEngine(cfg, _logger.Object);
        var ctx = MakeContext(callerSchemaVersion: "1.0", targetMinSchemaVersion: "1.0");

        Assert.True(engine.Evaluate(ctx).IsAllowed);
    }

    [Fact]
    public void Evaluate_SchemaNewerThanMin_Allows()
    {
        var cfg = new TrustAdmissionConfig
        {
            PolicyMode = "Enforce",
            Enabled = true,
            AllowSchemaMismatch = false
        };
        var engine = new TrustAdmissionPolicyEngine(cfg, _logger.Object);
        var ctx = MakeContext(callerSchemaVersion: "2.0", targetMinSchemaVersion: "1.0");

        Assert.True(engine.Evaluate(ctx).IsAllowed);
    }

    // === Risk level ===

    [Fact]
    public void Evaluate_RiskLevelNotAllowed_Denies()
    {
        var cfg = new TrustAdmissionConfig
        {
            PolicyMode = "Enforce",
            Enabled = true,
            AllowedRiskLevels = new List<string> { "low", "medium" }
        };
        var engine = new TrustAdmissionPolicyEngine(cfg, _logger.Object);
        var ctx = MakeContext(riskLevel: "critical");

        var result = engine.Evaluate(ctx);

        Assert.False(result.IsAllowed);
        Assert.Equal(TrustDenialReason.RiskLevelMismatch, result.DenialCode);
    }

    [Fact]
    public void Evaluate_RiskLevelAllowed_Allows()
    {
        var cfg = new TrustAdmissionConfig
        {
            PolicyMode = "Enforce",
            Enabled = true,
            AllowedRiskLevels = new List<string> { "low", "medium", "high" }
        };
        var engine = new TrustAdmissionPolicyEngine(cfg, _logger.Object);
        var ctx = MakeContext(riskLevel: "high");

        Assert.True(engine.Evaluate(ctx).IsAllowed);
    }

    // === Budget limits ===

    [Fact]
    public void Evaluate_BudgetExceeded_AllowedWhenPermitted()
    {
        var cfg = new TrustAdmissionConfig
        {
            PolicyMode = "Enforce",
            Enabled = true,
            AllowBudgetExceeded = true
        };
        var engine = new TrustAdmissionPolicyEngine(cfg, _logger.Object);
        var limits = new ManifestResourceLimits { MaxTokensPerRequest = 100 };
        var ctx = MakeContext(resourceLimits: limits);

        // Large payload would exceed limit
        ctx.Envelope.Payload = new string('x', 500);

        Assert.True(engine.Evaluate(ctx).IsAllowed);
    }

    // === TrustAdmissionResult factory methods ===

    [Fact]
    public void TrustAdmissionResult_Allowed_PropertiesCorrect()
    {
        var result = TrustAdmissionResult.Allowed();

        Assert.True(result.IsAllowed);
        Assert.Null(result.DenialReason);
        Assert.Null(result.DenialCode);
        Assert.False(result.DryRun);
    }

    [Fact]
    public void TrustAdmissionResult_Denied_PropertiesCorrect()
    {
        var result = TrustAdmissionResult.Denied("test reason", TrustDenialReason.IntentNotAllowed);

        Assert.False(result.IsAllowed);
        Assert.Equal("test reason", result.DenialReason);
        Assert.Equal(TrustDenialReason.IntentNotAllowed, result.DenialCode);
        Assert.False(result.DryRun);
    }

    [Fact]
    public void TrustAdmissionResult_Denied_DryRun()
    {
        var result = TrustAdmissionResult.Denied("dry-run denial", TrustDenialReason.TrustLevelTooLow, dryRun: true);

        Assert.False(result.IsAllowed);
        Assert.True(result.DryRun);
    }

    // === PolicyMode parsing ===

    [Theory]
    [InlineData("Enforce", PolicyMode.Enforce)]
    [InlineData("DryRun", PolicyMode.DryRun)]
    [InlineData("Disabled", PolicyMode.Disabled)]
    [InlineData("enforce", PolicyMode.Enforce)]
    [InlineData("dryrun", PolicyMode.DryRun)]
    [InlineData("unknown-value", PolicyMode.DryRun)] // Default
    public void PolicyMode_ParsesCorrectly(string modeString, PolicyMode expected)
    {
        var cfg = new TrustAdmissionConfig { PolicyMode = modeString, Enabled = true };
        var engine = new TrustAdmissionPolicyEngine(cfg, _logger.Object);

        Assert.Equal(expected, engine.Mode);
    }
}
