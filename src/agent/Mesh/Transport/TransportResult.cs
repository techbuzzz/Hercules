namespace Hercules.Mesh.Transport;

/// <summary>
///     Результат отправки <see cref="IntentEnvelope"/> через транспорт.
///     Содержит либо успешный ответ, либо информацию об ошибке.
/// </summary>
/// <param name="Response">Успешный ответ. Null если IsSuccess = false.</param>
/// <param name="IsSuccess">Успешно ли завершилась операция.</param>
/// <param name="Kind">Тип ошибки, если неуспешно.</param>
/// <param name="ErrorMessage">Человекочитаемое сообщение об ошибке.</param>
/// <param name="LatencyMs">Latency операции в миллисекундах.</param>
/// <param name="TransportKind">Транспорт, которым был отправлен запрос.</param>
public sealed record TransportResult(
    IntentResponse? Response,
    bool IsSuccess,
    TransportErrorKind Kind = TransportErrorKind.None,
    string? ErrorMessage = null,
    long LatencyMs = 0,
    TransportKind TransportKind = TransportKind.Http)
{
    /// <summary>Успешный результат.</summary>
    public static TransportResult Ok(IntentResponse response, long latencyMs, TransportKind kind)
        => new(response, true, TransportErrorKind.None, null, latencyMs, kind);

    /// <summary>Таймаут.</summary>
    public static TransportResult TimedOut(string targetAgentId, string? traceId, long latencyMs, TransportKind kind)
        => new(
            IntentResponse.TimedOut("", targetAgentId, traceId),
            false,
            TransportErrorKind.Timeout,
            "Request timed out",
            latencyMs,
            kind);

    /// <summary>Недоступный endpoint.</summary>
    public static TransportResult Unreachable(string targetAgentId, string? traceId, string details, long latencyMs, TransportKind kind)
        => new(
            IntentResponse.Failed("", targetAgentId, details, traceId),
            false,
            TransportErrorKind.Unreachable,
            details,
            latencyMs,
            kind);

    /// <summary>Транспортная ошибка (network, protocol, etc.).</summary>
    public static TransportResult TransportError(string targetAgentId, string? traceId, string message, long latencyMs, TransportKind kind)
        => new(
            IntentResponse.Failed("", targetAgentId, message, traceId),
            false,
            TransportErrorKind.TransportError,
            message,
            latencyMs,
            kind);

    /// <summary>Peer отклонил запрос (policy, auth, etc.).</summary>
    public static TransportResult Rejected(string targetAgentId, string? traceId, string reason, long latencyMs, TransportKind kind)
        => new(
            IntentResponse.Rejected("", targetAgentId, reason, traceId),
            false,
            TransportErrorKind.Rejected,
            reason,
            latencyMs,
            kind);
}

/// <summary>
///     Категория транспортной ошибки.
/// </summary>
public enum TransportErrorKind
{
    None,
    Timeout,
    Unreachable,
    TransportError,
    Rejected,
    SchemaMismatch,
}
