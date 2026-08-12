using System.Text.Json;
using System.Text.RegularExpressions;
using Hercules.Audit;
using Hercules.Config;
using Hercules.Tools.Approval;
using Microsoft.Extensions.Logging;

namespace Hercules.Tools.Policy;

/// <summary>
///     Политика безопасности для tool execution.
///     Проверяет каждый tool call перед выполнением на основе:
///     - side-effect level и required permissions
///     - deny rules (explicit blocklist)
///     - approval-required rules (high-risk tools)
///     - dry-run mode (log only, не блокирует)
/// </summary>
public sealed class ToolPolicyEngine : IConfigReload
{
    private readonly ToolPolicyConfig _config;
    private readonly ILogger<ToolPolicyEngine> _logger;
    private readonly ToolPermissionSet _permissions;
    private readonly IApprovalService? _approvalService;
    private readonly IAuditService? _auditService;

    /// <summary>
    ///     Реестр descriptors для известных tools.
    ///     Ключ — имя tool'а (lowercase).
    /// </summary>
    private readonly Dictionary<string, ToolDescriptor> _registry = new(StringComparer.OrdinalIgnoreCase);

    public ToolPolicyEngine(
        ToolPolicyConfig config,
        ToolPermissionSet permissions,
        ILogger<ToolPolicyEngine> logger,
        IApprovalService? approvalService = null,
        IAuditService? auditService = null)
    {
        _config = config;
        _permissions = permissions;
        _logger = logger;
        _approvalService = approvalService;
        _auditService = auditService;
    }

    /// <summary>
    ///     Зарегистрировать tool descriptor.
    ///     Вызывается при startup из ToolRegistry или из каждого tool.
    /// </summary>
    public void Register(ToolDescriptor descriptor)
    {
        _registry[descriptor.Name] = descriptor;
        _logger.LogDebug("[Policy] Registered tool descriptor: {Name} (side_effect={Level}, perms={Perms})",
            descriptor.Name, descriptor.SideEffectLevel, descriptor.RequiredPermissions);
    }

    /// <summary>
    ///     Зарегистрировать tool по ITool.
    ///     Использует <see cref="IToolDescriptorProvider" /> если tool реализует интерфейс,
    ///     иначе создаёт default descriptor из ITool.
    /// </summary>
    public void Register(ITool tool)
    {
        if (tool is IToolDescriptorProvider d)
        {
            Register(d.GetPolicyDescriptor());
        }
        else
        {
            var descriptor = new ToolDescriptor
            {
                Name = tool.Name,
                InputSchema = tool.ParametersSchema,
                // Default: external tools → External, WASM/local → Local, others → Read
                SideEffectLevel = InferSideEffectLevel(tool),
                RequiredPermissions = InferPermissions(tool),
            };
            Register(descriptor);
        }
    }

    /// <summary>
    ///     Проверить, разрешён ли tool к исполнению.
    ///     Вызывается из AgentCore перед каждым tool.ExecuteAsync.
    /// </summary>
    /// <param name="ctx">Контекст запроса.</param>
    /// <returns>Результат проверки.</returns>
    public ToolPolicyResult Evaluate(PolicyContext ctx)
    {
        var toolName = ctx.ToolName;

        // 1. Dry-run mode
        if (_config.DryRun)
        {
            _logger.LogDebug("[Policy:DryRun] Would evaluate tool={Name}", toolName);
            return ToolPolicyResult.Allowed(dryRun: true);
        }

        // 2. Explicit deny list
        if (_config.DeniedTools.Count > 0)
        {
            foreach (var pattern in _config.DeniedTools)
            {
                if (MatchesPattern(toolName, pattern))
                {
                    _logger.LogWarning("[Policy] Tool '{Name}' DENIED — matches deny rule '{Pattern}'", toolName, pattern);
                    _ = AuditPolicyDecisionAsync("system", toolName, "Denied", null, ctx.ArgumentsJson, ctx.SessionId);
                    return ToolPolicyResult.Denied($"Tool '{toolName}' is in the deny list (pattern: {pattern})");
                }
            }
        }

        // 3. Unknown tool
        if (!_registry.TryGetValue(toolName, out var descriptor))
        {
            // Unknown tool → allow if policy allows unknown, otherwise deny
            if (_config.AllowUnknownTools)
            {
                _logger.LogDebug("[Policy] Unknown tool '{Name}' — allowed (AllowUnknownTools=true)", toolName);
                return ToolPolicyResult.Allowed();
            }

            _logger.LogWarning("[Policy] Unknown tool '{Name}' — DENIED", toolName);
            _ = AuditPolicyDecisionAsync("system", toolName, "Denied", null, ctx.ArgumentsJson, ctx.SessionId);
            return ToolPolicyResult.UnknownTool(toolName);
        }

        // 4. Side-effect level check
        if (descriptor.SideEffectLevel >= _config.MinSideEffectLevelForApproval)
        {
            _logger.LogDebug("[Policy] Tool '{Name}' requires approval (side_effect={Level})",
                toolName, descriptor.SideEffectLevel);

            // Check if permissions allow this tool
            if (!_permissions.HasAll(descriptor.RequiredPermissions))
            {
                var missing = _permissions.Missing(descriptor.RequiredPermissions);
                _logger.LogWarning("[Policy] Tool '{Name}' DENIED — missing permissions: {Missing}", toolName, missing);
                _ = AuditPolicyDecisionAsync("system", toolName, "Denied", missing.ToString(), ctx.ArgumentsJson, ctx.SessionId);
                return ToolPolicyResult.Denied($"Missing permissions: {missing}");
            }

            // task_010: register approval request and return with request ID
            if (_approvalService is not null)
            {
                var reason = $"Tool '{toolName}' has side_effect_level={descriptor.SideEffectLevel} " +
                             $"which requires human approval (threshold={_config.MinSideEffectLevelForApproval})";
                var approvalResult = _approvalService
                    .RequestAsync(ctx.SessionId ?? "default", toolName, ctx.ArgumentsJson, reason)
                    .GetAwaiter().GetResult();
                _ = AuditPolicyDecisionAsync("system", toolName, "NeedsApproval", descriptor.RequiredPermissions.ToString(), ctx.ArgumentsJson, ctx.SessionId);
                return ToolPolicyResult.NeedsApproval(
                    $"{reason}. Approval request id={approvalResult.RequestId}, status={approvalResult.Status}");
            }

            _ = AuditPolicyDecisionAsync("system", toolName, "NeedsApproval", descriptor.RequiredPermissions.ToString(), ctx.ArgumentsJson, ctx.SessionId);
            return ToolPolicyResult.NeedsApproval(
                $"Tool '{toolName}' has side_effect_level={descriptor.SideEffectLevel} " +
                $"which requires human approval (threshold={_config.MinSideEffectLevelForApproval})");
        }

        // 5. Permission check
        if (!_permissions.HasAll(descriptor.RequiredPermissions))
        {
            var missing = _permissions.Missing(descriptor.RequiredPermissions);
            _logger.LogWarning("[Policy] Tool '{Name}' DENIED — missing permissions: {Missing}", toolName, missing);
            _ = AuditPolicyDecisionAsync("system", toolName, "Denied", missing.ToString(), ctx.ArgumentsJson, ctx.SessionId);
            return ToolPolicyResult.Denied($"Missing permissions: {missing}");
        }

        // 6. Critical tool check (always needs approval)
        if (descriptor.SideEffectLevel == SideEffectLevel.Critical)
        {
            // task_010: register approval request for critical tools
            if (_approvalService is not null)
            {
                var reason = $"Tool '{toolName}' is marked Critical";
                var approvalResult = _approvalService
                    .RequestAsync(ctx.SessionId ?? "default", toolName, ctx.ArgumentsJson, reason)
                    .GetAwaiter().GetResult();
                _ = AuditPolicyDecisionAsync("system", toolName, "NeedsApproval", descriptor.RequiredPermissions.ToString(), ctx.ArgumentsJson, ctx.SessionId);
                return ToolPolicyResult.NeedsApproval(
                    $"{reason}. Approval request id={approvalResult.RequestId}, status={approvalResult.Status}");
            }

            _ = AuditPolicyDecisionAsync("system", toolName, "NeedsApproval", descriptor.RequiredPermissions.ToString(), ctx.ArgumentsJson, ctx.SessionId);
            return ToolPolicyResult.NeedsApproval($"Tool '{toolName}' is marked Critical");
        }

        _logger.LogDebug("[Policy] Tool '{Name}' ALLOWED", toolName);
        _ = AuditPolicyDecisionAsync("system", toolName, "Allowed", descriptor.RequiredPermissions.ToString(), ctx.ArgumentsJson, ctx.SessionId);
        return ToolPolicyResult.Allowed();
    }

    /// <summary>
    ///     Fire-and-forget audit logging of policy decision (task_014).
    ///     Failures are swallowed to prevent policy logic from being affected by audit failures.
    /// </summary>
    private async Task AuditPolicyDecisionAsync(
        string actor,
        string toolName,
        string decision,
        string? permissions,
        string? argsJson,
        string? sessionId)
    {
        if (_auditService is null) return;
        try
        {
            await _auditService.LogToolPolicyDecisionAsync(
                actor, toolName, decision, permissions, argsJson, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Audit] Failed to log tool policy decision for {Tool}", toolName);
        }
    }

    /// <summary>Получить descriptor для tool'а.</summary>
    public ToolDescriptor? GetDescriptor(string toolName) =>
        _registry.TryGetValue(toolName, out var d) ? d : null;

    /// <summary>Получить список всех зарегистрированных tools.</summary>
    public IReadOnlyCollection<ToolDescriptor> GetAllDescriptors() => _registry.Values;

    private static bool MatchesPattern(string toolName, string pattern)
    {
        if (pattern == "*")
        {
            return true;
        }

        if (pattern.StartsWith("*") && pattern.EndsWith("*"))
        {
            return toolName.Contains(pattern.Trim('*'), StringComparison.OrdinalIgnoreCase);
        }

        if (pattern.StartsWith("*"))
        {
            return toolName.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);
        }

        if (pattern.EndsWith("*"))
        {
            return toolName.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(toolName, pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static SideEffectLevel InferSideEffectLevel(ITool tool)
    {
        var name = tool.Name.ToLowerInvariant();
        if (name.Contains("http") || name.Contains("fetch") || name.Contains("request"))
        {
            return SideEffectLevel.External;
        }

        if (name.Contains("file") || name.Contains("write") || name.Contains("shell") || name.Contains("exec"))
        {
            return SideEffectLevel.Local;
        }

        if (name.Contains("delete") || name.Contains("remove") || name.Contains("drop"))
        {
            return SideEffectLevel.Critical;
        }

        if (name.Contains("wasm") || name.Contains("code") || name.Contains("sandbox"))
        {
            return SideEffectLevel.Local;
        }

        return SideEffectLevel.Read;
    }

    private static ToolPermission InferPermissions(ITool tool)
    {
        var name = tool.Name.ToLowerInvariant();
        var perms = ToolPermission.None;

        if (name.Contains("http") || name.Contains("fetch"))
        {
            perms |= ToolPermission.Network;
        }

        if (name.Contains("file") || name.Contains("write"))
        {
            perms |= ToolPermission.Write;
        }

        if (name.Contains("delete") || name.Contains("remove"))
        {
            perms |= ToolPermission.Delete;
        }

        if (name.Contains("shell") || name.Contains("exec") || name.Contains("code"))
        {
            perms |= ToolPermission.Shell;
        }

        if (name.Contains("memory"))
        {
            perms |= ToolPermission.Memory;
        }

        return perms;
    }

    /// <summary>
    ///     Обновить конфигурацию при runtime change.
    ///     Вызывается через IConfigReload.Reload().
    /// </summary>
    void IConfigReload.Reload(AppConfig config)
    {
        // ToolPolicyEngine использует snapshot конфигурации, переданный в конструктор.
        // При обновлении конфигурации пересоздаём engine с новыми настройками.
        _logger.LogInformation("[Policy] Config reload triggered (runtime update not yet supported — requires engine rebuild)");
    }
}

/// <summary>
///     Интерфейс для tool'ов, которые предоставляют свой descriptor.
/// </summary>
public interface IToolWithDescriptor
{
    ToolDescriptor GetDescriptor();
}
