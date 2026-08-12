using Hercules.Tools.Policy;

namespace Hercules.Tools.Grants;

/// <summary>
///     Сервис управления least-privilege grants.
///     Навык получает только явно выданные permissions.
///     Runtime проверяет наличие grant перед tool execution.
/// </summary>
public interface ISkillGrantService
{
    /// <summary>Выдать grant навыку.</summary>
    Task<SkillGrant> GrantAsync(GrantRequest request, CancellationToken ct = default);

    /// <summary>Отозвать все grants навыка (или конкретный permission).</summary>
    Task RevokeAsync(string skillId, string? permission = null, CancellationToken ct = default);

    /// <summary>Отозвать grant по ID.</summary>
    Task RevokeByIdAsync(string grantId, CancellationToken ct = default);

    /// <summary>Получить все активные grants для навыка (Skill + Session + Global scope).</summary>
    Task<SkillGrant[]> GetGrantsAsync(string skillId, string? sessionId = null, CancellationToken ct = default);

    /// <summary>Получить Effective permissions: объединение всех активных grants.</summary>
    Task<ToolPermission> GetEffectivePermissionsAsync(string skillId, string? sessionId = null, CancellationToken ct = default);

    /// <summary>Проверить, есть ли у навыка конкретный permission.</summary>
    Task<GrantValidationResult> CheckPermissionAsync(string skillId, ToolPermission permission, string? sessionId = null, CancellationToken ct = default);

    /// <summary>Проверить, есть ли у навыка все требуемые permissions.</summary>
    Task<GrantValidationResult> CheckAllPermissionsAsync(string skillId, ToolPermission required, string? sessionId = null, CancellationToken ct = default);
}
