using Hercules.Config;
using Hercules.Tools.Grants;
using Hercules.Tools.Policy;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Tools.Grants;

public class SkillGrantServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SkillGrantStore _store;
    private readonly LeastPrivilegeConfig _config;
    private readonly SkillGrantService _svc;

    public SkillGrantServiceTests()
    {
        // Use unique temp dir per test to avoid file lock conflicts on Windows
        var tempDir = Path.Combine(Path.GetTempPath(), $"hercules_grants_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        _dbPath = Path.Combine(tempDir, "grants.db");
        _store = new SkillGrantStore(_dbPath);
        _config = new LeastPrivilegeConfig { Enabled = true, EnforceOnRuntime = true, MigrationMode = true };
        _svc = new SkillGrantService(_store, _config, Mock.Of<ILogger<SkillGrantService>>());
    }

    public void Dispose()
    {
        _store.Dispose();
        // Try multiple times on Windows (file locks may linger)
        for (var i = 0; i < 5; i++)
        {
            try
            {
                var dir = Path.GetDirectoryName(_dbPath);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                break;
            }
            catch (IOException) { Thread.Sleep(50); }
        }
    }

    [Fact]
    public async Task GrantAsync_CreatesGrant()
    {
        var req = new GrantRequest
        {
            SkillId = "test-skill",
            Permission = "Write",
            Scope = GrantScope.Skill,
            Grantor = "user"
        };

        var grant = await _svc.GrantAsync(req);

        Assert.NotNull(grant);
        Assert.Equal("test-skill", grant.SkillId);
        Assert.Equal(ToolPermission.Write, grant.Permission);
        Assert.Equal(GrantScope.Skill, grant.Scope);
        Assert.Equal("user", grant.Grantor);
    }

    [Fact]
    public async Task GrantAsync_ParsesPipeDelimitedPermissions()
    {
        var req = new GrantRequest
        {
            SkillId = "multi-perm-skill",
            Permission = "Write|Network",
            Scope = GrantScope.Global,
            Grantor = "admin"
        };

        var grant = await _svc.GrantAsync(req);

        Assert.Equal(ToolPermission.Write | ToolPermission.Network, grant.Permission);
        Assert.Equal(GrantScope.Global, grant.Scope);
    }

    [Fact]
    public async Task GrantAsync_RespectsDuration()
    {
        var req = new GrantRequest
        {
            SkillId = "temp-skill",
            Permission = "Read",
            Scope = GrantScope.Session,
            SessionId = "session-1",
            Grantor = "system",
            DurationMinutes = 30
        };

        var grant = await _svc.GrantAsync(req);

        Assert.NotNull(grant.ExpiresAt);
        Assert.True(grant.ExpiresAt > DateTime.UtcNow);
        Assert.True(grant.ExpiresAt <= DateTime.UtcNow.AddMinutes(31));
    }

    [Fact]
    public async Task GetGrantsAsync_ReturnsSkillAndGlobalGrants()
    {
        await _svc.GrantAsync(new GrantRequest { SkillId = "skill-a", Permission = "Read", Scope = GrantScope.Skill, Grantor = "system" });
        await _svc.GrantAsync(new GrantRequest { SkillId = "skill-a", Permission = "Write", Scope = GrantScope.Global, Grantor = "admin" });

        var grants = await _svc.GetGrantsAsync("skill-a");

        Assert.Equal(2, grants.Length);
        Assert.Contains(grants, g => g.Permission == ToolPermission.Read);
        Assert.Contains(grants, g => g.Permission == ToolPermission.Write);
    }

    [Fact]
    public async Task GetGrantsAsync_SessionScope_FiltersBySessionId()
    {
        await _svc.GrantAsync(new GrantRequest { SkillId = "s1", Permission = "Write", Scope = GrantScope.Session, SessionId = "sess-A", Grantor = "u1" });
        await _svc.GrantAsync(new GrantRequest { SkillId = "s1", Permission = "Read", Scope = GrantScope.Session, SessionId = "sess-B", Grantor = "u2" });

        var grantsA = await _svc.GetGrantsAsync("s1", "sess-A");
        var grantsB = await _svc.GetGrantsAsync("s1", "sess-B");

        Assert.Single(grantsA);
        Assert.Contains(grantsA, g => g.Permission == ToolPermission.Write);
        Assert.Single(grantsB);
        Assert.Contains(grantsB, g => g.Permission == ToolPermission.Read);
    }

    [Fact]
    public async Task RevokeAsync_RemovesAllGrantsForSkill()
    {
        await _svc.GrantAsync(new GrantRequest { SkillId = "revoke-me", Permission = "Read", Scope = GrantScope.Skill, Grantor = "system" });
        await _svc.GrantAsync(new GrantRequest { SkillId = "revoke-me", Permission = "Write", Scope = GrantScope.Skill, Grantor = "system" });

        await _svc.RevokeAsync("revoke-me");

        var grants = await _svc.GetGrantsAsync("revoke-me");
        Assert.Empty(grants);
    }

    [Fact]
    public async Task RevokeAsync_SpecificPermission_OnlyRemovesThatPermission()
    {
        await _svc.GrantAsync(new GrantRequest { SkillId = "partial", Permission = "Read", Scope = GrantScope.Skill, Grantor = "s" });
        await _svc.GrantAsync(new GrantRequest { SkillId = "partial", Permission = "Write", Scope = GrantScope.Skill, Grantor = "s" });

        await _svc.RevokeAsync("partial", "Write");

        var grants = await _svc.GetGrantsAsync("partial");
        Assert.Single(grants);
        Assert.Equal(ToolPermission.Read, grants[0].Permission);
    }

    [Fact]
    public async Task CheckPermissionAsync_ValidGrant_ReturnsTrue()
    {
        await _svc.GrantAsync(new GrantRequest { SkillId = "has-write", Permission = "Write", Scope = GrantScope.Skill, Grantor = "system" });

        var result = await _svc.CheckPermissionAsync("has-write", ToolPermission.Write);

        Assert.True(result.IsValid);
        Assert.Equal(ToolPermission.Write, result.GrantedPermissions);
        Assert.Equal(GrantScope.Skill, result.Scope);
    }

    [Fact]
    public async Task CheckPermissionAsync_MissingGrant_ReturnsInvalid()
    {
        var result = await _svc.CheckPermissionAsync("unknown-skill", ToolPermission.Write);

        // Migration mode: returns invalid when required Write is not in default Read grant
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Message ?? "");
    }

    [Fact]
    public async Task CheckAllPermissionsAsync_HasAllPermissions_ReturnsValid()
    {
        await _svc.GrantAsync(new GrantRequest { SkillId = "has-both", Permission = "Write|Network", Scope = GrantScope.Skill, Grantor = "s" });

        var result = await _svc.CheckAllPermissionsAsync("has-both", ToolPermission.Write | ToolPermission.Network);

        Assert.True(result.IsValid);
        Assert.Equal(ToolPermission.Write | ToolPermission.Network, result.GrantedPermissions);
        Assert.Equal(ToolPermission.None, result.MissingPermissions);
    }

    [Fact]
    public async Task CheckAllPermissionsAsync_PartialPermissions_ReturnsInvalidWithMissing()
    {
        await _svc.GrantAsync(new GrantRequest { SkillId = "partial-skill", Permission = "Read", Scope = GrantScope.Skill, Grantor = "s" });

        var result = await _svc.CheckAllPermissionsAsync("partial-skill", ToolPermission.Write);

        Assert.False(result.IsValid);
        Assert.Equal(ToolPermission.Read, result.GrantedPermissions);
        Assert.Equal(ToolPermission.Write, result.MissingPermissions);
    }

    [Fact]
    public async Task GetEffectivePermissionsAsync_CombinesMultipleGrants()
    {
        await _svc.GrantAsync(new GrantRequest { SkillId = "eff", Permission = "Read", Scope = GrantScope.Skill, Grantor = "s" });
        await _svc.GrantAsync(new GrantRequest { SkillId = "eff", Permission = "Write", Scope = GrantScope.Skill, Grantor = "s" });
        await _svc.GrantAsync(new GrantRequest { SkillId = "eff", Permission = "Network", Scope = GrantScope.Global, Grantor = "s" });

        var effective = await _svc.GetEffectivePermissionsAsync("eff");

        Assert.Equal(ToolPermission.Read | ToolPermission.Write | ToolPermission.Network, effective);
    }

    [Fact]
    public async Task CheckPermissionAsync_DisabledConfig_AllowsAll()
    {
        var disabledConfig = new LeastPrivilegeConfig { Enabled = false, EnforceOnRuntime = false };
        var svcDisabled = new SkillGrantService(_store, disabledConfig, Mock.Of<ILogger<SkillGrantService>>());

        var result = await svcDisabled.CheckPermissionAsync("any-skill", ToolPermission.Delete);

        Assert.True(result.IsValid);
        Assert.Equal(GrantScope.Global, result.Scope);
    }

    [Fact]
    public async Task RevokeByIdAsync_RemovesSpecificGrant()
    {
        var grant1 = await _svc.GrantAsync(new GrantRequest { SkillId = "id-test", Permission = "Read", Scope = GrantScope.Skill, Grantor = "s" });
        await _svc.GrantAsync(new GrantRequest { SkillId = "id-test", Permission = "Write", Scope = GrantScope.Skill, Grantor = "s" });

        await _svc.RevokeByIdAsync(grant1.Id);

        var grants = await _svc.GetGrantsAsync("id-test");
        Assert.Single(grants);
        Assert.Equal(ToolPermission.Write, grants[0].Permission);
    }
}
