using System.Text.Json.Serialization;

namespace Hercules.Contracts;

/// <summary>
///     Versioned contract for structured agent requests (Phase 3 inter-agent / future use).
///     Currently used as a structured input format for the agent loop.
/// </summary>
public sealed class AgentRequestContract
{
    public const string CurrentVersion = "1.0";

    /// <summary>Текстовый запрос пользователя.</summary>
    [JsonPropertyName("input")]
    public string Input { get; set; } = "";

    /// <summary>ID сессии (agent-generated).</summary>
    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = "";

    /// <summary>Уникальный ID запроса (для deduplication и traceability).</summary>
    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }

    /// <summary>
    ///     Опциональный forced skill_id (минуя router).
    ///     Null = стандартная маршрутизация.
    /// </summary>
    [JsonPropertyName("skill_id")]
    public string? SkillId { get; set; }

    /// <summary>Контекстные metadata (client info, preferred language, etc.).</summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }

    /// <summary>Версия контракта.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = CurrentVersion;
}
