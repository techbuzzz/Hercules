using System.Text.Encodings.Web;
using System.Text.Json;
using HerculesBus.Core;

namespace Hercules.Mesh;

/// <summary>
///     Межагентный JSON-конверт (intent envelope) — стандартный формат сообщений между агентами в mesh.
///     Спецификация: docs/AGENT-MESH-RU.md §4.
///     Переносим поверх HTTP, gRPC или шины сообщений.
/// </summary>
/// <param name="RequestId">Уникальный ID запроса (ULID или UUID).</param>
/// <param name="Sender">AgentId отправителя.</param>
/// <param name="Intent">Намерение (имя capability, e.g. "csharp-refactor").</param>
/// <param name="Payload">Полезная нагрузка — произвольный JSON (строка).</param>
/// <param name="ReplyTo">Endpoint для асинхронного ответа (опционально).</param>
/// <param name="TimeoutMs">Таймаут в мс (0 = без лимита).</param>
/// <param name="TraceId">ID распределённой трассировки (для observability).</param>
/// <param name="Timestamp">UTC timestamp создания конверта.</param>
public sealed record IntentEnvelope(
    string RequestId,
    string Sender,
    string Intent,
    string Payload,
    string? ReplyTo = null,
    int TimeoutMs = 30_000,
    string? TraceId = null,
    DateTimeOffset? Timestamp = null)
{
    public DateTimeOffset TimestampOrUtc => Timestamp ?? DateTimeOffset.UtcNow;

    /// <summary>Сериализовать в JSON.</summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, IntentJsonOpts.Instance);
    }

    /// <summary>Десериализовать из JSON.</summary>
    public static IntentEnvelope? FromJson(string json)
    {
        return JsonSerializer.Deserialize<IntentEnvelope>(json, IntentJsonOpts.Instance);
    }
}

/// <summary>
///     Ответ на inter-agent intent.
/// </summary>
/// <param name="RequestId">Совпадает с RequestId запроса.</param>
/// <param name="Status">"ok" | "error" | "timeout" | "rejected".</param>
/// <param name="Agent">AgentId ответившего агента.</param>
/// <param name="Mode">Режим ответа: "skill" | "direct" | "tool".</param>
/// <param name="Skill">Имя использованного навыка (если mode=skill).</param>
/// <param name="Result">Результат — произвольный JSON (строка).</param>
/// <param name="Confidence">Уверенность 0..1.</param>
/// <param name="Error">Текст ошибки (если status=error).</param>
/// <param name="TraceId">ID трассировки (совпадает с запросом).</param>
/// <param name="Timestamp">UTC timestamp ответа.</param>
public sealed record IntentResponse(
    string RequestId,
    string Status,
    string Agent,
    string Mode = "direct",
    string? Skill = null,
    string? Result = null,
    double? Confidence = null,
    string? Error = null,
    string? TraceId = null,
    DateTimeOffset? Timestamp = null)
{
    public DateTimeOffset TimestampOrUtc => Timestamp ?? DateTimeOffset.UtcNow;

    public bool IsSuccess => Status == "ok";

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, IntentJsonOpts.Instance);
    }

    public static IntentResponse? FromJson(string json)
    {
        return JsonSerializer.Deserialize<IntentResponse>(json, IntentJsonOpts.Instance);
    }

    public static IntentResponse Ok(string requestId, string agent, string result, string mode = "direct",
        string? skill = null, double? confidence = null, string? traceId = null)
    {
        return new IntentResponse(
            requestId,
            "ok",
            agent,
            mode,
            skill,
            result,
            confidence,
            TraceId: traceId);
    }

    public static IntentResponse Failed(string requestId, string agent, string error, string? traceId = null)
    {
        return new IntentResponse(
            requestId,
            "error",
            agent,
            Error: error,
            TraceId: traceId);
    }

    public static IntentResponse TimedOut(string requestId, string agent, string? traceId = null)
    {
        return new IntentResponse(
            requestId,
            "timeout",
            agent,
            Error: "Request timed out",
            TraceId: traceId);
    }

    public static IntentResponse Rejected(string requestId, string agent, string reason, string? traceId = null)
    {
        return new IntentResponse(
            requestId,
            "rejected",
            agent,
            Error: reason,
            TraceId: traceId);
    }
}

/// <summary>
///     Утилита для генерации ULID-подобных RequestId.
///     Использует HerculesBus.Core.Ulid для совместимости.
/// </summary>
public static class IntentIds
{
    public static string NewRequestId()
    {
        return Ulid.NewId();
    }
}

internal static class IntentJsonOpts
{
    public static readonly JsonSerializerOptions Instance = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
