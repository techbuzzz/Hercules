using System.Text.Json.Serialization;

namespace Hercules.Contracts;

/// <summary>
///     Versioned contract for structured agent responses.
///     Used for typed API responses and inter-agent protocol.
/// </summary>
public sealed class AgentResponseContract
{
    public const string CurrentVersion = "1.0";

    /// <summary>Текст ответа агенту.</summary>
    [JsonPropertyName("answer")]
    public string Answer { get; set; } = "";

    /// <summary>Режим ответа: "direct" | "skill" | "tool".</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "direct";

    /// <summary>Уверенность: "high" | "medium" | "low".</summary>
    [JsonPropertyName("confidence")]
    public string Confidence { get; set; } = "medium";

    /// <summary>ID использованного навыка. Null при direct-ответе.</summary>
    [JsonPropertyName("used_skill_id")]
    public string? UsedSkillId { get; set; }

    /// <summary>Имя использованного tool'а. Null если tool не использовался.</summary>
    [JsonPropertyName("tool_used")]
    public string? ToolUsed { get; set; }

    /// <summary>RequestId из соответствующего <see cref="AgentRequestContract" />.</summary>
    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }

    /// <summary>Провайдер LLM, ответивший на запрос.</summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "";

    /// <summary>
    ///     Предложение создать навык (input запроса). Null если не предлагается.
    /// </summary>
    [JsonPropertyName("propose_skill")]
    public string? ProposeSkill { get; set; }

    /// <summary>
    ///     Предложение улучшить навык: ID.
    /// </summary>
    [JsonPropertyName("propose_improve_id")]
    public string? ProposeImproveId { get; set; }

    /// <summary>Версия контракта.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = CurrentVersion;
}
