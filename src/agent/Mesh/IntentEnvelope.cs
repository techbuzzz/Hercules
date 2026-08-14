using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hercules.Mesh.Schema;
using HerculesBus.Core;

namespace Hercules.Mesh;

/// <summary>
///     Версионированный JSON delegation envelope — стандартный формат сообщений между агентами в mesh.
///     Спецификация: docs/AGENT-MESH-RU.md §4.
///     Переносим поверх HTTP, gRPC или шины сообщений.
/// </summary>
public sealed class IntentEnvelope
{
    /// <summary>Текущая версия формата envelope (semver).</summary>
    public const string CurrentVersion = "1.0";

    /// <summary>Уникальный ID запроса (ULID или UUID).</summary>
    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = "";

    /// <summary>ID распределённой трассировки (для observability).</summary>
    [JsonPropertyName("trace_id")]
    public string? TraceId { get; set; }

    /// <summary>
    ///     Ключ идемпотентности. Если указан, receiver должен игнорировать повторные запросы
    ///     с тем же ключом в течение configured window.
    /// </summary>
    [JsonPropertyName("idempotency_key")]
    public string? IdempotencyKey { get; set; }

    /// <summary>AgentId отправителя.</summary>
    [JsonPropertyName("sender")]
    public string Sender { get; set; } = "";

    /// <summary>
    ///     AgentId получателя. Null означает "маршрутизируй по intent".
    /// </summary>
    [JsonPropertyName("recipient")]
    public string? Recipient { get; set; }

    /// <summary>Имя capability/intent (e.g. "csharp-refactor").</summary>
    [JsonPropertyName("intent")]
    public string Intent { get; set; } = "";

    /// <summary>Полезная нагрузка — произвольный JSON (строка).</summary>
    [JsonPropertyName("payload")]
    public string Payload { get; set; } = "";

    /// <summary>
    ///     Запрошенная схема ответа. Позволяет sender'у указать ожидаемый формат.
    /// </summary>
    [JsonPropertyName("response_schema")]
    public ResponseSchema? ResponseSchema { get; set; }

    /// <summary>
    ///     Endpoint для асинхронного ответа (callback URL или queue name).
    ///     Опционально — для sync responses sender может игнорировать.
    /// </summary>
    [JsonPropertyName("reply_to")]
    public string? ReplyTo { get; set; }

    /// <summary>
    ///     Deadline (absolute UTC time) — когда запрос должен быть обработан.
    ///     Null означает default timeout.
    /// </summary>
    [JsonPropertyName("deadline")]
    public DateTimeOffset? Deadline { get; set; }

    /// <summary>
    ///     Auth context для delegation: токен, claims, глубина делегации.
    /// </summary>
    [JsonPropertyName("auth")]
    public AuthContext? Auth { get; set; }

    /// <summary>
    ///     Версия формата envelope (semver).
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = CurrentVersion;

    /// <summary>
    ///     UTC timestamp создания конверта.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    ///     Backward-compatible constructor (for existing code using positional args).
    ///     Maps: requestId, sender, intent, payload, replyTo, timeoutMs, traceId.
    /// </summary>
    [Obsolete("Use IntentEnvelope.Create<T> or property initializer instead.")]
    public IntentEnvelope(
        string requestId,
        string sender,
        string intent,
        string payload,
        string? replyTo = null,
        int timeoutMs = 30_000,
        string? traceId = null)
    {
        RequestId = requestId;
        Sender = sender;
        Intent = intent;
        Payload = payload;
        ReplyTo = replyTo;
        TraceId = traceId;
        Deadline = timeoutMs > 0 ? DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs) : null;
        Version = CurrentVersion;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>Default constructor for JSON deserialization.</summary>
    public IntentEnvelope()
    {
    }

    /// <summary>Получить deadline или вычислить из TimeoutMs.</summary>
    public DateTimeOffset GetDeadlineOrDefault(int defaultTimeoutMs = 30_000)
    {
        return Deadline ?? DateTimeOffset.UtcNow.AddMilliseconds(defaultTimeoutMs);
    }

    /// <summary>Проверить, не истёк ли deadline.</summary>
    public bool IsExpired => Deadline.HasValue && DateTimeOffset.UtcNow > Deadline.Value;

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

    /// <summary>
    ///     Создать envelope с typed payload (сериализует payload в JSON).
    /// </summary>
    public static IntentEnvelope Create<TPayload>(
        string requestId,
        string sender,
        string intent,
        TPayload payload,
        string? recipient = null,
        string? replyTo = null,
        int? timeoutMs = null,
        string? traceId = null,
        string? idempotencyKey = null,
        ResponseSchema? responseSchema = null,
        AuthContext? auth = null)
    {
        var payloadJson = payload is string s ? s : JsonSerializer.Serialize(payload, IntentJsonOpts.Instance);
        var deadline = timeoutMs.HasValue
            ? DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs.Value)
            : (DateTimeOffset?)null;

        return new IntentEnvelope
        {
            RequestId = requestId,
            Sender = sender,
            Recipient = recipient,
            Intent = intent,
            Payload = payloadJson,
            ReplyTo = replyTo,
            Deadline = deadline,
            TraceId = traceId,
            IdempotencyKey = idempotencyKey,
            ResponseSchema = responseSchema,
            Auth = auth,
            Version = CurrentVersion,
            Timestamp = DateTimeOffset.UtcNow
        };
    }
}

/// <summary>
///     Ответ на inter-agent intent.
/// </summary>
/// <param name="RequestId">Совпадает с RequestId запроса.</param>
/// <param name="Status">"ok" | "error" | "timeout" | "rejected" | "schema_mismatch".</param>
/// <param name="Agent">AgentId ответившего агента.</param>
/// <param name="Mode">Режим ответа: "skill" | "direct" | "tool".</param>
/// <param name="Skill">Имя использованного навыка (если mode=skill).</param>
/// <param name="Result">Результат — произвольный JSON (строка).</param>
/// <param name="Confidence">Уверенность 0..1.</param>
/// <param name="Error">Текст ошибки (если status=error/rejected).</param>
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

    public static IntentResponse SchemaMismatch(string requestId, string agent, string expectedSchema, string? traceId = null)
    {
        return new IntentResponse(
            requestId,
            "schema_mismatch",
            agent,
            Error: $"Response does not match expected schema: {expectedSchema}",
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
