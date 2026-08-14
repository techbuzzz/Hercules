using Hercules.Cache;
using Hercules.Config;
using Hercules.LLM.Providers;

namespace Hercules.LLM;

/// <summary>
///     Фабрика LLM-клиентов по имени провайдера.
///     Provider clients are cached via ICacheService (task_028) for reuse across requests.
/// </summary>
public sealed class LlmClientFactory : ILLMClientFactory
{
    private readonly LlmConfig _cfg;
    private readonly ICacheService _cache;

    public LlmClientFactory(LlmConfig cfg, ICacheService? cache = null)
    {
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _cache = cache ?? NullCacheService.Instance;
    }

    /// <summary>Создать клиент по имени провайдера. Кэширует результат через ICacheService.</summary>
    public ILLMClient Create(string provider)
    {
        return _cache.GetOrSetAsync<ILLMClient>(
            CacheClass.LlmPromptPrefix,
            $"client:{provider.ToLowerInvariant()}",
            () => Task.FromResult<ILLMClient?>(CreateUncached(provider)),
            CancellationToken.None).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException($"Factory returned null for provider: {provider}");
    }

    /// <summary>Некэшированное создание клиента — виртуальный для переопределения в тестах.</summary>
    public ILLMClient CreateUncached(string provider)
    {
        return provider.ToLowerInvariant() switch
        {
            "yandexgpt" or "yandex" => new YandexGPTClient(_cfg.YandexGpt),
            "ollama-cloud" => new LocalLLMClient(_cfg.OllamaCloud, "ollama-cloud"),
            "ollama-local" or "ollama" => new LocalLLMClient(_cfg.OllamaLocal, "ollama-local"),
            "lmstudio" => new LMStudioClient(new OpenAICompatibleConfig
            {
                Endpoint = _cfg.OpenAICompatible.Endpoint,
                ApiKey = _cfg.OpenAICompatible.ApiKey,
                Model = _cfg.OpenAICompatible.Model,
                Temperature = _cfg.OpenAICompatible.Temperature,
                MaxTokens = _cfg.OpenAICompatible.MaxTokens,
                DisplayName = "LM Studio",
                Description = "LM Studio local inference"
            }),
            "openai-compatible" => new OpenAICompatibleClient(_cfg.OpenAICompatible, "openai-compatible"),
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
