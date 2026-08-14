using Hercules.Skills;
using Hercules.Skills.Marketplace;
using Xunit;

namespace Hercules.Agent.Tests.Phase2Tests;

public class DependencyResolverTests
{
    [Fact]
    public async Task Resolve_NoDependencies_ReturnsEmptyList()
    {
        var resolver = new DependencyResolver();

        var result = await resolver.ResolveAsync(
            "no-deps",
            _ => Task.FromResult<SkillDependency[]?>(Array.Empty<SkillDependency>()),
            _ => Task.FromResult((0, false)));

        Assert.True(result.Success);
        Assert.Empty(result.SkillsToInstall);
        Assert.Empty(result.MissingRequired);
        Assert.Empty(result.Cycles);
    }

    [Fact]
    public async Task Resolve_LinearDependency_ResolvesAll()
    {
        var resolver = new DependencyResolver();
        var calls = new List<string>();

        var result = await resolver.ResolveAsync(
            "root",
            id =>
            {
                calls.Add(id);
                return id switch
                {
                    "root" => Task.FromResult<SkillDependency[]?>(new[] { new SkillDependency { Id = "dep-a", VersionConstraint = "^1.0.0" } }),
                    "dep-a" => Task.FromResult<SkillDependency[]?>(new[] { new SkillDependency { Id = "dep-b", VersionConstraint = "^1.0.0" } }),
                    "dep-b" => Task.FromResult<SkillDependency[]?>(Array.Empty<SkillDependency>()),
                    _ => Task.FromResult<SkillDependency[]?>(Array.Empty<SkillDependency>())
                };
            },
            _ => Task.FromResult((10000, false))); // v1.0.0

        Assert.True(result.Success);
        Assert.Contains("dep-b", result.SkillsToInstall);
        Assert.Contains("dep-a", result.SkillsToInstall);
        Assert.DoesNotContain("root", result.SkillsToInstall);
    }

    [Fact]
    public async Task Resolve_MissingRequiredDependency_ReportsMissing()
    {
        var resolver = new DependencyResolver();

        // root depends on "missing", but "missing" manifest is not found (null)
        var result = await resolver.ResolveAsync(
            "root",
            id => id == "root"
                ? Task.FromResult<SkillDependency[]?>(new[] { new SkillDependency { Id = "missing", IsRequired = true, VersionConstraint = "^1.0.0" } })
                : Task.FromResult<SkillDependency[]?>(null),  // null = manifest not found
            _ => Task.FromResult((0, false)));

        Assert.True(result.Success);
        Assert.Contains("missing", result.MissingRequired);
    }

    [Fact]
    public async Task Resolve_OptionalMissingDependency_DoesNotReport()
    {
        var resolver = new DependencyResolver();

        var result = await resolver.ResolveAsync(
            "root",
            id => id == "root"
                ? Task.FromResult<SkillDependency[]?>(new[] { new SkillDependency { Id = "optional", IsRequired = false, VersionConstraint = "^1.0.0" } })
                : Task.FromResult<SkillDependency[]?>(null),
            _ => Task.FromResult((0, false)));

        Assert.True(result.Success);
        Assert.DoesNotContain("optional", result.MissingRequired);
    }

    [Fact]
    public async Task Resolve_AlreadyInstalled_DoesNotReportMissing()
    {
        var resolver = new DependencyResolver();

        var result = await resolver.ResolveAsync(
            "root",
            id => id == "root"
                ? Task.FromResult<SkillDependency[]?>(new[] { new SkillDependency { Id = "installed-dep", VersionConstraint = "^1.0.0" } })
                : Task.FromResult<SkillDependency[]?>(Array.Empty<SkillDependency>()),
            id => id == "installed-dep"
                ? Task.FromResult((10000, true))  // v1.0.0 installed
                : Task.FromResult((0, false)));

        Assert.True(result.Success);
        Assert.DoesNotContain("installed-dep", result.MissingRequired);
    }

    [Fact]
    public async Task Resolve_VersionConstraintMismatch_ReportsMissing()
    {
        var resolver = new DependencyResolver();

        var result = await resolver.ResolveAsync(
            "root",
            id => id == "root"
                ? Task.FromResult<SkillDependency[]?>(new[] { new SkillDependency { Id = "old-dep", VersionConstraint = "^2.0.0" } })
                : Task.FromResult<SkillDependency[]?>(Array.Empty<SkillDependency>()),
            _ => Task.FromResult((10000, true)));  // v1.0.0 installed

        Assert.True(result.Success);
        Assert.Single(result.MissingRequired);
        Assert.StartsWith("old-dep", result.MissingRequired[0]);
        Assert.Contains("doesn't match", result.MissingRequired[0]);
    }

    [Fact]
    public void VersionMatchesConstraint_EmptyConstraint_ReturnsTrue()
    {
        Assert.True(DependencyResolver.VersionMatchesConstraint(10000, null));
        Assert.True(DependencyResolver.VersionMatchesConstraint(10000, ""));
        Assert.True(DependencyResolver.VersionMatchesConstraint(10000, "  "));
    }

    [Fact]
    public void VersionMatchesConstraint_CaretVersion_MatchesMajor()
    {
        // ^1.2.3: >= 1.0.0 AND < 2.0.0 (major unchanged)
        Assert.True(DependencyResolver.VersionMatchesConstraint(10000, "^1.2.3"));   // v1.0.0
        Assert.True(DependencyResolver.VersionMatchesConstraint(10100, "^1.2.3"));  // v1.1.0
        Assert.True(DependencyResolver.VersionMatchesConstraint(19999, "^1.2.3"));   // v1.99.99
        Assert.False(DependencyResolver.VersionMatchesConstraint(20000, "^1.2.3"));  // v2.0.0
    }

    [Fact]
    public void VersionMatchesConstraint_TildeVersion_MatchesMinor()
    {
        // ~1.2.3: >= 1.2.0 AND < 1.3.0 (minor unchanged)
        Assert.True(DependencyResolver.VersionMatchesConstraint(10200, "~1.2.3"));   // v1.2.0
        Assert.True(DependencyResolver.VersionMatchesConstraint(10299, "~1.2.3"));  // v1.2.99
        Assert.False(DependencyResolver.VersionMatchesConstraint(10300, "~1.2.3")); // v1.3.0
    }

    [Fact]
    public void VersionMatchesConstraint_GreaterThanOrEqual_Matches()
    {
        Assert.True(DependencyResolver.VersionMatchesConstraint(10000, ">=1"));
        Assert.True(DependencyResolver.VersionMatchesConstraint(20000, ">=1"));
        Assert.False(DependencyResolver.VersionMatchesConstraint(0, ">=1"));
    }

    [Fact]
    public void VersionMatchesConstraint_LessThan_Matches()
    {
        Assert.True(DependencyResolver.VersionMatchesConstraint(0, "<1"));
        Assert.False(DependencyResolver.VersionMatchesConstraint(10000, "<1"));
    }

    [Fact]
    public void VersionMatchesConstraint_ExactVersion_Matches()
    {
        Assert.True(DependencyResolver.VersionMatchesConstraint(10000, "1"));
        Assert.False(DependencyResolver.VersionMatchesConstraint(20000, "1"));
    }

    [Fact]
    public async Task Resolve_Cancellation_ThrowsOperationCancelled()
    {
        var resolver = new DependencyResolver();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await resolver.ResolveAsync(
                "root",
                _ => Task.FromResult<SkillDependency[]?>(Array.Empty<SkillDependency>()),
                _ => Task.FromResult((0, false)),
                cts.Token));
    }

    [Fact]
    public async Task Resolve_MultipleDepsOfSameSkill_Deduplicates()
    {
        var resolver = new DependencyResolver();

        var result = await resolver.ResolveAsync(
            "root",
            id => id switch
            {
                "root" => Task.FromResult<SkillDependency[]?>(new[]
                {
                    new SkillDependency { Id = "shared", VersionConstraint = "^1.0.0" },
                    new SkillDependency { Id = "shared", VersionConstraint = "^1.0.0" }
                }),
                "shared" => Task.FromResult<SkillDependency[]?>(Array.Empty<SkillDependency>()),
                _ => Task.FromResult<SkillDependency[]?>(Array.Empty<SkillDependency>())
            },
            _ => Task.FromResult((0, false)));

        Assert.True(result.Success);
        var sharedCount = result.SkillsToInstall.Count(s => s == "shared");
        Assert.Equal(1, sharedCount);
    }

    [Fact]
    public async Task Resolve_CycleDetected_ReportsCycle()
    {
        var resolver = new DependencyResolver();

        var result = await resolver.ResolveAsync(
            "root",
            id => id switch
            {
                "root" => Task.FromResult<SkillDependency[]?>(new[] { new SkillDependency { Id = "a" } }),
                "a" => Task.FromResult<SkillDependency[]?>(new[] { new SkillDependency { Id = "b" } }),
                "b" => Task.FromResult<SkillDependency[]?>(new[] { new SkillDependency { Id = "root" } }), // cycle back to root
                _ => Task.FromResult<SkillDependency[]?>(Array.Empty<SkillDependency>())
            },
            _ => Task.FromResult((10000, false)));

        Assert.False(result.Success);
        Assert.NotEmpty(result.Cycles);
    }
}
