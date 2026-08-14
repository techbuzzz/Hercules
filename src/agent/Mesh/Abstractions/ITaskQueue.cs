namespace Hercules.Mesh.Abstractions;

/// <summary>
///     Durable task queue для распределённого исполнения задач в mesh.
///     Обеспечивает: at-least-once delivery, visibility timeout, dead-letter queue.
///     Реализации: <see cref="InProcess.InProcessTaskQueue"/> (default),
///     Redis/Valkey (task_067), PostgreSQL (task_069).
///     Спецификация: task_066.
/// </summary>
public interface ITaskQueue : IDisposable
{
    /// <summary>
    ///     Backend kind: "in-process" | "redis" | "postgres".
    /// </summary>
    string BackendKind { get; }

    /// <summary>
    ///     Enqueue a task synchronously. Возвращает immediately с task ID.
    ///     Используется для fan-out, где не нужен worker consumption.
    /// </summary>
    /// <param name="task">Task descriptor.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Enqueued task with assigned ID.</returns>
    Task<QueuedTask> EnqueueAsync(MeshTask task, CancellationToken ct = default);

    /// <summary>
    ///     Dequeue the next available task (long-polling). Работает по FIFO.
    ///     Вызывается из worker-цикла. Задача становится invisible на <paramref name="visibilityTimeout"/>.
    ///     Если не ack'd в течение visibility timeout — задача снова visible для других worker'ов.
    /// </summary>
    /// <param name="queueName">Имя очереди.</param>
    /// <param name="visibilityTimeout">Как долго задача невидима для других worker'ов.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Следующая задача или null если timeout истёк.</returns>
    Task<QueuedTask?> DequeueAsync(
        string queueName,
        TimeSpan visibilityTimeout,
        CancellationToken ct = default);

    /// <summary>
    ///     Acknowledge successful processing. Задача удаляется из очереди.
    /// </summary>
    /// <param name="taskId">ID задачи.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AckAsync(string taskId, CancellationToken ct = default);

    /// <summary>
    ///     Mark task as failed. Если <paramref name="retry"/> меньше <see cref="MeshTask.MaxRetries"/>,
    ///     задача re-enqueues; иначе — отправляется в dead-letter queue.
    /// </summary>
    /// <param name="taskId">ID задачи.</param>
    /// <param name="reason">Причина ошибки.</param>
    /// <param name="retry">Текущий номер retry (0-based).</param>
    /// <param name="ct">Cancellation token.</param>
    Task FailAsync(string taskId, string reason, int retry, CancellationToken ct = default);

    /// <summary>
    ///     Получить задачи из dead-letter queue.
    /// </summary>
    /// <param name="queueName">DLQ name (обычно "{queueName}-dlq").</param>
    /// <param name="limit">Max возвращаемых записей.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<QueuedTask>> GetDeadLetterQueueAsync(
        string queueName,
        int limit = 100,
        CancellationToken ct = default);

    /// <summary>
    ///     Requeue a task from dead-letter queue обратно в рабочую очередь.
    /// </summary>
    /// <param name="taskId">ID задачи.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RequeueDeadLetterAsync(string taskId, CancellationToken ct = default);

    /// <summary>
    ///     Проверить connectivity до queue backend.
    /// </summary>
    ValueTask<bool> IsHealthyAsync(CancellationToken ct = default);
}

/// <summary>
///     Mesh task descriptor — передаётся в очередь и обратно в worker.
/// </summary>
public sealed record MeshTask
{
    /// <summary>Уникальный ID задачи. Присваивается queue при enqueue.</summary>
    public string Id { get; set; } = "";

    /// <summary>Имя очереди.</summary>
    public string QueueName { get; set; } = "";

    /// <summary>AgentId ответственного агента (если задан).</summary>
    public string? AssignedAgentId { get; set; }

    /// <summary>Intent/capability, которую нужно вызвать.</summary>
    public string Intent { get; set; } = "";

    /// <summary>Payload — JSON-строка с параметрами задачи.</summary>
    public string Payload { get; set; } = "";

    /// <summary>Максимум retry попыток. Default: 3.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Delay перед первым retry (null = сразу).</summary>
    public TimeSpan? RetryDelay { get; set; }

    /// <summary>Deadline — после этого времени задача считается stale.</summary>
    public DateTimeOffset? Deadline { get; set; }

    /// <summary>Trace ID для distributed tracing.</summary>
    public string? TraceId { get; set; }

    /// <summary>Корневой RequestId для распределённой трассировки.</summary>
    public string? RootRequestId { get; set; }

    /// <summary>Metadata для расширений (priority, tags, etc.).</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
///     Задача в очереди с runtime metadata.
/// </summary>
public sealed class QueuedTask
{
    /// <summary>Уникальный ID задачи.</summary>
    public required string Id { get; init; }

    /// <summary>Receipt handle — используется для ack/fail.</summary>
    public required string ReceiptHandle { get; init; }

    /// <summary>Десериализованная задача.</summary>
    public required MeshTask Task { get; init; }

    /// <summary>Время постановки в очередь.</summary>
    public required DateTimeOffset EnqueuedAt { get; init; }

    /// <summary>Номер текущего retry (0 = первая попытка).</summary>
    public int RetryCount { get; set; }

    /// <summary>Число ack/nack, которые были received.</summary>
    public int DeliveryCount { get; set; }
}
