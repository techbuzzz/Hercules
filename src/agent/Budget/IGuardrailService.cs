namespace Hercules.Budget;

/// <summary>
///     Сервис учёта и проверки guardrail-лимитов.
///     Интегрируется в AgentCore для предотвращения превышения бюджета.
/// </summary>
public interface IGuardrailService
{
    /// <summary>
    ///     Проверить все лимиты перед выполнением запроса.
    ///     Возвращает violations с HasHardViolation=true, если нужен hard stop.
    /// </summary>
    GuardrailCheckResult CheckLimits(string sessionId, int estimatedTokens = 0);

    /// <summary>
    ///     Записать фактическое использование после LLM-вызова.
    /// </summary>
    void RecordLlmUsage(string sessionId, int inputTokens, int outputTokens, decimal costUsd, string provider);

    /// <summary>
    ///     Записать вызов инструмента.
    /// </summary>
    void RecordToolCall(string sessionId);

    /// <summary>
    ///     Записать ретрай инструмента.
    /// </summary>
    void RecordToolRetry(string sessionId);

    /// <summary>
    ///     Записать elapsed time (wall-clock).
    /// </summary>
    void RecordElapsedTime(string sessionId, long elapsedMs);

    /// <summary>
    ///     Получить статус всех лимитов для сессии.
    /// </summary>
    IReadOnlyList<GuardrailStatus> GetStatus(string sessionId);

    /// <summary>
    ///     Сбросить счётчики запроса (вызывается после каждого HandleAsync).
    /// </summary>
    void ResetRequestCounters(string sessionId);

    /// <summary>
    ///     Получить счётчики запроса для чтения.
    /// </summary>
    RequestCounters GetRequestCounters(string sessionId);
}
