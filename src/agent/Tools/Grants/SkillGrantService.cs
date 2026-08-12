using Hercules.Config;
using Hercules.Tools.Policy;
using Microsoft.Extensions.Logging;

namespace Hercules.Tools.Grants;

/// <summary>
///     Реализация ISkillGrantService.
///     Использует SkillGrantStore (SQLite) для персистентности.
/// </summary>
public sealed class SkillGrantService : ISkillGrantService
{
    private readonly SkillGrantStore _store;
    private readonly LeastPrivilegeConfig _config;
    private readonly ILogger<SkillGrantService> _logger;

    public SkillGrantService(SkillGrantStore store, LeastPrivilegeConfig config, ILogger<SkillGrantService> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<SkillGrant> GrantAsync(GrantRequest request, CancellationToken ct = default)
    {
        var permission = ToolPermissionExtensions.ParseFromString(request.Permission);
        var grant = new SkillGrant
        {
            SkillId = request.SkillId,
            Permission = permission,
            Scope = request.Scope,
            SessionId = request.SessionId,
            Grantor = request.Grantor,
            GrantedAt = DateTime.UtcNow,
            ExpiresAt = request.DurationMinutes > 0
                ? DateTime.UtcNow.AddMinutes(request.DurationMinutes)
                : null
        };

        _store.UpsertGrant(grant);
        _logger.LogInformation("[Grants] Granted {Permission} to skill '{SkillId}' (scope={Scope}, grantor={Grantor}, expires={ExpiresAt})",
            permission, request.SkillId, request.Scope, request.Grantor, grant.ExpiresAt);

        return Task.FromResult(grant);
    }

    public Task RevokeAsync(string skillId, string? permission = null, CancellationToken ct = default)
    {
        ToolPermission? perm = permission is not null
            ? ToolPermissionExtensions.ParseFromString(permission)
            : null;

        _store.RevokeGrants(skillId, perm);
        _logger.LogInformation("[Grants] Revoked grants for skill '{SkillId}' (permission={Permission})",
            skillId, permission ?? "ALL");

        return Task.CompletedTask;
    }

    public Task RevokeByIdAsync(string grantId, CancellationToken ct = default)
    {
        _store.RevokeGrantById(grantId);
        _logger.LogInformation("[Grants] Revoked grant by ID '{GrantId}'", grantId);
        return Task.CompletedTask;
    }

    public Task<SkillGrant[]> GetGrantsAsync(string skillId, string? sessionId = null, CancellationToken ct = default)
    {
        var grants = _store.GetGrantsForSkill(skillId, sessionId);
        return Task.FromResult(grants);
    }

    public Task<ToolPermission> GetEffectivePermissionsAsync(string skillId, string? sessionId = null, CancellationToken ct = default)
    {
        var grants = _store.GetGrantsForSkill(skillId, sessionId);
        var effective = ToolPermission.None;
        foreach (var grant in grants)
        {
            effective |= grant.Permission;
        }
        return Task.FromResult(effective);
    }

    public Task<GrantValidationResult> CheckPermissionAsync(
        string skillId, ToolPermission permission, string? sessionId = null, CancellationToken ct = default)
    {
        return CheckAllPermissionsAsync(skillId, permission, sessionId, ct);
    }

    public Task<GrantValidationResult> CheckAllPermissionsAsync(
        string skillId, ToolPermission required, string? sessionId = null, CancellationToken ct = default)
    {
        if (!_config.Enabled)
        {
            // Grants disabled — allow all
            return Task.FromResult(GrantValidationResult.Valid(ToolPermission.Read | ToolPermission.Write | ToolPermission.Network | ToolPermission.Memory, GrantScope.Global));
        }

        if (!_config.EnforceOnRuntime)
        {
            return Task.FromResult(GrantValidationResult.Valid(ToolPermission.Read | ToolPermission.Write | ToolPermission.Network | ToolPermission.Memory, GrantScope.Global));
        }

        var grants = _store.GetGrantsForSkill(skillId, sessionId);
        if (grants.Length == 0)
        {
            // No grants found — check migration mode
            if (_config.MigrationMode)
            {
                // Migration mode: no declared permissions = allow only Read (safe default)
                var effective = ToolPermission.Read;
                var missing = required & ~effective;
                if (missing == ToolPermission.None)
                {
                    return Task.FromResult(GrantValidationResult.Valid(effective, GrantScope.Skill));
                }
                return Task.FromResult(GrantValidationResult.Invalid(
                    $"No grants found for skill '{skillId}'. Migration mode: only Read granted by default.", effective, missing));
            }

            return Task.FromResult(GrantValidationResult.NotFoundResult(skillId));
        }

        var effectivePermissions = ToolPermission.None;
        GrantScope dominantScope = GrantScope.Skill;
        foreach (var grant in grants)
        {
            effectivePermissions |= grant.Permission;
            if (grant.Scope > dominantScope) dominantScope = grant.Scope;
        }

        var missingPerms = required & ~effectivePermissions;
        if (missingPerms == ToolPermission.None)
        {
            return Task.FromResult(GrantValidationResult.Valid(effectivePermissions, dominantScope));
        }

        return Task.FromResult(new GrantValidationResult
        {
            IsValid = false,
            GrantedPermissions = effectivePermissions,
            MissingPermissions = missingPerms,
            Scope = dominantScope,
            Message = $"Skill '{skillId}' is missing required permissions: {ToolPermissionExtensions.ToHumanReadable(missingPerms)}"
        });
    }
}
