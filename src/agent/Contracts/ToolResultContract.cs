using System.Text.Json.Serialization;

namespace Hercules.Contracts;

/// <summary>
///     Versioned contract for Agent → LLM tool-result responses.
///     Serialized by <see cref="AgentCore" /> when returning tool execution results into the LLM transcript.
/// </summary>
public sealed class ToolResultContract
{
    public const string CurrentVersion = "1.0";

    /// <summary>true если tool успешно отработал.</summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>Текстовый output (stdout-style). Пустая строка если Success=false.</summary>
    [JsonPropertyName("output")]
    public string Output { get; set; } = "";

    /// <summary>Сообщение об ошибке. Null или пусто если Success=true.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>
    ///     Дополнительные метаданные: HTTP status, duration_ms, blocked_reason, etc.
    ///     Null = без метаданных.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }

    /// <summary>
    ///     RequestId из соответствующего <see cref="ToolCallContract" />.
    ///     Null если вызов был без request_id.
    /// </summary>
    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }

    /// <summary>Версия контракта.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = CurrentVersion;
}
