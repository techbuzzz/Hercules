namespace HerculesBus;

/// <summary>
///     Сообщение в HerculesBus. Маршрутизируется по Channel + SenderAgentId.
///     HerculesBus = "мессенджер для ИИ агентов" — агенты общаются между собой
///     через каналы (#main, #wasm-sandbox, #contract-audit и т.п.).
///     НЕ для людей — если нужен человек в loop, кидается ApprovalRequest.
/// </summary>
/// <param name="Id">Уникальный ID сообщения (server-assigned, ULID).</param>
/// <param name="Channel">Канал (#main, #code-execution, #alerts, и т.п.).</param>
/// <param name="SenderAgentId">ID агента-отправителя (e.g. "hercules", "contract-auditor").</param>
/// <param name="SenderName">Human-readable имя (для UI).</param>
/// <param name="Kind">Тип сообщения (text/tool-call/alert/system).</param>
/// <param name="Body">Содержимое (text или JSON).</param>
/// <param name="ReplyTo">ID сообщения, на которое это ответ (для тредов).</param>
/// <param name="Mentions">Список AgentId, которых нужно явно упомянуть (для routing priority).</param>
/// <param name="Attachments">Опциональные вложения (URL-encoded: file refs, base64 blobs).</param>
/// <param name="Timestamp">Server-assigned UTC timestamp.</param>
public sealed record AgentMessage(
    string Id,
    string Channel,
    string SenderAgentId,
    string SenderName,
    string Kind,
    string Body,
    string? ReplyTo = null,
    IReadOnlyList<string>? Mentions = null,
    IReadOnlyList<MessageAttachment>? Attachments = null,
    DateTimeOffset? Timestamp = null)
{
    public DateTimeOffset TimestampOrUtc => Timestamp ?? DateTimeOffset.UtcNow;
}

/// <summary>
///     Вложение к сообщению. Например, результат tool execution, скриншот, log dump.
/// </summary>
public sealed record MessageAttachment(
    string Name,
    string MimeType,
    string Url,
    long SizeBytes = 0);

/// <summary>
///     Тип сообщения. Влияет на UI-рендеринг и приоритет routing.
/// </summary>
public static class MessageKinds
{
    /// <summary>Обычное текстовое сообщение.</summary>
    public const string Text = "text";

    /// <summary>Запрос на tool call (с JSON-схемой параметров в Body).</summary>
    public const string ToolCall = "tool-call";

    /// <summary>Результат tool call.</summary>
    public const string ToolResult = "tool-result";

    /// <summary>Системное событие (agent online/offline, ошибка).</summary>
    public const string System = "system";

    /// <summary>Алерт — высокий приоритет, всегда показывается.</summary>
    public const string Alert = "alert";

    /// <summary>Approval request — запрос одобрения от человека (human-in-the-loop bridge).</summary>
    public const string ApprovalRequest = "approval-request";
}
