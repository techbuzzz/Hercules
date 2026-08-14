using Hercules.Mesh.Schema;

namespace Hercules.Mesh.TaskLifecycle;

/// <summary>
///     Inter-agent task lifecycle protocol.
///     Управляет состоянием delegated задач: accepted, working, awaiting-input,
///     completed, failed, cancelled, expired.
///     Вызывающий агент может poll, receive callback, или получать push-уведомления
///     (в зависимости от transport capabilities).
/// </summary>
public interface ITaskLifecycleProtocol
{
    /// <summary>
    ///     Принять delegated задачу к исполнению.
    ///     Создаёт запись в протоколе и возвращает assigned taskId.
    /// </summary>
    Task<DelegatedTask> AcceptAsync(
        string parentRequestId,
        string callerAgentId,
        string intent,
        string payload,
        AuthContext? auth = null,
        DateTimeOffset? deadline = null,
        CancellationToken ct = default);

    /// <summary>
    ///     Обновить состояние delegated задачи.
    ///     Валидирует, что переход разрешён из текущего состояния.
    /// </summary>
    Task<DelegatedTask> UpdateStateAsync(
        string taskId,
        DelegatedTaskState newState,
        CancellationToken ct = default);

    /// <summary>
    ///     Перевести в AwaitingInput с контекстом ожидания.
    /// </summary>
    Task<DelegatedTask> AwaitInputAsync(
        string taskId,
        string inputType,
        string? question,
        List<string>? choices,
        CancellationToken ct = default);

    /// <summary>
    ///     Получить текущее состояние delegated задачи.
    /// </summary>
    Task<DelegatedTask?> GetStateAsync(string taskId, CancellationToken ct = default);

    /// <summary>
    ///     Завершить задачу с результатом (переводит в Completed).
    /// </summary>
    Task<DelegatedTask> CompleteAsync(
        string taskId,
        string result,
        CancellationToken ct = default);

    /// <summary>
    ///     Завершить задачу с ошибкой (переводит в Failed).
    /// </summary>
    Task<DelegatedTask> FailAsync(
        string taskId,
        string error,
        CancellationToken ct = default);

    /// <summary>
    ///     Отменить задачу (переводит в Cancelled).
    /// </summary>
    Task<DelegatedTask> CancelAsync(
        string taskId,
        string? reason,
        CancellationToken ct = default);

    /// <summary>
    ///     Отметить задачу истёкшей (проверяет deadline).
    ///     Возвращает true если задача была помечена как Expired.
    /// </summary>
    Task<bool> CheckExpiredAsync(string taskId, CancellationToken ct = default);

    /// <summary>
    ///     Зафиксировать получение ввода от вызывающего агента (callback).
    ///     Переводит задачу из AwaitingInput → Working.
    /// </summary>
    Task<DelegatedTask> RecordInputAsync(
        string taskId,
        string input,
        CancellationToken ct = default);

    /// <summary>
    ///     Привязать локальный DurableTask к delegated задаче.
    /// </summary>
    Task<DelegatedTask> BindLocalTaskAsync(
        string taskId,
        string localTaskId,
        CancellationToken ct = default);

    /// <summary>
    ///     Long-poll: ожидать смены состояния из AwaitingInput.
    ///     Возвращает обновлённую DelegatedTask когда состояние изменится.
    ///     Таймаут определяется min(deadline, pollTimeoutMs).
    /// </summary>
    Task<DelegatedTask?> PollForInputDeliveryAsync(
        string taskId,
        int pollTimeoutMs,
        CancellationToken ct = default);

    /// <summary>
    ///     Уведомить вызывающего агента о смене состояния (callback).
    ///     Использует IntentTransport для отправки callback'а.
    /// </summary>
    Task NotifyStateChangeAsync(
        string taskId,
        CancellationToken ct = default);
}
