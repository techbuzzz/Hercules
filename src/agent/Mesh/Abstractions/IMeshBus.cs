using Hercules.Mesh.Transport;

namespace Hercules.Mesh.Abstractions;

/// <summary>
///     Pub/sub bus для inter-agent сообщений в mesh.
///     Поддерживает: Publish (fan-out), Subscribe (topic-based), RequestReply (synchronous).
///     Реализации: <see cref="InProcess.InProcessMeshBus"/> (default),
///     Redis/Valkey, NATS, etc. (tasks 067–068).
///     Спецификация: task_066.
/// </summary>
public interface IMeshBus : IDisposable
{
    /// <summary>
    ///     Backend kind: "in-process" | "redis" | "nats" | "postgres".
    /// </summary>
    string BackendKind { get; }

    /// <summary>
    ///     Опубликовать сообщение в topic. Fan-out: все подписчики получат копию.
    ///     Возвращает task, который completed когда сообщение записано в transport.
    /// </summary>
    /// <param name="topic">Имя топика (например, "intent/{capability}" или "events/{eventType}").</param>
    /// <param name="envelope">IntentEnvelope или производное сообщение.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PublishAsync(string topic, IntentEnvelope envelope, CancellationToken ct = default);

    /// <summary>
    ///     Подписаться на топик. Возвращает disposable для отписки.
    ///     Каждый подписчик получает копию каждого сообщения (fan-out).
    /// </summary>
    /// <param name="topic">Имя топика (поддерживает wildcard "events.*" если backend позволяет).</param>
    /// <param name="handler">Обработчик каждого входящего сообщения.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IDisposable> SubscribeAsync(
        string topic,
        Func<IntentEnvelope, CancellationToken, Task> handler,
        CancellationToken ct = default);

    /// <summary>
    ///     Synchronous request/reply: отправить envelope и дождаться ответа.
    ///     Под капотом может использовать reply-to topic или correlation ID.
    ///     Таймаут берётся из <paramref name="envelope"/>.<see cref="IntentEnvelope.Deadline"/>
    ///     или используется <paramref name="defaultTimeout"/>.
    /// </summary>
    /// <param name="targetAgentId">AgentId получателя.</param>
    /// <param name="envelope">Request envelope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="defaultTimeout">Фоллбэк timeout если deadline не задан.</param>
    Task<IntentResponse> RequestReplyAsync(
        string targetAgentId,
        IntentEnvelope envelope,
        CancellationToken ct = default,
        TimeSpan? defaultTimeout = null);

    /// <summary>
    ///     Проверить connectivity до backend-брокера.
    ///     Для in-process всегда возвращает true.
    /// </summary>
    ValueTask<bool> IsHealthyAsync(CancellationToken ct = default);
}
