using System.Text.Json.Serialization;
using Hercules.Tools.Policy;

namespace Hercules.Tools.Grants;

/// <summary>
///     Scope определяет, как долго действует grant.
/// </summary>
public enum GrantScope
{
    /// <summary>Grant действует в рамках одного skill (default).</summary>
    Skill = 0,

    /// <summary>Grant действует в рамках одной session.</summary>
    Session = 1,

    /// <summary>Grant действует глобально для всех навыков и сессий.</summary>
    Global = 2
}

/// <summary>
///     Grant, выданный навыку.
/// </summary>
public sealed class SkillGrant
{
    /// <summary>Уникальный ID grant.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>ID навыка, которому выдан grant.</summary>
    public string SkillId { get; set; } = "";

    /// <summary>Выданный permission (enum flags).</summary>
    public ToolPermission Permission { get; set; } = ToolPermission.None;

    /// <summary>Scope grant: Skill / Session / Global.</summary>
    public GrantScope Scope { get; set; } = GrantScope.Skill;

    /// <summary>Session ID, если Scope=Session.</summary>
    public string? SessionId { get; set; }

    /// <summary>Кто выдал grant (agent/system/user).</summary>
    public string Grantor { get; set; } = "system";

    /// <summary>Когда выдан.</summary>
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Когда истекает (null = без срока).</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Активен ли grant.</summary>
    public bool IsActive => ExpiresAt is null || ExpiresAt > DateTime.UtcNow;
}

/// <summary>
///     Результат проверки grant.
/// </summary>
public sealed class GrantValidationResult
{
    /// <summary>Валиден ли grant.</summary>
    public bool IsValid { get; set; }

    /// <summary>Какие permissions фактически есть.</summary>
    public ToolPermission GrantedPermissions { get; set; } = ToolPermission.None;

    /// <summary>Каких permissions не хватает.</summary>
    public ToolPermission MissingPermissions { get; set; } = ToolPermission.None;

    /// <summary>Grant истёк.</summary>
    public bool IsExpired { get; set; }

    /// <summary>Grant не найден.</summary>
    public bool NotFound { get; set; }

    /// <summary>Scope grant.</summary>
    public GrantScope Scope { get; set; } = GrantScope.Skill;

    /// <summary>Сообщение об ошибке.</summary>
    public string? Message { get; set; }

    public static GrantValidationResult Valid(ToolPermission granted, GrantScope scope) =>
        new() { IsValid = true, GrantedPermissions = granted, Scope = scope };

    public static GrantValidationResult Invalid(string message, ToolPermission granted = ToolPermission.None, ToolPermission missing = ToolPermission.None) =>
        new() { IsValid = false, GrantedPermissions = granted, MissingPermissions = missing, Message = message };

    public static GrantValidationResult NotFoundResult(string skillId) =>
        new() { IsValid = false, NotFound = true, Message = $"No grants found for skill '{skillId}'" };
}

/// <summary>
///     Запрос на выдачу grant.
/// </summary>
public sealed class GrantRequest
{
    /// <summary>ID навыка.</summary>
    public string SkillId { get; set; } = "";

    /// <summary>Permission в виде строки ("Read", "Write|Network", etc.).</summary>
    public string Permission { get; set; } = "";

    /// <summary>Scope: Skill / Session / Global.</summary>
    public GrantScope Scope { get; set; } = GrantScope.Skill;

    /// <summary>Session ID, если Scope=Session.</summary>
    public string? SessionId { get; set; }

    /// <summary>Срок действия в минутах (0 = без срока).</summary>
    public int DurationMinutes { get; set; } = 0;

    /// <summary>Кто выдаёт (agent/system/user).</summary>
    public string Grantor { get; set; } = "system";
}
