using Hercules.Config;
using Hercules.LLM.Providers;

namespace Hercules.LLM;

/// <summary>
///     Фабрика LLM-клиентов по имени провайдера.
/// </summary>
public class LlmClientFactory(LlmConfig cfg) : ILLMClientFactory
{
    /// <summary>Создать клиент по имени провайдера. Виртуальный для переопределения в тестах.</summary>
    public virtual ILLMClient Create(string provider)
    {
        return provider.ToLowerInvariant() switch
        {
            "yandexgpt" or "yandex" => new YandexGPTClient(cfg.YandexGpt),
            "ollama-cloud" => new LocalLLMClient(cfg.OllamaCloud, "ollama-cloud"),
            "ollama-local" or "ollama" => new LocalLLMClient(cfg.OllamaLocal, "ollama-local"),
            "lmstudio" => new LMStudioClient(new OpenAICompatibleConfig
            {
                Endpoint = cfg.OpenAICompatible.Endpoint,
                ApiKey = cfg.OpenAICompatible.ApiKey,
                Model = cfg.OpenAICompatible.Model,
                Temperature = cfg.OpenAICompatible.Temperature,
                MaxTokens = cfg.OpenAICompatible.MaxTokens,
                DisplayName = "LM Studio",
                Description = "LM Studio local inference"
            }),
            "openai-compatible" => new OpenAICompatibleClient(cfg.OpenAICompatible, "openai-compatible"),
            _ => throw new ArgumentException($"Неизвестный LLM-провайдер: {provider}")
        };
    }
}

/// <summary>
///     Интерфейс фабрики LLM-клиентов. Выделен для тестирования и будущего DI.
/// </summary>
public interface ILLMClientFactory
{
    ILLMClient Create(string provider);
}
