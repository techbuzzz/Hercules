using Hercules.Skills;
using Xunit;

namespace Hercules.Agent.Tests.Skills.Package;

/// <summary>
///     Тесты SkillManifestValidator: semver validation, Hercules version compatibility,
///     RequiredTools check, RiskLevel enforcement, Budget sanity.
/// </summary>
public class SkillManifestTests
{
    // ---- Semver validation ----

    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("0.0.1", true)]
    [InlineData("1.2.3", true)]
    [InlineData("1.2.3-alpha", true)]
    [InlineData("1.2.3-alpha.1", true)]
    [InlineData("1.2.3+build.123", true)]
    [InlineData("1.2.3-alpha+build", true)]
    [InlineData("1.0", false)]        // missing patch
    [InlineData("1", false)]           // missing minor+patch
    [InlineData("v1.0.0", false)]     // prefix not allowed
    [InlineData("1.0.0.0", false)]    // too many parts
    [InlineData("", false)]            // empty — "not specified" error, not semver
    public void IsValidSemver(string version, bool expected)
    {
        var validator = new SkillManifestValidator("1.0.0");
        var manifest = new SkillManifest { SchemaVersion = version };
        var result = validator.Validate(manifest);

        // Valid semver → no semver-related errors; invalid → has semver error (or "not specified" for empty)
        var hasSemverError = result.Errors.Any(e =>
            e.Contains("semver", StringComparison.OrdinalIgnoreCase) ||
            (version == "" && e.Contains("SchemaVersion")));
        Assert.Equal(!expected, hasSemverError);
    }

    // ---- Hercules version compatibility ----

    [Fact]
    public void Validate_HerculesOlderThanMinVersion_ReportsError()
    {
        var validator = new SkillManifestValidator("0.9.0"); // current: 0.9.0
        var manifest = new SkillManifest { MinHerculesVersion = "1.0.0" };
        var result = validator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("1.0.0") && e.Contains("MinHerculesVersion"));
        Assert.False(result.IsCompatible);
    }

    [Fact]
    public void Validate_HerculesNewerThanMaxVersion_ReportsWarning()
    {
        var validator = new SkillManifestValidator("2.0.0"); // current: 2.0.0
        var manifest = new SkillManifest { MaxHerculesVersion = "1.5.0" };
        var result = validator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("MaxHerculesVersion"));
        Assert.False(result.IsCompatible);
    }

    [Fact]
    public void Validate_HerculesInRange_IsCompatible()
    {
        var validator = new SkillManifestValidator("1.5.0");
        var manifest = new SkillManifest
        {
            MinHerculesVersion = "1.0.0",
            MaxHerculesVersion = "2.0.0"
        };
        var result = validator.Validate(manifest);

        Assert.True(result.IsValid);
        Assert.True(result.IsCompatible);
    }

    [Fact]
    public void Validate_NoVersionBounds_IsCompatible()
    {
        var validator = new SkillManifestValidator("9.9.9");
        var manifest = new SkillManifest(); // no version bounds
        var result = validator.Validate(manifest);

        Assert.True(result.IsValid);
        Assert.True(result.IsCompatible);
    }

    [Fact]
    public void Validate_NullManifest_ReturnsError()
    {
        var validator = new SkillManifestValidator("1.0.0");
        var result = validator.Validate(null);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("null") || e.Contains("отсутствует"));
    }

    // ---- RequiredTools ----

    [Fact]
    public void Validate_MissingTool_ReportsWarning()
    {
        var validator = new SkillManifestValidator("1.0.0",
            knownToolNames: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "read_file", "write_file"
            });

        var manifest = new SkillManifest
        {
            RequiredTools = new List<string> { "read_file", "unknown_tool" }
        };
        var result = validator.Validate(manifest);

        Assert.Contains("unknown_tool", result.MissingTools);
        Assert.Contains(result.Warnings, w => w.Contains("unknown_tool"));
    }

    [Fact]
    public void Validate_AllToolsKnown_NoMissingTools()
    {
        var validator = new SkillManifestValidator("1.0.0",
            knownToolNames: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "read_file", "write_file", "network_call"
            });

        var manifest = new SkillManifest
        {
            RequiredTools = new List<string> { "read_file", "WRITE_FILE" } // case-insensitive
        };
        var result = validator.Validate(manifest);

        Assert.Empty(result.MissingTools);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Validate_KnownToolsEmpty_NoMissingToolsWarning()
    {
        var validator = new SkillManifestValidator("1.0.0",
            knownToolNames: new HashSet<string>()); // empty — no tools registered
        var manifest = new SkillManifest { RequiredTools = new List<string> { "some_tool" } };
        var result = validator.Validate(manifest);

        // No warning when KnownToolNames is empty (offline/unknown env)
        Assert.Empty(result.Warnings);
    }

    // ---- RiskLevel ----

    [Fact]
    public void Validate_RiskLevelNotAllowed_ReportsError()
    {
        var validator = new SkillManifestValidator("1.0.0",
            allowedRiskLevels: new List<SkillRiskLevel> { SkillRiskLevel.Low, SkillRiskLevel.Medium });

        var manifest = new SkillManifest { RiskLevel = SkillRiskLevel.High };
        var result = validator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("High") && e.Contains("RiskLevel"));
    }

    [Fact]
    public void Validate_RiskLevelAllowed_NoError()
    {
        var validator = new SkillManifestValidator("1.0.0",
            allowedRiskLevels: new List<SkillRiskLevel> { SkillRiskLevel.Low, SkillRiskLevel.Medium });

        var manifest = new SkillManifest { RiskLevel = SkillRiskLevel.Medium };
        var result = validator.Validate(manifest);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_AllowedRiskLevelsNull_NoRiskCheck()
    {
        var validator = new SkillManifestValidator("1.0.0",
            allowedRiskLevels: null);

        var manifest = new SkillManifest { RiskLevel = SkillRiskLevel.Critical };
        var result = validator.Validate(manifest);

        Assert.Empty(result.Errors);
    }

    // ---- Budget sanity ----

    [Fact]
    public void Validate_NegativeBudgetTokens_ReportsError()
    {
        var validator = new SkillManifestValidator("1.0.0");
        var manifest = new SkillManifest
        {
            Budget = new SkillManifestBudget { MaxTokensPerCall = -1 }
        };
        var result = validator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("MaxTokensPerCall"));
    }

    [Fact]
    public void Validate_NegativeBudgetCost_ReportsError()
    {
        var validator = new SkillManifestValidator("1.0.0");
        var manifest = new SkillManifest
        {
            Budget = new SkillManifestBudget { MaxCostPerCallUsd = -0.01m }
        };
        var result = validator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("MaxCostPerCallUsd"));
    }

    [Fact]
    public void Validate_ValidBudget_NoError()
    {
        var validator = new SkillManifestValidator("1.0.0");
        var manifest = new SkillManifest
        {
            Budget = new SkillManifestBudget
            {
                MaxTokensPerCall = 4000,
                MaxCallsPerMinute = 10,
                MaxCostPerCallUsd = 0.05m
            }
        };
        var result = validator.Validate(manifest);

        Assert.True(result.IsValid);
    }

    // ---- Schema version errors ----

    [Fact]
    public void Validate_InvalidSchemaVersion_ReportsError()
    {
        var validator = new SkillManifestValidator("1.0.0");
        var manifest = new SkillManifest { SchemaVersion = "not-semver" };
        var result = validator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SchemaVersion"));
    }

    [Fact]
    public void Validate_EmptySchemaVersion_ReportsError()
    {
        var validator = new SkillManifestValidator("1.0.0");
        var manifest = new SkillManifest { SchemaVersion = "" };
        var result = validator.Validate(manifest);

        Assert.False(result.IsValid);
    }

    // ---- IsCompatible quick check ----

    [Fact]
    public void IsCompatible_VersionMismatch_ReturnsFalse()
    {
        var validator = new SkillManifestValidator("0.9.0");
        var manifest = new SkillManifest { MinHerculesVersion = "1.0.0" };
        Assert.False(validator.IsCompatible(manifest));
    }

    [Fact]
    public void IsCompatible_NullManifest_ReturnsFalse()
    {
        var validator = new SkillManifestValidator("1.0.0");
        Assert.False(validator.IsCompatible(null));
    }

    [Fact]
    public void IsCompatible_NoBounds_ReturnsTrue()
    {
        var validator = new SkillManifestValidator("99.99.99");
        Assert.True(validator.IsCompatible(new SkillManifest()));
    }

    // ---- Full valid manifest ----

    [Fact]
    public void Validate_FullValidManifest_IsValid()
    {
        var validator = new SkillManifestValidator("1.5.0",
            knownToolNames: new HashSet<string> { "http", "fs_read" },
            allowedRiskLevels: new List<SkillRiskLevel> { SkillRiskLevel.Low, SkillRiskLevel.Medium, SkillRiskLevel.High });

        var manifest = new SkillManifest
        {
            SchemaVersion = "1.0.0",
            Owner = "hercules-core",
            MinHerculesVersion = "1.0.0",
            MaxHerculesVersion = "2.0.0",
            InputSchemaVersion = "1.0.0",
            OutputSchemaVersion = "1.0.0",
            RequiredTools = new List<string> { "http" },
            Permissions = new List<string> { "Read", "Network" },
            ModelRequirements = "context>=8k",
            RiskLevel = SkillRiskLevel.Medium,
            Budget = new SkillManifestBudget
            {
                MaxTokensPerCall = 2000,
                MaxCallsPerMinute = 5,
                MaxCostPerCallUsd = 0.01m
            }
        };

        var result = validator.Validate(manifest);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.True(result.IsCompatible);
        Assert.Empty(result.MissingTools);
        Assert.Empty(result.Warnings);
    }
}
