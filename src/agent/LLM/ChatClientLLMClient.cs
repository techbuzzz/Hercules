using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Hercules.LLM;

/// <summary>
///     Базовая реализация <see cref="ILLMClient" /> поверх Microsoft.Extensions.AI <see cref="IChatClient" />.
///     Конкретные провайдеры (YandexGPT, Ollama) создают <see cref="IChatClient" /> и передают его сюда.
/// </summary>
public class ChatClientLLMClient(
    IChatClient chatClient,
    string providerName,
    string modelName,
    float temperature,
    int maxTokens)
    : ILLMClient
{
    public string ProviderName { get; } = providerName;
    public string ModelName { get; } = modelName;

    public async Task<LlmResponse> CompleteAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
    {
        ChatResponse response = await chatClient.GetResponseAsync(Map(messages), BuildOptions(), ct);
        return new LlmResponse(response.Text ?? string.Empty, ProviderName, ModelName);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatTurn> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (ChatResponseUpdate update in chatClient.GetStreamingResponseAsync(Map(messages), BuildOptions(), ct))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }

    private ChatOptions BuildOptions()
    {
        return new ChatOptions
        {
            ModelId = ModelName,
            Temperature = temperature,
            MaxOutputTokens = maxTokens
        };
    }

    private static List<ChatMessage> Map(IReadOnlyList<ChatTurn> messages)
    {
        var result = new List<ChatMessage>(messages.Count);
        foreach (ChatTurn m in messages)
        {
            Microsoft.Extensions.AI.ChatRole role = m.Role switch
            {
                ChatRole.System => Microsoft.Extensions.AI.ChatRole.System,
                ChatRole.Assistant => Microsoft.Extensions.AI.ChatRole.Assistant,
                _ => Microsoft.Extensions.AI.ChatRole.User
            };
            result.Add(new ChatMessage(role, m.Content));
        }

        return result;
    }
}
