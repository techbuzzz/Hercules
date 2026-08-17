namespace Hercules.SkillSdk;

/// <summary>
///     Role of a participant in an LLM chat.
/// </summary>
public enum SkillLlmRole
{
    System,
    User,
    Assistant
}

/// <summary>
///     One message in an LLM chat request.
/// </summary>
public readonly record struct SkillLlmMessage(SkillLlmRole Role, string Content);

/// <summary>
///     Result of an LLM completion call.
/// </summary>
public sealed class SkillLlmResponse
{
    public string Text { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public string? Error { get; set; }
}
