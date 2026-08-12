namespace Hercules.Budget;

/// <summary>
///     Типы guardrail-лимитов.
/// </summary>
public enum GuardrailLimitType
{
    /// <summary>Максимум токенов (input + output) на один запрос.</summary>
    TokensPerRequest,

    /// <summary>Максимум вызовов инструментов на один запрос.</summary>
    ToolCallsPerRequest,

    /// <summary>Максимум ретраев одного инструмента на запрос.</summary>
    RetriesPerTool,

    /// <summary>Максимум wall-clock секунд на один запрос.</summary>
    WallClockSecondsPerRequest,

    /// <summary>Максимум USD на один день.</summary>
    CostPerDay,

    /// <summary>Максимум токенов (input + output) на один день.</summary>
    TokensPerDay,

    /// <summary>Максимум LLM-вызовов на один день.</summary>
    CallsPerDay,
}

/// <summary>
///     Текущий статус одного guardrail-лимита.
/// </summary>
/// <param name="Type">Тип лимита.</param>
/// <param name="Limit">Абсолютный лимит (0 = без лимита).</param>
/// <param name="Current">Текущее потребление.</param>
/// <param name="Remaining">Осталось до лимита.</param>
/// <param name="IsExceeded">Лимит превышен.</param>
/// <param name="IsHardCap">Лимит имеет hard-cap enforcement.</param>
public sealed record GuardrailStatus(
    GuardrailLimitType Type,
    long Limit,
    long Current,
    long Remaining,
    bool IsExceeded,
    bool IsHardCap);

/// <summary>
///     Нарушение guardrail-лимита.
/// </summary>
/// <param name="Type">Тип нарушенного лимита.</param>
/// <param name="Limit">Значение лимита.</param>
/// <param name="Actual">Фактическое потребление.</param>
/// <param name="Message">Человеко-читаемое сообщение.</param>
/// <param name="EnforcementMode">soft_warn или hard_cap.</param>
public sealed record GuardrailViolation(
    GuardrailLimitType Type,
    long Limit,
    long Actual,
    string Message,
    string EnforcementMode);

/// <summary>
///     Результат проверки всех guardrails.
/// </summary>
/// <param name="Violations">Список нарушений.</param>
/// <param name="HasHardViolation">Есть ли хотя бы одно hard-cap нарушение.</param>
public sealed record GuardrailCheckResult(
    IReadOnlyList<GuardrailViolation> Violations,
    bool HasHardViolation);

/// <summary>
///     Счётчики использования для одного запроса (in-memory).
/// </summary>
public sealed class RequestCounters
{
    public int ToolCalls { get; set; }
    public int RetriesForCurrentTool { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public int TokensUsed { get; set; }
}
