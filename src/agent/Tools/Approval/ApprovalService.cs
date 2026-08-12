using System.Collections.Concurrent;
using Hercules.Config;
using Hercules.Storage;
using Microsoft.Extensions.Logging;

namespace Hercules.Tools.Approval;

/// <summary>
///     Статус pending-запроса на подтверждение.
/// </summary>
public enum ApprovalStatus
{
    Pending,
    Approved,
    Denied,
    Expired
}

/// <summary>
///     Результат запроса на подтверждение.
/// </summary>
public sealed record ApprovalResult(
    string RequestId,
    string SessionId,
    string ToolName,
    string ArgumentsJson,
    string Reason,
    DateTime RequestedAt,
    ApprovalStatus Status);

/// <summary>
///     Сервис управления approval gates.
///     Хранит pending-запросы в памяти (fast path) + персистит в SQLite.
/// </summary>
public sealed class ApprovalService : IApprovalService
{
    private readonly ApprovalConfig _config;
    private readonly SqliteSessionStore _store;
    private readonly ILogger<ApprovalService> _logger;

    /// <summary>
    ///     In-memory cache: key = requestId, value = ApprovalResult.
    ///     Используется для O(1) lookup при проверке IsApproved().
    /// </summary>
    private readonly ConcurrentDictionary<string, ApprovalResult> _cache = new();

    /// <summary>
    ///     In-memory index: sessionId → set of requestIds (для GetPendingForSession).
    /// </summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> _sessionIndex = new();

    public ApprovalService(ApprovalConfig config, SqliteSessionStore store, ILogger<ApprovalService> logger)
    {
        _config = config;
        _store = store;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ApprovalResult> RequestAsync(
        string sessionId,
        string toolName,
        string argumentsJson,
        string reason,
        string? requestedBy = null,
        CancellationToken ct = default)
    {
        if (!_config.Enabled)
        {
            // Approval disabled → auto-approve
            _logger.LogDebug("[Approval] Approval disabled — auto-approving {Tool}", toolName);
            return new ApprovalResult(
                $"auto-{Guid.NewGuid():N}",
                sessionId,
                toolName,
                argumentsJson,
                reason,
                DateTime.UtcNow,
                ApprovalStatus.Approved);
        }

        var requestId = $"apr_{Guid.NewGuid():N}";
        var result = new ApprovalResult(
            requestId,
            sessionId,
            toolName,
            argumentsJson,
            reason,
            DateTime.UtcNow,
            ApprovalStatus.Pending);

        // In-memory cache
        _cache[requestId] = result;
        _sessionIndex.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, bool>())[requestId] = true;

        // Persist to SQLite
        var dbRecord = new ApprovalRequest(
            requestId,
            sessionId,
            toolName,
            argumentsJson,
            "RequiresApproval",
            reason,
            DateTime.UtcNow,
            requestedBy ?? "agent",
            "Pending",
            null,
            null);
        await _store.SaveApprovalRequestAsync(dbRecord, ct);

        _logger.LogInformation("[Approval] Requested: id={Id} tool={Tool} session={Session}",
            requestId, toolName, sessionId);
        return result;
    }

    /// <inheritdoc />
    public async Task<bool> ApproveAsync(string requestId, CancellationToken ct = default)
    {
        if (!_cache.TryGetValue(requestId, out var cached))
        {
            _logger.LogWarning("[Approval] Approve: request {Id} not found in cache", requestId);
            return false;
        }

        if (cached.Status != ApprovalStatus.Pending)
        {
            _logger.LogWarning("[Approval] Approve: request {Id} already processed (status={Status})", requestId, cached.Status);
            return false;
        }

        var now = DateTime.UtcNow;
        var updated = cached with { Status = ApprovalStatus.Approved };
        _cache[requestId] = updated;

        await _store.UpdateApprovalStatusAsync(requestId, "Approved", now, null, ct);
        _logger.LogInformation("[Approval] Approved: id={Id} tool={Tool}", requestId, cached.ToolName);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DenyAsync(string requestId, CancellationToken ct = default)
    {
        if (!_cache.TryGetValue(requestId, out var cached))
        {
            _logger.LogWarning("[Approval] Deny: request {Id} not found in cache", requestId);
            return false;
        }

        if (cached.Status != ApprovalStatus.Pending)
        {
            _logger.LogWarning("[Approval] Deny: request {Id} already processed (status={Status})", requestId, cached.Status);
            return false;
        }

        var now = DateTime.UtcNow;
        var updated = cached with { Status = ApprovalStatus.Denied };
        _cache[requestId] = updated;

        await _store.UpdateApprovalStatusAsync(requestId, "Denied", null, now, ct);
        _logger.LogInformation("[Approval] Denied: id={Id} tool={Tool}", requestId, cached.ToolName);
        return true;
    }

    /// <inheritdoc />
    public bool IsApproved(string toolName, string sessionId)
    {
        // Expire old entries first
        var now = DateTime.UtcNow;
        var cutoff = now.AddMinutes(-_config.DefaultTtlMinutes);

        // Check if any pending request for this tool+session is Approved
        foreach (var (id, result) in _cache)
        {
            if (result.SessionId != sessionId || result.ToolName != toolName)
                continue;
            if (result.Status == ApprovalStatus.Approved)
                return true;
            if (result.Status == ApprovalStatus.Pending && result.RequestedAt < cutoff)
            {
                // Mark as expired (don't await — fire and forget)
                _ = ExpireAsync(id);
            }
        }

        return false;
    }

    /// <inheritdoc />
    public IReadOnlyList<ApprovalResult> GetPending(string? sessionId = null)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.AddMinutes(-_config.DefaultTtlMinutes);

        IEnumerable<KeyValuePair<string, ApprovalResult>> source =
            sessionId is null
                ? _cache
                : (_sessionIndex.TryGetValue(sessionId, out var index)
                    ? index.Select(kv => new KeyValuePair<string, ApprovalResult>(kv.Key, _cache[kv.Key]))
                    : Enumerable.Empty<KeyValuePair<string, ApprovalResult>>());

        return source
            .Where(kv => kv.Value.Status == ApprovalStatus.Pending)
            .Select(kv => kv.Value)
            .OrderBy(r => r.RequestedAt)
            .ToList();
    }

    /// <inheritdoc />
    public ApprovalResult? Get(string requestId)
    {
        return _cache.TryGetValue(requestId, out var r) ? r : null;
    }

    /// <inheritdoc />
    public async Task ExpireOldApprovalsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.AddMinutes(-_config.DefaultTtlMinutes);
        var expired = new List<string>();

        foreach (var (id, result) in _cache)
        {
            if (result.Status == ApprovalStatus.Pending && result.RequestedAt < cutoff)
            {
                var updated = result with { Status = ApprovalStatus.Expired };
                _cache[id] = updated;
                expired.Add(id);
            }
        }

        if (expired.Count > 0)
        {
            await _store.ExpireOldApprovalsAsync(_config.DefaultTtlMinutes, ct);
            _logger.LogInformation("[Approval] Expired {Count} old approval requests", expired.Count);
        }
    }

    private async Task ExpireAsync(string requestId)
    {
        if (!_cache.TryGetValue(requestId, out var cached) || cached.Status != ApprovalStatus.Pending)
            return;

        var updated = cached with { Status = ApprovalStatus.Expired };
        _cache[requestId] = updated;
        await _store.UpdateApprovalStatusAsync(requestId, "Expired", null, null);
    }
}

/// <summary>
///     Интерфейс approval service для DI и тестируемости.
/// </summary>
public interface IApprovalService
{
    /// <summary>Создать pending-запрос на подтверждение.</summary>
    Task<ApprovalResult> RequestAsync(string sessionId, string toolName, string argumentsJson, string reason, string? requestedBy = null, CancellationToken ct = default);

    /// <summary>Одобрить запрос.</summary>
    Task<bool> ApproveAsync(string requestId, CancellationToken ct = default);

    /// <summary>Отклонить запрос.</summary>
    Task<bool> DenyAsync(string requestId, CancellationToken ct = default);

    /// <summary>
    ///     Проверить, одобрен ли конкретный tool для session.
    ///     Используется в AgentCore перед tool execution.
    /// </summary>
    bool IsApproved(string toolName, string sessionId);

    /// <summary>Получить все pending-запросы (опционально — для конкретной сессии).</summary>
    IReadOnlyList<ApprovalResult> GetPending(string? sessionId = null);

    /// <summary>Получить конкретный запрос по ID.</summary>
    ApprovalResult? Get(string requestId);

    /// <summary>Протухнуть все pending-запросы старше TTL.</summary>
    Task ExpireOldApprovalsAsync(CancellationToken ct = default);
}
