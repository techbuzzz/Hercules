namespace Hercules.SkillSdk;

/// <summary>
///     Safe LLM client for file-based skills.
///     Routes through the agent's configured LLM providers.
/// </summary>
public interface ILlmClient
{
    /// <summary>
    ///     Send a chat completion request.
    ///     Uses the agent's "main" role provider by default.
    /// </summary>
    Task<SkillLlmResponse> CompleteAsync(IReadOnlyList<SkillLlmMessage> messages, CancellationToken ct = default);

    /// <summary>
    ///     Send a simple single-prompt completion request.
    /// </summary>
    Task<SkillLlmResponse> PromptAsync(string prompt, CancellationToken ct = default);
}
