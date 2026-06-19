using Hercules.Config;

namespace Hercules.LLM;

/// <summary>
///     Фабрика LLM-клиентов по имени провайдера.
/// </summary>
public sealed class LlmClientFactory(LlmConfig cfg)
{
    /// <summary>Создать клиент по имени провайдера.</summary>
    public ILLMClient Create(string provider)
    {
        return provider.ToLowerInvariant() switch
        {
            "yandexgpt" or "yandex" => new YandexGPTClient(cfg.YandexGpt),
            "ollama-cloud" => new LocalLLMClient(cfg.OllamaCloud, "ollama-cloud"),
            "ollama-local" or "ollama" or "lmstudio" => new LocalLLMClient(cfg.OllamaLocal, "ollama-local"),
            _ => throw new ArgumentException($"Неизвестный LLM-провайдер: {provider}")
        };
    }
}
