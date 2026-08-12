namespace Hercules.Audit;

/// <summary>
///     Расширенный аудит-сервис (task_014).
///     Каждое событие содержит: actor, requestId, tool, permission, payload hash, result, timestamp.
/// </summary>
public interface IAuditService
{
    /// <summary>Логировать решение policy engine для tool execution.</summary>
    Task LogToolPolicyDecisionAsync(
        string actor,
        string toolName,
        string policyDecision,  // Allowed | Denied | NeedsApproval
        string? permissions,
        string? argsJson,
        string? sessionId,
        string? requestId = null,
        CancellationToken ct = default);

    /// <summary>Логировать фактическое выполнение tool.</summary>
    Task LogToolExecutionAsync(
        string actor,
        string toolName,
        string result,  // success | failure | timeout | denied
        string? error,
        string? sessionId,
        string? requestId = null,
        CancellationToken ct = default);

    /// <summary>Логировать действие с навыком (create/improve/delete/deprecate/rollback).</summary>
    Task LogSkillActionAsync(
        string actor,
        string action,
        string skillId,
        string? details,
        string? sessionId,
        CancellationToken ct = default);

    /// <summary>Логировать изменение конфигурации.</summary>
    Task LogConfigChangeAsync(
        string actor,
        string configKey,
        string? oldValue,
        string? newValue,
        string? sessionId,
        CancellationToken ct = default);

    /// <summary>Логировать действие с approval request.</summary>
    Task LogApprovalActionAsync(
        string actor,
        string approvalId,
        string action,  // approve | deny | expire | request
        string? toolName,
        string? reason,
        string? sessionId,
        CancellationToken ct = default);

    /// <summary>Универсальный метод для произвольных audit-событий.</summary>
    Task LogAsync(
        string actor,
        string action,
        string? target = null,
        string? details = null,
        string? sessionId = null,
        string? requestId = null,
        string? toolName = null,
        string? policyDecision = null,
        string? permissionUsed = null,
        string? result = null,
        CancellationToken ct = default);

    /// <summary>Query audit log с фильтрами.</summary>
    Task<IReadOnlyList<Storage.AuditLogEntry>> QueryAsync(
        string? actor = null,
        string? action = null,
        string? target = null,
        string? sessionId = null,
        string? toolName = null,
        string? result = null,
        DateTime? from = null,
        DateTime? to = null,
        int limit = 100,
        CancellationToken ct = default);
}
