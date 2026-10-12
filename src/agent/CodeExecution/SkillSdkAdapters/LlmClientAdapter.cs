using Hercules.LLM;
using Hercules.SkillSdk;
using Microsoft.Extensions.Logging;

namespace Hercules.CodeExecution.SkillSdkAdapters;

/// <summary>
///     Agent-side implementation of <see cref="ILlmClient"/> for file-based skills.
///     Routes through the agent's configured ILLMClient.
/// </summary>
public sealed class LlmClientAdapter : ILlmClient
{
    private readonly ILLMClient _client;
    private readonly ILogger _logger;

    public LlmClientAdapter(ILLMClient client, ILogger logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<SkillLlmResponse> CompleteAsync(IReadOnlyList<SkillLlmMessage> messages, CancellationToken ct = default)
    {
        var turns = messages.Select(m => new ChatTurn((ChatRole)m.Role, m.Content)).ToList();
        try
        {
            var result = await _client.CompleteAsync(turns, ct);
            _logger.LogInformation("SkillSdk LLM completion: {Provider}/{Model} ({In}/{Out} tokens)",
                result.Provider, result.Model, result.InputTokens, result.OutputTokens);

            return new SkillLlmResponse
            {
                Text = result.Text,
                Provider = result.Provider,
                Model = result.Model,
                InputTokens = result.InputTokens,
                OutputTokens = result.OutputTokens
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SkillSdk LLM completion failed");
            return new SkillLlmResponse { Error = $"LLM error: {ex.GetType().Name}: {ex.Message}" };
        }
    }

    public Task<SkillLlmResponse> PromptAsync(string prompt, CancellationToken ct = default)
    {
        return CompleteAsync(new[]
        {
            new SkillLlmMessage(SkillLlmRole.User, prompt)
        }, ct);
    }
}
