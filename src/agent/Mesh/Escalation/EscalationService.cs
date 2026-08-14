using System.Collections.Concurrent;
using Hercules.Config;
using Hercules.Storage;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Escalation;

/// <summary>
///     Service for managing human-in-the-loop escalations (task_049).
///     Stores pending escalations in memory for fast lookup + persists to SQLite.
/// </summary>
public sealed class EscalationService : IEscalationService
{
    private readonly EscalationConfig _config;
    private readonly SqliteSessionStore _store;
    private readonly ILogger<EscalationService> _logger;

    /// <summary>In-memory cache: escalationId → EscalationResult.</summary>
    private readonly ConcurrentDictionary<string, EscalationResult> _cache = new();

    /// <summary>In-memory index: sessionId → set of escalationIds.</summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> _sessionIndex = new();

    public EscalationService(EscalationConfig config, SqliteSessionStore store, ILogger<EscalationService> logger)
    {
        _config = config;
        _store = store;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<EscalationResult> EscalateAsync(EscalationContext ctx, CancellationToken ct = default)
    {
        if (!_config.Enabled)
        {
            _logger.LogDebug("[Escalation] Escalation disabled — skipping {Type}", ctx.Type);
            var result = new EscalationResult
            {
                EscalationId = $"esc_auto_{Guid.NewGuid():N}",
                RequestId = ctx.RequestId,
                SessionId = ctx.SessionId,
                Type = ctx.Type,
                Severity = ctx.Severity,
                Status = EscalationStatus.Approved, // auto-approve when disabled
                ActionPlan = ctx.ActionPlan,
                Context = ctx.Context,
                PayloadJson = ctx.PayloadJson,
                ToolOrIntentName = ctx.ToolOrIntentName,
                RequestedBy = ctx.RequestedBy,
                CreatedAt = DateTime.UtcNow
            };
            return Task.FromResult(result);
        }

        var escalationId = $"esc_{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;
        var result2 = new EscalationResult
        {
            EscalationId = escalationId,
            RequestId = ctx.RequestId,
            SessionId = ctx.SessionId,
            Type = ctx.Type,
            Severity = ctx.Severity,
            Status = EscalationStatus.Pending,
            ActionPlan = ctx.ActionPlan,
            Context = ctx.Context,
            PayloadJson = ctx.PayloadJson,
            ToolOrIntentName = ctx.ToolOrIntentName,
            RequestedBy = ctx.RequestedBy,
            CreatedAt = now
        };

        // In-memory cache
        _cache[escalationId] = result2;
        _sessionIndex.GetOrAdd(ctx.SessionId, _ => new ConcurrentDictionary<string, bool>())[escalationId] = true;

        // Persist to SQLite (fire and forget)
        _ = PersistAsync(result2, ct);

        var modeStr = _config.Mode;
        _logger.LogInformation(
            "[Escalation] Escalated: id={Id} type={Type} severity={Severity} session={Session} actionPlan={ActionPlan}",
            escalationId, ctx.Type, ctx.Severity, ctx.SessionId,
            ctx.ActionPlan.Length > 60 ? ctx.ActionPlan[..60] + "..." : ctx.ActionPlan);

        if (_config.PageOperatorOnCritical && ctx.Severity == EscalationSeverity.Critical)
        {
            _logger.LogWarning(
                "[Escalation] CRITICAL escalation requires operator attention: id={Id} type={Type}",
                escalationId, ctx.Type);
        }

        return Task.FromResult(result2);
    }

    private async Task PersistAsync(EscalationResult result, CancellationToken ct)
    {
        try
        {
            await _store.SaveEscalationAsync(result, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Escalation] Failed to persist escalation {Id}", result.EscalationId);
        }
    }

    /// <inheritdoc />
    public Task<bool> ApproveAsync(string escalationId, string? resolvedBy = null, CancellationToken ct = default)
    {
        if (!_cache.TryGetValue(escalationId, out var cached))
        {
            _logger.LogWarning("[Escalation] Approve: escalation {Id} not found in cache", escalationId);
            return Task.FromResult(false);
        }

        if (cached.Status != EscalationStatus.Pending)
        {
            _logger.LogWarning("[Escalation] Approve: escalation {Id} already processed (status={Status})",
                escalationId, cached.Status);
            return Task.FromResult(false);
        }

        var updated = cached with { Status = EscalationStatus.Approved, ResolvedAt = DateTime.UtcNow, ResolvedBy = resolvedBy ?? "operator" };
        _cache[escalationId] = updated;

        _ = _store.UpdateEscalationStatusAsync(escalationId, "Approved", resolvedBy ?? "operator", ct);
        _logger.LogInformation("[Escalation] Approved: id={Id} by={By}", escalationId, resolvedBy ?? "operator");
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> DenyAsync(string escalationId, string? resolvedBy = null, CancellationToken ct = default)
    {
        if (!_cache.TryGetValue(escalationId, out var cached))
        {
            _logger.LogWarning("[Escalation] Deny: escalation {Id} not found in cache", escalationId);
            return Task.FromResult(false);
        }

        if (cached.Status != EscalationStatus.Pending)
        {
            _logger.LogWarning("[Escalation] Deny: escalation {Id} already processed (status={Status})",
                escalationId, cached.Status);
            return Task.FromResult(false);
        }

        var updated = cached with { Status = EscalationStatus.Denied, ResolvedAt = DateTime.UtcNow, ResolvedBy = resolvedBy ?? "operator" };
        _cache[escalationId] = updated;

        _ = _store.UpdateEscalationStatusAsync(escalationId, "Denied", resolvedBy ?? "operator", ct);
        _logger.LogInformation("[Escalation] Denied: id={Id} by={By}", escalationId, resolvedBy ?? "operator");
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<int> BatchApproveAsync(IEnumerable<string> escalationIds, string? resolvedBy = null, CancellationToken ct = default)
    {
        var count = 0;
        foreach (var id in escalationIds)
        {
            if (ApproveAsync(id, resolvedBy ?? "operator", ct).GetAwaiter().GetResult())
                count++;
        }
        return Task.FromResult(count);
    }

    /// <inheritdoc />
    public IReadOnlyList<EscalationResult> GetPending(string? sessionId = null, EscalationSeverity? minSeverity = null)
    {
        IEnumerable<KeyValuePair<string, EscalationResult>> source =
            sessionId is null
                ? _cache
                : (_sessionIndex.TryGetValue(sessionId, out var index)
                    ? index.Select(kv => new KeyValuePair<string, EscalationResult>(kv.Key, _cache[kv.Key]))
                    : Enumerable.Empty<KeyValuePair<string, EscalationResult>>());

        return source
            .Where(kv => kv.Value.Status == EscalationStatus.Pending)
            .Where(kv => !minSeverity.HasValue || kv.Value.Severity >= minSeverity.Value)
            .OrderByDescending(kv => kv.Value.Severity)
            .ThenBy(kv => kv.Value.CreatedAt)
            .Select(kv => kv.Value)
            .ToList();
    }

    /// <inheritdoc />
    public EscalationResult? Get(string escalationId)
    {
        return _cache.TryGetValue(escalationId, out var r) ? r : null;
    }

    /// <inheritdoc />
    public bool IsApproved(string escalationId)
    {
        ExpireStale(escalationId);
        return _cache.TryGetValue(escalationId, out var r) && r.Status == EscalationStatus.Approved;
    }

    /// <inheritdoc />
    public bool IsDenied(string escalationId)
    {
        ExpireStale(escalationId);
        return _cache.TryGetValue(escalationId, out var r) && r.Status == EscalationStatus.Denied;
    }

    private void ExpireStale(string escalationId)
    {
        if (!_cache.TryGetValue(escalationId, out var cached) || cached.Status != EscalationStatus.Pending)
            return;

        var cutoff = DateTime.UtcNow.AddMinutes(-_config.DefaultTtlMinutes);
        if (cached.CreatedAt < cutoff)
        {
            var updated = cached with { Status = EscalationStatus.Expired, ResolvedAt = DateTime.UtcNow, ResolvedBy = "system" };
            _cache[escalationId] = updated;
            _ = _store.UpdateEscalationStatusAsync(escalationId, "Expired", "system");
        }
    }

    /// <inheritdoc />
    public async Task ExpireOldAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.AddMinutes(-_config.DefaultTtlMinutes);
        var expired = new List<string>();

        foreach (var (id, result) in _cache)
        {
            if (result.Status == EscalationStatus.Pending && result.CreatedAt < cutoff)
            {
                var updated = result with { Status = EscalationStatus.Expired, ResolvedAt = now, ResolvedBy = "system" };
                _cache[id] = updated;
                expired.Add(id);
            }
        }

        if (expired.Count > 0)
        {
            await _store.ExpireOldEscalationsAsync(_config.DefaultTtlMinutes, ct);
            _logger.LogInformation("[Escalation] Expired {Count} old escalations", expired.Count);
        }
    }
}
