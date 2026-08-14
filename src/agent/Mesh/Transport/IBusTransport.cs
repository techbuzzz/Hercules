namespace Hercules.Mesh.Transport;

/// <summary>
///     Интерфейс адаптера message-bus транспорта.
///     Реализуется для RabbitMQ, NATS и Azure Service Bus.
///     Обеспечивает асинхронную доставку intent'ов в disconnected-средах.
///     Спецификация: task_037.
/// </summary>
public interface IBusTransport : ITransport
{
    /// <summary>
    ///     Тип message-bus: "rabbitmq" | "nats" | "azure-service-bus".
    /// </summary>
    string BusType { get; }

    /// <summary>
    ///     Имя очереди или топика, в который отправляются outbound-сообщения.
    /// </summary>
    string OutboundQueue { get; }

    /// <summary>
    ///     Поддерживает ли bus at-most-once delivery (fire-and-forget).
    /// </summary>
    bool SupportsAtMostOnce { get; }

    /// <summary>
    ///     Поддерживает ли bus at-least-once delivery (acknowledgement + redelivery).
    /// </summary>
    bool SupportsAtLeastOnce { get; }

    /// <summary>
    ///     Подписаться на inbound-очередь для получения intent'ов от других агентов.
    ///     <paramref name="handler"/> вызывается для каждого входящего <see cref="IntentEnvelope"/>.
    ///     Возвращает disposable для отписки.
    /// </summary>
    /// <param name="queueName">Очередь или топик для подписки.</param>
    /// <param name="handler">Обработчик входящего intent'а. Возвращает <see cref="IntentResponse"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Disposable subscription.</returns>
    Task<IDisposable> SubscribeAsync(
        string queueName,
        Func<IntentEnvelope, CancellationToken, Task<IntentResponse>> handler,
        CancellationToken ct = default);

    /// <summary>
    ///     Проверить connectivity до message-bus брокера.
    /// </summary>
    Task<bool> IsConnectedAsync(CancellationToken ct = default);
}

/// <summary>
///     Stub-реализация <see cref="IBusTransport"/> для окружений, где message-bus не сконфигурирован.
///     Возвращает ошибку при любой попытке отправки.
/// </summary>
public sealed class NoopBusTransport : IBusTransport
{
    public TransportKind Kind => TransportKind.Bus;
    public bool SupportsBidirectionalStreaming => false;
    public DeliveryGuarantee DeliveryGuarantee => DeliveryGuarantee.AtMostOnce;
    public string BusType => "none";
    public string OutboundQueue => "";
    public bool SupportsAtMostOnce => false;
    public bool SupportsAtLeastOnce => false;

    public Task<TransportResult> SendAsync(string targetAgentId, IntentEnvelope envelope, CancellationToken ct = default)
    {
        return Task.FromResult(TransportResult.TransportError(
            targetAgentId,
            envelope.TraceId,
            "Message-bus transport not configured. Use HTTP or gRPC.",
            latencyMs: 0,
            TransportKind.Bus));
    }

    public Task<IDisposable> SubscribeAsync(
        string queueName,
        Func<IntentEnvelope, CancellationToken, Task<IntentResponse>> handler,
        CancellationToken ct = default)
    {
        return Task.FromResult<IDisposable>(new NoopSubscription());
    }

    public Task<bool> IsConnectedAsync(CancellationToken ct = default) => Task.FromResult(false);

    public void Dispose() { /* no-op: no managed resources */ }

    private sealed class NoopSubscription : IDisposable
    {
        public void Dispose() { }
    }
}
