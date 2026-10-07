namespace Hercules.SkillSdk;

/// <summary>
///     Role of a participant in an LLM chat.
/// </summary>
public enum SkillLlmRole
{
    /// <summary>System / instruction prompt that frames the conversation.</summary>
    System,

    /// <summary>Input authored by the caller.</summary>
    User,

    /// <summary>Prior model turn being replayed into the conversation.</summary>
    Assistant
}

/// <summary>
///     One message in an LLM chat request.
/// </summary>
/// <param name="Role">Who authored the message.</param>
/// <param name="Content">Message text.</param>
public readonly record struct SkillLlmMessage(SkillLlmRole Role, string Content);

/// <summary>
///     Result of an LLM completion call.
/// </summary>
public sealed class SkillLlmResponse
{
    /// <summary>Generated text, or empty when <see cref="Error"/> is set.</summary>
    public string Text { get; set; } = "";

    /// <summary>Provider that served the request (may differ from the configured one after fallback).</summary>
    public string Provider { get; set; } = "";

    /// <summary>Model identifier that produced the response.</summary>
    public string Model { get; set; } = "";

    /// <summary>Prompt tokens consumed, or <c>0</c> when the provider did not report usage.</summary>
    public int InputTokens { get; set; }

    /// <summary>Completion tokens produced, or <c>0</c> when the provider did not report usage.</summary>
    public int OutputTokens { get; set; }

    /// <summary>Failure description, or <c>null</c> on success.</summary>
    public string? Error { get; set; }
}