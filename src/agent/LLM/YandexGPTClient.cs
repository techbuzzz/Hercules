using System.ClientModel;
using Hercules.Config;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Hercules.LLM;

/// <summary>
///     Клиент YandexGPT через OpenAI-совместимый endpoint.
///     Yandex принимает модель в формате URI: gpt://{folderId}/{model}/latest.
/// </summary>
public sealed class YandexGPTClient(YandexGptConfig cfg) : ChatClientLLMClient(BuildChatClient(cfg), "yandexgpt", ResolveModel(cfg), cfg.Temperature, cfg.MaxTokens)
{
    /// <summary>
    ///     Для Yandex имя модели передаётся как gpt://{folderId}/{model}/latest,
    ///     если folderId задан и модель ещё не в формате URI.
    /// </summary>
    private static string ResolveModel(YandexGptConfig cfg)
    {
        if (cfg.Model.StartsWith("gpt://", StringComparison.OrdinalIgnoreCase))
        {
            return cfg.Model;
        }

        return !string.IsNullOrWhiteSpace(cfg.FolderId)
            ? $"gpt://{cfg.FolderId}/{cfg.Model}/latest"
            : cfg.Model;
    }

    private static IChatClient BuildChatClient(YandexGptConfig cfg)
    {
        var options = new OpenAIClientOptions { Endpoint = new Uri(cfg.Endpoint) };
        var credential = new ApiKeyCredential(string.IsNullOrEmpty(cfg.ApiKey)
            ? "no-key"
            : cfg.ApiKey);
        var openAi = new OpenAIClient(credential, options);
        return openAi.GetChatClient(ResolveModel(cfg)).AsIChatClient();
    }
}
