namespace Hercules.Mesh.TaskLifecycle;

/// <summary>
///     Полное состояние delegated (inter-agent) задачи.
///     Включает inter-agent metadata и актуальное состояние.
/// </summary>
/// <param name="TaskId">Локальный task ID (ULID).</param>
/// <param name="ParentRequestId">RequestId оригинального IntentEnvelope.</param>
/// <param name="CallerAgentId">AgentId вызывающего агента.</param>
/// <param name="Intent">Intent/capability, запрошенная вызывающим.</param>
/// <param name="Payload">Оригинальный payload запроса.</param>
/// <param name="State">Текущее состояние delegated задачи.</param>
/// <param name="CreatedAt">UTC timestamp создания.</param>
/// <param name="UpdatedAt">UTC timestamp последнего обновления состояния.</param>
/// <param name="CompletedAt">UTC timestamp завершения (если завершена).</param>
/// <param name="Result">Результат выполнения (JSON-строка, если Completed).</param>
/// <param name="Error">Текст ошибки (если Failed).</param>
/// <param name="CancellationReason">Причина отмены (если Cancelled).</param>
/// <param name="ExpiresAt">UTC deadline задачи.</param>
/// <param name="AwaitingInputContext">Контекст ожидания ввода (если AwaitingInput).</param>
/// <param name="LocalTaskId">Ссылка на локальный DurableTask (nullable).</param>
public sealed record DelegatedTask(
    string TaskId,
    string ParentRequestId,
    string CallerAgentId,
    string Intent,
    string Payload,
    DelegatedTaskState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    string? Result,
    string? Error,
    string? CancellationReason,
    DateTimeOffset? ExpiresAt,
    AwaitingInputContext? AwaitingInputContext,
    string? LocalTaskId);

/// <summary>
///     Контекст ожидания ввода от вызывающего агента.
/// </summary>
/// <param name="RequestedAt">Когда был запрошен ввод.</param>
/// <param name="InputType">Тип ожидаемого ввода (text, file, approval, choice).</param>
/// <param name="Question">Человекочитаемый вопрос / запрос.</param>
/// <param name="Choices">Варианты выбора (если applicable).</param>
public sealed record AwaitingInputContext(
    DateTimeOffset RequestedAt,
    string InputType,
    string? Question,
    List<string>? Choices);
