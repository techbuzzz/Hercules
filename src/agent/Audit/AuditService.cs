using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hercules.Config;
using Hercules.Redaction;
using Hercules.Storage;
using Microsoft.Extensions.Logging;

namespace Hercules.Audit;

/// <summary>
///     Расширенный аудит-сервис (task_014).
///     Обогащает каждое событие requestId, tool name, policy decision, payload hash.
///     Делегирует запись в IAuditLog (task_003).
/// </summary>
public sealed class AuditService : IAuditService
{
    private readonly IAuditLog _auditLog;
    private readonly AuditConfig _config;
    private readonly IRedactionService? _redaction;
    private readonly PayloadHashService _hashService;
    private readonly ILogger<AuditService> _logger;

    public AuditService(
        IAuditLog auditLog,
        AuditConfig config,
        IRedactionService? redaction,
        PayloadHashService hashService,
        ILogger<AuditService> logger)
    {
        _auditLog = auditLog;
        _config = config;
        _redaction = redaction;
        _hashService = hashService;
        _logger = logger;
    }

    public async Task LogToolPolicyDecisionAsync(
        string actor,
        string toolName,
        string policyDecision,
        string? permissions,
        string? argsJson,
        string? sessionId,
        string? requestId = null,
        CancellationToken ct = default)
    {
        var details = argsJson is not null && _redaction is not null
            ? _redaction.Redact(argsJson, sensitivity: "high")
            : argsJson;

        var hash = _config.PayloadHashEnabled && argsJson is not null
            ? _hashService.ComputeHash(argsJson)
            : null;

        await WriteEntryAsync(
            actor, "tool_policy_decision", target: toolName,
            details: details, sessionId: sessionId,
            requestId: requestId, toolName: toolName,
            policyDecision: policyDecision,
            permissionUsed: permissions,
            result: policyDecision.ToLowerInvariant() == "allowed" ? "success" : "blocked",
            payloadHash: hash,
            ct: ct);

        _logger.LogDebug(
            "[Audit] Tool policy decision: actor={Actor} tool={Tool} decision={Decision}",
            actor, toolName, policyDecision);
    }

    public async Task LogToolExecutionAsync(
        string actor,
        string toolName,
        string result,
        string? error,
        string? sessionId,
        string? requestId = null,
        CancellationToken ct = default)
    {
        var details = error is not null && _redaction is not null
            ? _redaction.Redact(error, sensitivity: "high")
            : error;

        await WriteEntryAsync(
            actor, "tool_executed", target: toolName,
            details: details, sessionId: sessionId,
            requestId: requestId, toolName: toolName,
            policyDecision: null,
            permissionUsed: null,
            result: result,
            payloadHash: null,
            ct: ct);

        _logger.LogDebug(
            "[Audit] Tool execution: actor={Actor} tool={Tool} result={Result}",
            actor, toolName, result);
    }

    public async Task LogSkillActionAsync(
        string actor,
        string action,
        string skillId,
        string? details,
        string? sessionId,
        CancellationToken ct = default)
    {
        await WriteEntryAsync(
            actor, $"skill_{action}", target: skillId,
            details: details, sessionId: sessionId,
            requestId: null, toolName: null,
            policyDecision: null,
            permissionUsed: null,
            result: "success",
            payloadHash: null,
            ct: ct);

        _logger.LogInformation(
            "[Audit] Skill action: actor={Actor} action={Action} skill={Skill}",
            actor, action, skillId);
    }

    public async Task LogConfigChangeAsync(
        string actor,
        string configKey,
        string? oldValue,
        string? newValue,
        string? sessionId,
        CancellationToken ct = default)
    {
        var details = newValue is not null && _redaction is not null
            ? _redaction.Redact(newValue, sensitivity: "high")
            : newValue;

        var hash = _config.PayloadHashEnabled && newValue is not null
            ? _hashService.ComputeHash(newValue)
            : null;

        await WriteEntryAsync(
            actor, "config_changed", target: configKey,
            details: details, sessionId: sessionId,
            requestId: null, toolName: null,
            policyDecision: null,
            permissionUsed: null,
            result: "success",
            payloadHash: hash,
            ct: ct);

        _logger.LogInformation(
            "[Audit] Config changed: actor={Actor} key={Key}", actor, configKey);
    }

    public async Task LogApprovalActionAsync(
        string actor,
        string approvalId,
        string action,
        string? toolName,
        string? reason,
        string? sessionId,
        CancellationToken ct = default)
    {
        await WriteEntryAsync(
            actor, $"approval_{action}", target: approvalId,
            details: reason, sessionId: sessionId,
            requestId: null, toolName: toolName,
            policyDecision: null,
            permissionUsed: null,
            result: action.ToLowerInvariant(),
            payloadHash: null,
            ct: ct);

        _logger.LogInformation(
            "[Audit] Approval action: actor={Actor} action={Action} approvalId={Id}",
            actor, action, approvalId);
    }

    public async Task LogAsync(
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
        CancellationToken ct = default)
    {
        if (_config.LogActorActions.Count > 0 &&
            !_config.LogActorActions.Contains(actor, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        await WriteEntryAsync(
            actor, action, target, details, sessionId,
            requestId, toolName, policyDecision, permissionUsed, result, null, ct);
    }

    private async Task WriteEntryAsync(
        string actor,
        string action,
        string? target,
        string? details,
        string? sessionId,
        string? requestId,
        string? toolName,
        string? policyDecision,
        string? permissionUsed,
        string? result,
        string? payloadHash,
        CancellationToken ct)
    {
        if (!_config.Enabled)
        {
            return;
        }

        // Try enriched write first (task_014)
        if (_auditLog is Storage.AuditLogService als)
        {
            await als.LogExAsync(actor, action, target, details, sessionId,
                requestId, toolName, policyDecision, permissionUsed, result, payloadHash, ct);
        }
        else
        {
            // Fallback to basic write (backward compatibility)
            await _auditLog.LogAsync(actor, action, target, details, sessionId, ct);
        }
    }

    public async Task<IReadOnlyList<Storage.AuditLogEntry>> QueryAsync(
        string? actor = null,
        string? action = null,
        string? target = null,
        string? sessionId = null,
        string? toolName = null,
        string? result = null,
        DateTime? from = null,
        DateTime? to = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(target))
        {
            return await _auditLog.GetByTargetAsync(target, limit, ct);
        }

        var all = await _auditLog.GetRecentAsync(limit, ct);
        return all;
    }

    /// <inheritdoc />
    public async Task<AuditActionStats> GetActionStatsAsync(
        string action,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default)
    {
        // task_077: delegate to IAuditLog aggregate to avoid loading 100k rows into memory.
        if (_auditLog is Storage.AuditLogService als)
        {
            var s = await als.GetAuditLogStatsAsync(action, from, to, ct);
            return new AuditActionStats(s.Total, s.Successes, s.Failures, s.Timeouts, s.Denied);
        }

        // Fallback: IAuditLog implementations that don't expose an aggregate still exist;
        // synthesise an AuditActionStats from a bounded in-memory scan (cap 10k rows so we
        // never OOM the SLO endpoint). This preserves the public contract while honouring
        // task_077's "don't load 100k rows" invariant.
        var entries = await _auditLog.GetRecentAsync(10_000, ct);
        var scoped = entries.Where(e => e.Action == action
            && (from is null || e.CreatedAt >= from)
            && (to is null || e.CreatedAt < to)).ToList();

        int total = scoped.Count;
        int successes = scoped.Count(e => string.Equals(e.Result, "success", StringComparison.OrdinalIgnoreCase));
        int failures = scoped.Count(e => !string.IsNullOrEmpty(e.Result) && e.Result.Contains("failure", StringComparison.OrdinalIgnoreCase));
        int timeouts = scoped.Count(e => !string.IsNullOrEmpty(e.Result) && e.Result.Contains("timeout", StringComparison.OrdinalIgnoreCase));
        int denied = scoped.Count(e => !string.IsNullOrEmpty(e.Result) && e.Result.Contains("denied", StringComparison.OrdinalIgnoreCase));

        return new AuditActionStats(total, successes, failures, timeouts, denied);
    }
}
