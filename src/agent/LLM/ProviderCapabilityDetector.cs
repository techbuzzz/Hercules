using System.Text.Json;
using Hercules.Cache;
using Hercules.Config;
using Microsoft.Extensions.Logging;

namespace Hercules.LLM;

/// <summary>
///     Detector возможностей LLM-провайдера.
///     Определяет vision, function calling, streaming, max tokens из /v1/models или конфига.
///     Results are cached via ICacheService (task_028).
/// </summary>
public sealed class ProviderCapabilityDetector
{
    /// <summary>Имя named HttpClient-клиента для LLM capability detection (task_078).</summary>
    public const string HttpClientName = "llm-capabilities";

    private readonly LlmConfig _cfg;
    private readonly ILogger<ProviderCapabilityDetector>? _logger;
    private readonly ICacheService _cache;
    private readonly IHttpClientFactory? _httpFactory;

    public ProviderCapabilityDetector(
        LlmConfig cfg,
        ILogger<ProviderCapabilityDetector>? logger = null,
        ICacheService? cache = null)
        : this(cfg, logger, cache, httpFactory: null)
    {
    }

    /// <summary>DI-friendly конструктор (task_078): named-клиент "llm-capabilities".</summary>
    public ProviderCapabilityDetector(
        LlmConfig cfg,
        ILogger<ProviderCapabilityDetector>? logger,
        ICacheService? cache,
        IHttpClientFactory? httpFactory)
    {
        _cfg = cfg;
        _logger = logger;
        _cache = cache ?? NullCacheService.Instance;
        _httpFactory = httpFactory;
    }

    private HttpClient ResolveClient()
    {
        if (_httpFactory is not null)
        {
            return _httpFactory.CreateClient(HttpClientName);
        }
        return new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    }

    /// <summary>Detect capabilities for a specific provider (cached).</summary>
    public async Task<ProviderCapabilities> DetectAsync(string provider, CancellationToken ct = default)
    {
        var result = await _cache.GetOrSetAsync(
            CacheClass.LlmCapability,
            $"provider:{provider}",
            () => InnerDetectAsync(provider, ct),
            ct);

        return result ?? new ProviderCapabilities { Provider = provider };
    }

    private async Task<ProviderCapabilities?> InnerDetectAsync(string provider, CancellationToken ct)
    {
        try
        {
            return provider.ToLowerInvariant() switch
            {
                "yandexgpt" or "yandex" => await DetectFromOpenAIEndpointAsync("yandexgpt", _cfg.YandexGpt.Endpoint, _cfg.YandexGpt.Model, ct),
                "ollama-cloud" => await DetectOllamaModelsAsync("ollama-cloud", _cfg.OllamaCloud.Endpoint, _cfg.OllamaCloud.Model, ct),
                "ollama-local" or "ollama" => await DetectOllamaModelsAsync("ollama-local", _cfg.OllamaLocal.Endpoint, _cfg.OllamaLocal.Model, ct),
                "lmstudio" => await DetectFromOpenAIEndpointAsync("lmstudio", _cfg.OpenAICompatible.Endpoint, _cfg.OpenAICompatible.Model, ct),
                "openai-compatible" => await DetectFromOpenAIEndpointAsync("openai-compatible", _cfg.OpenAICompatible.Endpoint, _cfg.OpenAICompatible.Model, ct),
                _ => new ProviderCapabilities { Provider = provider }
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Capability detection failed for '{Provider}'", provider);
            return new ProviderCapabilities { Provider = provider };
        }
    }

    /// <summary>Detect capabilities for all configured providers.</summary>
    public async Task<IReadOnlyList<ProviderCapabilities>> DetectAllAsync(CancellationToken ct = default)
    {
        var providers = new[] { _cfg.Provider }
            .Concat(_cfg.Fallback)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var tasks = providers.Select(p => DetectAsync(p, ct));
        var results = await Task.WhenAll(tasks);
        return results;
    }

    private async Task<ProviderCapabilities> DetectFromOpenAIEndpointAsync(
        string name, string endpoint, string configuredModel, CancellationToken ct)
    {
        using var http = ResolveClient();
        var baseUrl = endpoint.TrimEnd('/').Replace("/v1", "");
        var modelsUrl = $"{baseUrl}/v1/models";

        try
        {
            using var response = await http.GetAsync(modelsUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                return BuildDefault(name, configuredModel);
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);

            var modelIds = new List<string>();
            int maxCtx = -1;
            bool hasVision = false;
            bool hasFunctionCalling = false;

            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                    {
                        var id = idProp.GetString() ?? "";
                        modelIds.Add(id);

                        // Detect capabilities from model id/name heuristics
                        var lower = id.ToLowerInvariant();
                        if (lower.Contains("vision") || lower.Contains("gpt-4o") || lower.Contains("claude-3"))
                            hasVision = true;
                        if (!lower.Contains("instruct") && !lower.Contains("chat"))
                            hasFunctionCalling = true;
                    }

                    if (item.TryGetProperty("context_window_tokens", out var ctxProp))
                        maxCtx = Math.Max(maxCtx, ctxProp.TryGetInt32(out var v) ? v : -1);
                }
            }

            return new ProviderCapabilities
            {
                Provider = name,
                Models = modelIds,
                Model = configuredModel,
                Vision = hasVision,
                FunctionCalling = hasFunctionCalling,
                Streaming = true,
                MaxContextTokens = maxCtx
            };
        }
        catch
        {
            return BuildDefault(name, configuredModel);
        }
    }

    private async Task<ProviderCapabilities> DetectOllamaModelsAsync(
        string name, string endpoint, string configuredModel, CancellationToken ct)
    {
        using var http = ResolveClient();
        var baseUrl = endpoint.TrimEnd('/').Replace("/v1", "");
        var tagsUrl = $"{baseUrl}/api/tags";

        try
        {
            using var response = await http.GetAsync(tagsUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                return BuildDefault(name, configuredModel);
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);

            var modelIds = new List<string>();
            if (doc.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in models.EnumerateArray())
                {
                    if (item.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                    {
                        modelIds.Add(nameProp.GetString() ?? "");
                    }
                }
            }

            // Ollama models: function calling depends on model
            var hasFc = configuredModel.Contains("function") ||
                        configuredModel.StartsWith("mistral", StringComparison.OrdinalIgnoreCase) ||
                        configuredModel.StartsWith("mixtral", StringComparison.OrdinalIgnoreCase) ||
                        configuredModel.StartsWith("llama3", StringComparison.OrdinalIgnoreCase);

            return new ProviderCapabilities
            {
                Provider = name,
                Models = modelIds,
                Model = configuredModel,
                Vision = false, // Ollama vision models are separate (llava etc.)
                FunctionCalling = hasFc,
                Streaming = true,
                MaxContextTokens = -1 // Ollama doesn't expose this in /api/tags
            };
        }
        catch
        {
            return BuildDefault(name, configuredModel);
        }
    }

    private static ProviderCapabilities BuildDefault(string provider, string model)
    {
        return new ProviderCapabilities
        {
            Provider = provider,
            Model = model,
            Vision = false,
            FunctionCalling = true,
            Streaming = true,
            MaxContextTokens = -1
        };
    }
}
