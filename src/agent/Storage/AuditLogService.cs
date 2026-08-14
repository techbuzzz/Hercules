namespace Hercules.Storage;

/// <summary>
///     Интерфейс аудит-лога: фиксация действий агента и пользователя.
/// </summary>
public interface IAuditLog
{
    /// <summary>Записать аудит-событие.</summary>
    Task LogAsync(string actor, string action, string? target = null, string? details = null, string? sessionId = null, CancellationToken ct = default);

    /// <summary>Записать расширенное аудит-событие (task_014: requestId, toolName, policyDecision, etc.).</summary>
    Task LogExAsync(
        string actor, string action, string? target, string? details, string? sessionId,
        string? requestId, string? toolName, string? policyDecision,
        string? permissionUsed, string? result, string? payloadHash,
        CancellationToken ct = default);

    /// <summary>Получить последние N записей.</summary>
    Task<IReadOnlyList<AuditLogEntry>> GetRecentAsync(int limit = 100, CancellationToken ct = default);

    /// <summary>Получить записи по target (skill_id, session_id, etc.).</summary>
    Task<IReadOnlyList<AuditLogEntry>> GetByTargetAsync(string target, int limit = 50, CancellationToken ct = default);

    /// <summary>
    ///     task_077: Aggregate counts of audit_log rows filtered by <paramref name="action" />
    ///     and an optional time window. Single SQL aggregate, no row materialisation.
    /// </summary>
    Task<AuditLogStats> GetAuditLogStatsAsync(
        string action,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default);
}

/// <summary>
///     Реализация AuditLogService на основе SqliteSessionStore.
/// </summary>
public sealed class AuditLogService(SqliteSessionStore store) : IAuditLog
{
    public async Task LogAsync(string actor, string action, string? target = null, string? details = null, string? sessionId = null, CancellationToken ct = default)
    {
        await store.LogAuditAsync(actor, action, target, details, sessionId, ct);
    }

    public async Task LogExAsync(
        string actor, string action, string? target, string? details, string? sessionId,
        string? requestId, string? toolName, string? policyDecision,
        string? permissionUsed, string? result, string? payloadHash,
        CancellationToken ct = default)
    {
        await store.LogAuditExAsync(actor, action, target, details, sessionId,
            requestId, toolName, policyDecision, permissionUsed, result, payloadHash, ct);
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetRecentAsync(int limit = 100, CancellationToken ct = default)
    {
        return await store.GetAuditLogAsync(limit, ct);
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetByTargetAsync(string target, int limit = 50, CancellationToken ct = default)
    {
        return await store.GetAuditLogByTargetAsync(target, limit, ct);
    }

    /// <inheritdoc />
    public async Task<AuditLogStats> GetAuditLogStatsAsync(
        string action,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default)
    {
        return await store.GetAuditLogStatsAsync(action, from, to, ct);
    }
}
