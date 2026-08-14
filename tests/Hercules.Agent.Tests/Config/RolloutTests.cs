using System.Text.Json;
using Hercules.Config;
using Hercules.Config.Rollout;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Config;

public class RolloutManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigRolloutConfig _config;
    private readonly FakeValidator _validator;
    private readonly LocalConfigValidator _localValidator;
    private readonly RuntimeConfigStore _store;
    private readonly RolloutManager _manager;

    public RolloutManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-rollout-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var bundleDir = Path.Combine(_tempDir, "bundles");
        Directory.CreateDirectory(bundleDir);

        _config = new ConfigRolloutConfig
        {
            BundlesPath = bundleDir,
            RequireBundleSignature = false,
            RequireTrustedSigner = false,
            AutoPromoteToStaging = true,
            MaxPayloadBytes = 5 * 1024 * 1024,
            DefaultStagingDurationMinutes = 60,
            EnableExpiryChecker = false
        };

        var storeDir = Path.Combine(_tempDir, "store");
        Directory.CreateDirectory(storeDir);
        var storePath = Path.Combine(storeDir, "config.json");
        _store = new RuntimeConfigStore(new AppConfig(), storePath, NullLogger<RuntimeConfigStore>.Instance);

        _validator = new FakeValidator();
        _localValidator = new LocalConfigValidator(_config, NullLogger<LocalConfigValidator>.Instance);
        _manager = new RolloutManager(
            _config,
            _validator,
            _localValidator,
            _store,
            NullLogger<RolloutManager>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* ignore */ }
    }

    private static ConfigBundle ValidBundle(string payload = "{}") => new()
    {
        Id = "bundle-test-001",
        Version = "1.0.0",
        Type = "config",
        Name = "test-bundle",
        Payload = payload,
        Signature = "fake"
    };

    [Fact]
    public void GetState_Initially_Has_No_Current_Bundle()
    {
        var state = _manager.GetState();
        Assert.Null(state.CurrentBundleId);
        Assert.Equal(BundleStage.Pending, state.CurrentStage);
    }

    [Fact]
    public async Task ApplyBundle_With_Valid_Bundle_Passes()
    {
        var bundle = ValidBundle();
        var result = await _manager.ApplyBundleAsync(bundle);

        Assert.True(result.Success);
        Assert.Equal(bundle.Id, result.AppliedBundleId);
    }

    [Fact]
    public async Task ApplyBundle_Sets_Bundle_To_Staging_When_AutoPromote_Is_True()
    {
        var bundle = ValidBundle();
        var result = await _manager.ApplyBundleAsync(bundle);

        Assert.True(result.Success);
        Assert.Equal(BundleStage.Staging, result.NewStage);
    }

    [Fact]
    public async Task ApplyBundle_With_Invalid_Signature_Fails()
    {
        _validator.FailNextValidation = true;
        var bundle = ValidBundle();
        var result = await _manager.ApplyBundleAsync(bundle);

        Assert.False(result.Success);
        Assert.Contains("validation failed", result.Error);
    }

    [Fact]
    public async Task ApplyBundle_With_Invalid_JSON_Fails()
    {
        var bundle = ValidBundle("not-valid-json");
        var result = await _manager.ApplyBundleAsync(bundle);

        Assert.False(result.Success);
        Assert.Contains("Invalid JSON", result.Error);
    }

    [Fact]
    public async Task PromoteStage_From_Pending_To_Staging_Succeeds()
    {
        _config.AutoPromoteToStaging = false;
        var mgr = new RolloutManager(_config, _validator, _localValidator, _store,
            NullLogger<RolloutManager>.Instance);

        var bundle = ValidBundle();
        await mgr.ApplyBundleAsync(bundle);

        var result = await mgr.PromoteStageAsync(bundle.Id);

        Assert.True(result.Success);
        Assert.Equal(BundleStage.Staging, result.NewStage);
    }

    [Fact]
    public async Task PromoteStage_From_Staging_To_Production_Sets_Current()
    {
        _config.AutoPromoteToStaging = false;
        var mgr = new RolloutManager(_config, _validator, _localValidator, _store,
            NullLogger<RolloutManager>.Instance);

        var bundle = ValidBundle();
        await mgr.ApplyBundleAsync(bundle);
        await mgr.PromoteStageAsync(bundle.Id);

        var result = await mgr.PromoteStageAsync(bundle.Id);

        Assert.True(result.Success);
        Assert.Equal(BundleStage.Production, result.NewStage);

        var state = mgr.GetState();
        Assert.Equal(bundle.Id, state.CurrentBundleId);
        Assert.Equal("1.0.0", state.CurrentVersion);
    }

    [Fact]
    public async Task PromoteStage_Sets_LastKnownGood_Before_Promoting()
    {
        _config.AutoPromoteToStaging = false;
        var mgr = new RolloutManager(_config, _validator, _localValidator, _store,
            NullLogger<RolloutManager>.Instance);

        // First bundle to production
        var bundle1 = ValidBundle();
        bundle1.Id = "bundle-lkg-1";
        await mgr.ApplyBundleAsync(bundle1);
        await mgr.PromoteStageAsync(bundle1.Id);
        await mgr.PromoteStageAsync(bundle1.Id);

        // Second bundle to production
        var bundle2 = ValidBundle();
        bundle2.Id = "bundle-lkg-2";
        bundle2.Version = "2.0.0";
        await mgr.ApplyBundleAsync(bundle2);
        await mgr.PromoteStageAsync(bundle2.Id);

        var result = await mgr.PromoteStageAsync(bundle2.Id);

        Assert.True(result.Success);
        var state = mgr.GetState();
        Assert.Equal(bundle1.Id, state.LastKnownGoodBundleId);
        Assert.Equal("1.0.0", state.LastKnownGoodVersion);
    }

    [Fact]
    public async Task Rollback_To_LKG_Succeeds()
    {
        _config.AutoPromoteToStaging = false;
        var mgr = new RolloutManager(_config, _validator, _localValidator, _store,
            NullLogger<RolloutManager>.Instance);

        // v1 bundle
        var bundle1 = ValidBundle();
        bundle1.Id = "bundle-rb-1";
        await mgr.ApplyBundleAsync(bundle1);
        await mgr.PromoteStageAsync(bundle1.Id);
        await mgr.PromoteStageAsync(bundle1.Id);

        // v2 bundle
        var bundle2 = ValidBundle();
        bundle2.Id = "bundle-rb-2";
        bundle2.Version = "2.0.0";
        await mgr.ApplyBundleAsync(bundle2);
        await mgr.PromoteStageAsync(bundle2.Id);
        await mgr.PromoteStageAsync(bundle2.Id);

        var result = await mgr.RollbackAsync("Test rollback");

        Assert.True(result.Success);
        var state = mgr.GetState();
        Assert.Equal(bundle1.Id, state.CurrentBundleId);
        Assert.NotNull(state.LastRollbackAt);
    }

    [Fact]
    public async Task Rollback_With_No_LKG_Fails()
    {
        var result = await _manager.RollbackAsync();
        Assert.False(result.Success);
        Assert.Contains("No last-known-good", result.Error);
    }

    [Fact]
    public async Task PromoteStage_To_AlreadyFinal_Fails()
    {
        _config.AutoPromoteToStaging = false;
        var mgr = new RolloutManager(_config, _validator, _localValidator, _store,
            NullLogger<RolloutManager>.Instance);

        var bundle = ValidBundle();
        await mgr.ApplyBundleAsync(bundle);
        await mgr.PromoteStageAsync(bundle.Id);
        await mgr.PromoteStageAsync(bundle.Id);

        var result = await mgr.PromoteStageAsync(bundle.Id);

        Assert.False(result.Success);
        Assert.Contains("already at final stage", result.Error);
    }

    [Fact]
    public async Task PromoteStage_NonExistent_Bundle_Fails()
    {
        var result = await _manager.PromoteStageAsync("non-existent-bundle");
        Assert.False(result.Success);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task GetBundle_Returns_Bundle_After_Apply()
    {
        var bundle = ValidBundle();
        await _manager.ApplyBundleAsync(bundle);

        var retrieved = _manager.GetBundle(bundle.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(bundle.Id, retrieved.Id);
        Assert.Equal(bundle.Version, retrieved.Version);
    }

    [Fact]
    public void GetBundle_Returns_Null_For_Unknown_Id()
    {
        var retrieved = _manager.GetBundle("unknown-bundle");
        Assert.Null(retrieved);
    }

    [Fact]
    public void GetState_Returns_Copy_Not_Mutable_Reference()
    {
        var state1 = _manager.GetState();
        var state2 = _manager.GetState();
        Assert.NotSame(state1, state2);
    }

    private sealed class FakeValidator : ISignedBundleValidator
    {
        public bool FailNextValidation { get; set; }

        public Task<BundleValidationResult> ValidateAsync(ConfigBundle bundle, CancellationToken ct = default)
        {
            if (FailNextValidation)
            {
                FailNextValidation = false;
                return Task.FromResult(BundleValidationResult.Failure(new[] { "Fake validation failure" }));
            }

            return Task.FromResult(BundleValidationResult.Success(bundle.SignerId, bundle.SignedAt));
        }

        public Task<ConfigBundle> SignBundleAsync(ConfigBundle bundle, CancellationToken ct = default)
        {
            bundle.Signature = "fake-signed";
            bundle.SignedAt = DateTime.UtcNow;
            bundle.SignerId = "test-signer";
            return Task.FromResult(bundle);
        }
    }
}

public class LocalConfigValidatorTests
{
    private readonly ConfigRolloutConfig _config = new() { MaxPayloadBytes = 5 * 1024 * 1024 };
    private readonly LocalConfigValidator _validator = new(new ConfigRolloutConfig(), NullLogger<LocalConfigValidator>.Instance);

    [Fact]
    public void Validate_Valid_Config_Payload_Passes()
    {
        var bundle = new ConfigBundle
        {
            Id = "test",
            Payload = """{"llm":{"provider":"test"},"agent":{"skillCreationThreshold":3}}""",
            Type = "config",
            Version = "1.0.0"
        };

        var result = _validator.Validate(bundle);
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_Invalid_JSON_Fails()
    {
        var bundle = new ConfigBundle
        {
            Id = "test",
            Payload = "not-valid-json",
            Type = "config"
        };

        var result = _validator.Validate(bundle);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Invalid JSON"));
    }

    [Fact]
    public void Validate_Empty_Payload_Fails()
    {
        var bundle = new ConfigBundle { Id = "test", Payload = "", Type = "config" };
        var result = _validator.Validate(bundle);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Oversized_Payload_Fails()
    {
        var config = new ConfigRolloutConfig { MaxPayloadBytes = 10 };
        var validator = new LocalConfigValidator(config, NullLogger<LocalConfigValidator>.Instance);
        var payload = "{\"data\":\"" + new string('x', 100) + "\"}";
        var bundle = new ConfigBundle
        {
            Id = "test",
            Payload = payload,
            Type = "config"
        };

        var result = validator.Validate(bundle);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("exceeds maximum"));
    }

    [Fact]
    public void Validate_Valid_Policy_Payload_Passes()
    {
        var bundle = new ConfigBundle
        {
            Id = "test",
            Payload = """{"rules":[{"action":"deny","tool":"exec"}]}""",
            Type = "policy",
            Version = "1.0.0"
        };

        var result = _validator.Validate(bundle);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Missing_Version_Warns()
    {
        var bundle = new ConfigBundle
        {
            Id = "test",
            Payload = "{}",
            Version = "not-semver"
        };

        var result = _validator.Validate(bundle);
        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("not a valid semver"));
    }
}
