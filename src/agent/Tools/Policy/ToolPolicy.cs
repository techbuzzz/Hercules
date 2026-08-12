using System.Text.Json.Serialization;

namespace Hercules.Tools.Policy;

/// <summary>
///     Уровень побочных эффектов tool'а.
///     Определяет, какой ущерб может нанести tool при некорректном использовании.
/// </summary>
public enum SideEffectLevel
{
    /// <summary>Tool только читает данные, не изменяя состояние системы.</summary>
    None = 0,

    /// <summary>Tool читает данные из внешних источников (HTTP GET, file read).</summary>
    Read = 1,

    /// <summary>Tool локально изменяет файлы, создаёт процессы, пишет в память.</summary>
    Local = 2,

    /// <summary>Tool делает исходящие сетевые вызовы, отправляет данные.</summary>
    External = 3,

    /// <summary>
    ///     Tool может навсегда удалить данные, запустить финансовые операции,
    ///     получить доступ к оборудованию или секретам.
    /// </summary>
    Critical = 4
}

/// <summary>
///     Типы разрешений, которые может запрашивать tool.
/// </summary>
[Flags]
public enum ToolPermission
{
    None = 0,
    Read = 1 << 0,
    Write = 1 << 1,
    Delete = 1 << 2,
    Network = 1 << 3,
    Shell = 1 << 4,
    Financial = 1 << 5,
    Hardware = 1 << 6,
    Memory = 1 << 7,
}

/// <summary>
///     Descriptor tool'а для policy engine.
///     Каждый <see cref="ITool" /> предоставляет этот descriptor,
///     на основе которого <see cref="ToolPolicyEngine" /> принимает решение
///     об исполнении.
/// </summary>
public sealed class ToolDescriptor
{
    /// <summary>Имя tool'а (должно совпадать с ITool.Name).</summary>
    public string Name { get; init; } = "";

    /// <summary>JSON Schema входных параметров (null = без параметров).</summary>
    public string? InputSchema { get; init; }

    /// <summary>JSON Schema выходных данных (null = результат не структурирован).</summary>
    public string? OutputSchema { get; init; }

    /// <summary>Уровень побочных эффектов.</summary>
    public SideEffectLevel SideEffectLevel { get; init; } = SideEffectLevel.None;

    /// <summary>Набор требуемых разрешений.</summary>
    public ToolPermission RequiredPermissions { get; init; } = ToolPermission.None;

    /// <summary>
    ///     Таймаут выполнения в секундах.
    ///     Если 0 — используется default из конфигурации.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 0;

    /// <summary>
    ///     Максимальное число retry при неудаче (0 = без retry).
    ///     Решает сам tool, но policy проверяет валидность значения.
    /// </summary>
    public int MaxRetries { get; init; } = 0;

    /// <summary>
    ///     Если true — повторный вызов с теми же аргументами безопасен (idempotent).
    ///     Политика может использовать это для retry decisioning.
    /// </summary>
    public bool Idempotent { get; init; } = false;
}

/// <summary>
///     Результат проверки policy для одного tool call.
/// </summary>
public enum PolicyDecision
{
    /// <summary>Tool разрешён к исполнению.</summary>
    Allowed,

    /// <summary>Tool запрещён.</summary>
    Denied,

    /// <summary>Tool требует подтверждения человека (human-in-the-loop).</summary>
    RequiresApproval,

    /// <summary>Tool не найден в реестре — неявный deny.</summary>
    UnknownTool,
}

/// <summary>
///     Результат проверки policy engine.
/// </summary>
public sealed record ToolPolicyResult(
    PolicyDecision Decision,
    string? DeniedReason = null,
    bool DryRun = false)
{
    public bool IsAllowed => Decision == PolicyDecision.Allowed;
    public bool IsDenied => Decision == PolicyDecision.Denied || Decision == PolicyDecision.UnknownTool;
    public bool RequiresApproval => Decision == PolicyDecision.RequiresApproval;

    public static ToolPolicyResult Allowed(bool dryRun = false) =>
        new(PolicyDecision.Allowed, null, dryRun);

    public static ToolPolicyResult Denied(string reason) =>
        new(PolicyDecision.Denied, reason);

    public static ToolPolicyResult NeedsApproval(string reason) =>
        new(PolicyDecision.RequiresApproval, reason);

    public static ToolPolicyResult UnknownTool(string toolName) =>
        new(PolicyDecision.UnknownTool, $"Tool '{toolName}' is not registered in the policy registry");
}

/// <summary>
///     Контекст запроса для policy evaluation.
/// </summary>
public sealed class PolicyContext
{
    /// <summary>Имя tool'а.</summary>
    public string ToolName { get; init; } = "";

    /// <summary>JSON-аргументы вызова (для анализа).</summary>
    public string ArgumentsJson { get; init; } = "{}";

    /// <summary>Текущий session ID.</summary>
    public string? SessionId { get; init; }

    /// <summary>Уже одобренные tool'ы в этом сеансе (для chaining detection).</summary>
    public IReadOnlyList<string> ApprovedToolsThisSession { get; init; } = [];
}
