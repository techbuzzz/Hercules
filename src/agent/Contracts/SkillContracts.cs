using System.Text.Json.Serialization;

namespace Hercules.Contracts;

/// <summary>
///     Typed contract for LLM skill-creation response.
///     Used by <see cref="Agent.SkillManager.CreateAsync" /> instead of raw JSON parsing.
/// </summary>
public sealed class SkillCreationContract
{
    public const string CurrentVersion = "1.0";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>Фразы-приёмники (ключевые слова/паттерны для роутера).</summary>
    [JsonPropertyName("phrase_receivers")]
    public List<string> PhraseReceivers { get; set; } = new();

    /// <summary>Legacy-поле: поддержка чтения старых ответов с "triggers".</summary>
    [JsonPropertyName("triggers")]
    public List<string>? Triggers { get; set; }

    /// <summary>System prompt для навыка.</summary>
    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = CurrentVersion;
}

/// <summary>
///     Typed contract for LLM skill-improvement response.
///     Used by <see cref="Agent.SkillManager.ImproveAsync" /> instead of raw JSON parsing.
/// </summary>
public sealed class SkillImproveContract
{
    public const string CurrentVersion = "1.0";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = CurrentVersion;
}
