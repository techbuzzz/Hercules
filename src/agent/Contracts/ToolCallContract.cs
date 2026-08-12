using System.Text.Json.Serialization;

namespace Hercules.Contracts;

/// <summary>
///     Versioned contract for LLM → Agent tool-call requests.
///     Replaces the ad-hoc <c>{"action": "...", "arguments": {...}}</c> regex parsing in <see cref="AgentCore" />.
/// </summary>
public sealed class ToolCallContract
{
    public const string CurrentVersion = "1.0";

    /// <summary>Имя tool'а (lowercase, unique).</summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    /// <summary>
    ///     Именованные аргументы tool'а (десериализуются в конкретный тип в tool implementation).
    ///     Пустой словарь, если tool не принимает параметров.
    /// </summary>
    [JsonPropertyName("arguments")]
    public Dictionary<string, object?> Arguments { get; set; } = new();

    /// <summary>Версия контракта. Используется для future schema evolution.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = CurrentVersion;

    /// <summary>
    ///     Уникальный ID запроса (генерируется агентом, пробрасывается в результат).
    ///     Null = запрос без tracking.
    /// </summary>
    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }
}
