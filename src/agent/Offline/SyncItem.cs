using System.Text.Json;
using HerculesBus.Core;

namespace Hercules.Offline;

/// <summary>
///     Тип элемента в offline outbox.
/// </summary>
public enum OutboxItemType
{
    SensorLog,
    TaskResult,
    Alert
}

/// <summary>
///     Статус обработки элемента.
/// </summary>
public enum OutboxItemStatus
{
    Pending,
    Synced,
    Failed
}

/// <summary>
///     Приоритет элемента (higher = flushed first).
/// </summary>
public enum OutboxItemPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

/// <summary>
///     Один элемент в offline outbox-очереди.
///     Сериализуется как JSON-строка в колонку payload.
/// </summary>
public sealed class OutboxItem
{
    public long Id { get; set; }

    /// <summary>
    ///     Уникальный идентификатор (ULID string) для deduplication.
    ///     Не инициализируется здесь — каждая factory-метод
    ///     (<see cref="SensorLog"/>, <see cref="TaskResult"/>, <see cref="Alert"/>)
    ///     генерирует свой ULID. Default-инициализатор был убран в task_073,
    ///     потому что он создавал ULID при каждом `new OutboxItem()` (даже для
    ///     unit-тестов и mock-данных), а factory-методы всё равно перезаписывали
    ///     это значение, теряя первое поколение ID.
    /// </summary>
    public string ItemId { get; set; } = string.Empty;

    public OutboxItemType Type { get; set; }

    /// <summary>JSON-encoded payload.</summary>
    public string Payload { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public OutboxItemPriority Priority { get; set; } = OutboxItemPriority.Normal;

    public OutboxItemStatus Status { get; set; } = OutboxItemStatus.Pending;

    public int RetryCount { get; set; } = 0;

    public string? LastError { get; set; }

    /// <summary>
    ///     Необязательный session ID для traceability.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    ///     Необязательный correlation ID для ordering.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    ///     Создать item для sensor log.
    /// </summary>
    public static OutboxItem SensorLog(string sessionId, string correlationId, object payload)
        => new()
        {
            ItemId = Ulid.NewId(),
            Type = OutboxItemType.SensorLog,
            Payload = System.Text.Json.JsonSerializer.Serialize(payload),
            CreatedAt = DateTime.UtcNow,
            Priority = OutboxItemPriority.Normal,
            SessionId = sessionId,
            CorrelationId = correlationId
        };

    /// <summary>
    ///     Создать item для результата задачи.
    /// </summary>
    public static OutboxItem TaskResult(string sessionId, string correlationId, object payload)
        => new()
        {
            ItemId = Ulid.NewId(),
            Type = OutboxItemType.TaskResult,
            Payload = System.Text.Json.JsonSerializer.Serialize(payload),
            CreatedAt = DateTime.UtcNow,
            Priority = OutboxItemPriority.High,
            SessionId = sessionId,
            CorrelationId = correlationId
        };

    /// <summary>
    ///     Создать item для alert.
    /// </summary>
    public static OutboxItem Alert(string sessionId, string correlationId, object payload)
        => new()
        {
            ItemId = Ulid.NewId(),
            Type = OutboxItemType.Alert,
            Payload = System.Text.Json.JsonSerializer.Serialize(payload),
            CreatedAt = DateTime.UtcNow,
            Priority = OutboxItemPriority.Critical,
            SessionId = sessionId,
            CorrelationId = correlationId
        };
}

/// <summary>
///     Результат flush-операции.
/// </summary>
public sealed record FlushResult(
    int TotalProcessed,
    int SuccessCount,
    int FailureCount,
    int SkippedCount,
    List<string> Errors);
