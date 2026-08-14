using System.Collections.Concurrent;
using Hercules.Cache;
using Hercules.Config;
using Hercules.LLM.Providers;

namespace Hercules.LLM;

/// <summary>
///     Фабрика LLM-клиентов по имени провайдера.
///     Provider clients are cached via ICacheService (task_028) for reuse across requests.
///     task_077: cache uses a sync in-memory lookup so the sync <see cref="Create" /> path
///     never blocks on async I/O. Async callers can use <see cref="CreateAsync" />.
/// </summary>
public sealed class LlmClientFactory : ILLMClientFactory
{
    private readonly LlmConfig _cfg;
    private readonly ICacheService _cache;
    private readonly ConcurrentDictionary<string, ILLMClient> _localCache = new(StringComparer.OrdinalIgnoreCase);

    public LlmClientFactory(LlmConfig cfg, ICacheService? cache = null)
    {
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _cache = cache ?? NullCacheService.Instance;
    }

    /// <summary>Создать клиент по имени провайдера. Кэширует результат в локальном concurrent-кеше.</summary>
    public ILLMClient Create(string provider)
    {
        // task_077: a sync, lock-free cache lookup avoids the sync-over-async
        // `.GetAwaiter().GetResult()` that previously pinned a thread-pool worker.
        return _localCache.GetOrAdd(provider, p => CreateUncached(p));
    }

    /// <summary>Async вариант для async-hot-path callers; использует ICacheService, если он подключён.</summary>
    public async Task<ILLMClient> CreateAsync(string provider, CancellationToken ct = default)
    {
        if (_localCache.TryGetValue(provider, out var cached))
            return cached;

        var fromService = await _cache.GetOrSetAsync<ILLMClient>(
            CacheClass.LlmPromptPrefix,
            $"client:{provider.ToLowerInvariant()}",
            () => Task.FromResult<ILLMClient?>(CreateUncached(provider)),
            ct).ConfigureAwait(false);

        if (fromService is not null)
            _localCache[provider] = fromService;

        return fromService ?? throw new InvalidOperationException($"Factory returned null for provider: {provider}");
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
