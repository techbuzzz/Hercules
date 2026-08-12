using System.ClientModel;
using Hercules.Config;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Hercules.LLM.Providers;

/// <summary>
///     Клиент для любого OpenAI-совместимого провайдера (LM Studio, LiteLLM, FastChat, etc.).
///     Использует стандартный OpenAI SDK с кастомным base URL.
/// </summary>
public sealed class OpenAICompatibleClient(OpenAICompatibleConfig cfg, string providerName)
    : ChatClientLLMClient(
        BuildChatClient(cfg),
        providerName,
        cfg.Model,
        cfg.Temperature,
        cfg.MaxTokens)
{
    /// <summary>Display name из конфигурации.</summary>
    public string DisplayName => cfg.DisplayName;

    private static IChatClient BuildChatClient(OpenAICompatibleConfig cfg)
    {
        var options = new OpenAIClientOptions { Endpoint = new Uri(cfg.Endpoint) };
        var credential = new ApiKeyCredential(
            string.IsNullOrEmpty(cfg.ApiKey) ? "no-key" : cfg.ApiKey);
        var openAi = new OpenAIClient(credential, options);
        return openAi.GetChatClient(cfg.Model).AsIChatClient();
    }
}
