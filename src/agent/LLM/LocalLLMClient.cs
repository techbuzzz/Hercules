using System.ClientModel;
using Hercules.Config;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Hercules.LLM;

/// <summary>
///     Клиент для Ollama (Cloud или Local) и совместимых серверов (LM Studio)
///     через OpenAI-совместимый интерфейс (/v1).
/// </summary>
public sealed class LocalLLMClient(OllamaConfig cfg, string providerName) : ChatClientLLMClient(BuildChatClient(cfg), providerName, cfg.Model, cfg.Temperature, cfg.MaxTokens)
{
    private static IChatClient BuildChatClient(OllamaConfig cfg)
    {
        var options = new OpenAIClientOptions { Endpoint = new Uri(cfg.Endpoint) };
        // Локальный Ollama не требует ключа, но SDK ожидает непустое значение.
        var credential = new ApiKeyCredential(string.IsNullOrEmpty(cfg.ApiKey)
            ? "ollama"
            : cfg.ApiKey);
        var openAi = new OpenAIClient(credential, options);
        return openAi.GetChatClient(cfg.Model).AsIChatClient();
    }
}
