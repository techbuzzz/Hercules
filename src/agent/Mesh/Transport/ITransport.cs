namespace Hercules.Mesh.Transport;

/// <summary>
///     Lookup для <see cref="AgentManifest"/> по agentId.
///     Реализуется <see cref="CapabilityRegistry"/>.
/// </summary>
public interface ICapabilityLookup
{
    /// <summary>Попытка получить манифест агента. Null если не найден.</summary>
    AgentManifest? TryGet(string agentId);
}

/// <summary>
///     Тип транспорта для inter-agent коммуникации.
/// </summary>
public enum TransportKind
{
    /// <summary>HTTP/REST — синхронный request/response.</summary>
    Http = 0,

    /// <summary>gRPC — bi-directional streaming, lower latency.</summary>
    Grpc = 1,

    /// <summary>Очередь сообщений (RabbitMQ / NATS / Azure Service Bus).</summary>
    Bus = 2,
}

/// <summary>
///     Delivery guarantee, которую обеспечивает транспорт.
/// </summary>
public enum DeliveryGuarantee
{
    /// <summary>At-most-once: fire-and-forget, no redelivery.</summary>
    AtMostOnce,

    /// <summary>At-least-once: messages are acknowledged, may be redelivered.</summary>
    AtLeastOnce,

    /// <summary>Exactly-once: deduplication на уровне транспорта.</summary>
    ExactlyOnce,
}

/// <summary>
///     Интерфейс транспорта для отправки <see cref="IntentEnvelope"/> peer-агенту.
///     Поддерживает HTTP, gRPC и message-bus адаптеры.
///     Спецификация: docs/ROADMAP-RU.md Phase 3 #15, task_037.
/// </summary>
public interface ITransport : IDisposable
{
    /// <summary>Тип транспорта.</summary>
    TransportKind Kind { get; }

    /// <summary>
    ///     Поддерживает ли транспорт двунаправленный стриминг (gRPC streaming).
    ///     Для HTTP и Bus всегда false.
    /// </summary>
    bool SupportsBidirectionalStreaming { get; }

    /// <summary>
    ///     Какую гарантию доставки обеспечивает транспорт.
    /// </summary>
    DeliveryGuarantee DeliveryGuarantee { get; }

    /// <summary>
    ///     Отправить <paramref name="envelope"/> указанному peer-агенту и получить ответ.
    ///     Для async-транспортов (Bus) возвращает task, который completed когда сообщение
    ///     отправлено в очередь (не когда пришёл ответ — для этого используется reply-to).
    /// </summary>
    /// <param name="targetAgentId">AgentId получателя (из CapabilityRegistry).</param>
    /// <param name="envelope">Делегация envelope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Результат с ответом или ошибкой.</returns>
    Task<TransportResult> SendAsync(string targetAgentId, IntentEnvelope envelope, CancellationToken ct = default);
}
