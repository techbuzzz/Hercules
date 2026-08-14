namespace Hercules.Tasks;

/// <summary>
///     Статусы долгой (durable) задачи.
/// </summary>
public enum DurableTaskStatus
{
    Pending,
    Running,
    WaitingApproval,
    Completed,
    Failed,
    Cancelled,
    Paused
}

/// <summary>
///     Идентификатор durable задачи.
/// </summary>
public readonly record struct TaskId(Guid Value)
{
    public static TaskId New() => new(Guid.NewGuid());
    public override readonly string ToString() => Value.ToString("N");
    public static implicit operator string(TaskId id) => id.ToString();
    public static implicit operator TaskId(string id) => new(Guid.Parse(id));
}

/// <summary>
///     Metadata durable задачи.
/// </summary>
public sealed record DurableTaskMetadata
{
    /// <summary>Имя навыка, который выполняет задачу.</summary>
    public string? SkillId { get; set; }

    /// <summary>ID сессии агента, в которой задача была создана.</summary>
    public string? SessionId { get; set; }

    /// <summary>Список тегов для фильтрации и категоризации.</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Приоритет: low, normal, high, critical.</summary>
    public string Priority { get; set; } = "normal";

    /// <summary>Имя пользователя или агента, создавшего задачу.</summary>
    public string? CreatedBy { get; set; }

    /// <summary>Описание задачи для человека.</summary>
    public string? Description { get; set; }

    /// <summary>Имя агента-владельца задачи.</summary>
    public string? OwnerAgentId { get; set; }
}

/// <summary>
///     Полное состояние durable задачи.
/// </summary>
public sealed record DurableTask(
    TaskId Id,
    string Name,
    DurableTaskStatus Status,
    DurableTaskMetadata Metadata,
    TaskRetryPolicy RetryPolicy,
    int AttemptCount,
    int CurrentStep,
    string? Result,
    string? Error,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? CompletedAt,
    string? CancellationReason);

/// <summary>
///     Политика retry для задачи.
/// </summary>
public sealed record TaskRetryPolicy
{
    public int MaxRetries { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 1000;
    public double BackoffMultiplier { get; set; } = 2.0;
    public bool UseJitter { get; set; } = true;

    /// <summary>Список error type, которые НЕ подлежат retry (напр. "ValidationError").</summary>
    public List<string> NonRetryableErrors { get; set; } = new();
}

/// <summary>
///     Чекпоинт задачи: snapshot состояния на определённом шаге.
/// </summary>
public sealed record TaskCheckpoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public TaskId TaskId { get; set; }
    public int StepNumber { get; set; }
    public string StateSnapshot { get; set; } = "{}"; // JSON snapshot
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
///     Запрос на создание durable задачи.
/// </summary>
public sealed record CreateTaskRequest
{
    public string Name { get; set; } = "";
    public DurableTaskMetadata? Metadata { get; set; }
    public TaskRetryPolicy? RetryPolicy { get; set; }
    public bool IsDurable { get; set; } = true;
}

/// <summary>
///     Результат выполнения durable задачи.
/// </summary>
public sealed record TaskExecutionResult(
    TaskId TaskId,
    bool Success,
    string? Result,
    string? Error,
    int Attempts,
    TimeSpan Elapsed);
