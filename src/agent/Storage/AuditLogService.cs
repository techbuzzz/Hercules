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

    /// <summary>
    ///     [task_087] Filterable query with WHERE clauses pushed into the storage
    ///     backend where possible. Each nullable filter on <paramref name="query"/>
    ///     is applied with equality; <c>From</c>/<c>To</c> bound <c>created_at</c>.
    ///     Default implementation loads <c>Limit*4</c> rows from
    ///     <see cref="GetRecentAsync"/> and applies the filters in memory so the
    ///     contract is honoured for any IAuditLog implementation. The SQLite
    ///     <see cref="AuditLogService"/> overrides this default with a SQL query
    ///     that pushes every filter into the WHERE clause.
    /// </summary>
    async Task<IReadOnlyList<AuditLogEntry>> QueryAsync(AuditLogQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Oversample to compensate for in-memory filtering. Cap at 4000 rows so
        // the fallback path can never OOM the audit endpoint even on a busy
        // multi-tenant deployment. The SQLite override sidesteps this entirely.
        var scanLimit = Math.Min(Math.Max(query.EffectiveLimit * 4, 200), 4000);
        var recent = await GetRecentAsync(scanLimit, ct).ConfigureAwait(false);

        IEnumerable<AuditLogEntry> q = recent;
        if (!string.IsNullOrEmpty(query.Actor))     q = q.Where(e => string.Equals(e.Actor, query.Actor, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(query.Action))    q = q.Where(e => string.Equals(e.Action, query.Action, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(query.Target))    q = q.Where(e => string.Equals(e.Target, query.Target, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(query.SessionId)) q = q.Where(e => string.Equals(e.SessionId, query.SessionId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(query.ToolName))  q = q.Where(e => string.Equals(e.ToolName, query.ToolName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(query.Result))    q = q.Where(e => string.Equals(e.Result, query.Result, StringComparison.OrdinalIgnoreCase));
        if (query.From.HasValue)                    q = q.Where(e => e.CreatedAt >= query.From.Value);
        if (query.To.HasValue)                      q = q.Where(e => e.CreatedAt < query.To.Value);

        return q.Take(query.EffectiveLimit).ToList();
    }
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

    /// <summary>
    ///     [task_087] Push filters into SQL. The default in-memory fallback on
    ///     <see cref="IAuditLog.QueryAsync(AuditLogQuery, CancellationToken)"/>
    ///     is replaced with a parameterised query that adds a WHERE clause per
    ///     non-null filter — no row over-fetch, no materialisation cost on the
    ///     SLO/dashboard hot path.
    /// </summary>
    public async Task<IReadOnlyList<AuditLogEntry>> QueryAsync(AuditLogQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await store.GetAuditLogQueryAsync(query, ct);
    }
}
