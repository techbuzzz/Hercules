using System.Net;
using System.Text.Json;
using Hercules.Config;
using Microsoft.Extensions.Logging;

namespace Hercules.LLM;

/// <summary>
///     Health check для LLM-провайдеров.
///     Проверяет каждый сконфигурированный провайдер через HTTP GET к его /v1/models или специфичному endpoint.
/// </summary>
public sealed class ProviderHealthChecker(LlmConfig cfg, ILogger<ProviderHealthChecker>? logger = null)
{
    private readonly ILogger<ProviderHealthChecker>? _logger = logger;

    /// <summary>Таймаут на один health check.</summary>
    public static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Проверить один провайдер по имени.</summary>
    public async Task<ProviderHealthResult> CheckAsync(string provider, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            return provider.ToLowerInvariant() switch
            {
                "yandexgpt" or "yandex" => await CheckOpenAIEndpointAsync("yandexgpt", cfg.YandexGpt.Endpoint, ct),
                "ollama-cloud" => await CheckOllamaAsync("ollama-cloud", cfg.OllamaCloud.Endpoint, ct),
                "ollama-local" or "ollama" => await CheckOllamaAsync("ollama-local", cfg.OllamaLocal.Endpoint, ct),
                "lmstudio" => await CheckLMStudioAsync(cfg.OpenAICompatible.Endpoint, ct),
                "openai-compatible" => await CheckOpenAIEndpointAsync("openai-compatible", cfg.OpenAICompatible.Endpoint, ct),
                _ => new ProviderHealthResult
                {
                    Provider = provider,
                    Healthy = false,
                    Status = "unknown",
                    Error = $"Unknown provider: {provider}"
                }
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger?.LogWarning(ex, "Health check failed for '{Provider}'", provider);
            return new ProviderHealthResult
            {
                Provider = provider,
                Healthy = false,
                Status = "error",
                Error = ex.Message,
                LatencyMs = (int)sw.ElapsedMilliseconds
            };
        }
    }

    /// <summary>Проверить все сконфигурированные провайдеры (primary + fallbacks).</summary>
    public async Task<IReadOnlyList<ProviderHealthResult>> CheckAllAsync(CancellationToken ct = default)
    {
        var providers = new[] { cfg.Provider }
            .Concat(cfg.Fallback)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var tasks = providers.Select(p => CheckAsync(p, ct));
        var results = await Task.WhenAll(tasks);
        return results;
    }

    private async Task<ProviderHealthResult> CheckOpenAIEndpointAsync(
        string name, string endpoint, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var http = new HttpClient { Timeout = HealthCheckTimeout };

        // YandexGPT и generic OpenAI: GET /v1/models
        var url = NormalizeEndpoint(endpoint, "/v1/models");
        try
        {
            using var response = await http.GetAsync(url, ct);
            sw.Stop();
            if (response.IsSuccessStatusCode)
            {
                return new ProviderHealthResult
                {
                    Provider = name,
                    Healthy = true,
                    Status = "ok",
                    LatencyMs = (int)sw.ElapsedMilliseconds
                };
            }

            return new ProviderHealthResult
            {
                Provider = name,
                Healthy = false,
                Status = $"http_{response.StatusCode}",
                Error = $"{response.StatusCode}",
                LatencyMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ProviderHealthResult
            {
                Provider = name,
                Healthy = false,
                Status = "unreachable",
                Error = ex.Message,
                LatencyMs = (int)sw.ElapsedMilliseconds
            };
        }
    }

    private async Task<ProviderHealthResult> CheckOllamaAsync(
        string name, string endpoint, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var http = new HttpClient { Timeout = HealthCheckTimeout };

        // Ollama: GET /api/tags
        var baseUrl = endpoint.TrimEnd('/').Replace("/v1", "");
        var url = $"{baseUrl}/api/tags";
        try
        {
            using var response = await http.GetAsync(url, ct);
            sw.Stop();
            if (response.IsSuccessStatusCode)
            {
                return new ProviderHealthResult
                {
                    Provider = name,
                    Healthy = true,
                    Status = "ok",
                    LatencyMs = (int)sw.ElapsedMilliseconds
                };
            }

            return new ProviderHealthResult
            {
                Provider = name,
                Healthy = false,
                Status = $"http_{response.StatusCode}",
                Error = $"{response.StatusCode}",
                LatencyMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ProviderHealthResult
            {
                Provider = name,
                Healthy = false,
                Status = "unreachable",
                Error = ex.Message,
                LatencyMs = (int)sw.ElapsedMilliseconds
            };
        }
    }

    private async Task<ProviderHealthResult> CheckLMStudioAsync(string endpoint, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var http = new HttpClient { Timeout = HealthCheckTimeout };

        // LM Studio: GET /api/info
        var baseUrl = endpoint.TrimEnd('/').Replace("/v1", "");
        var url = $"{baseUrl}/api/info";
        try
        {
            using var response = await http.GetAsync(url, ct);
            sw.Stop();
            if (response.IsSuccessStatusCode)
            {
                return new ProviderHealthResult
                {
                    Provider = "lmstudio",
                    Healthy = true,
                    Status = "ok",
                    LatencyMs = (int)sw.ElapsedMilliseconds
                };
            }

            return new ProviderHealthResult
            {
                Provider = "lmstudio",
                Healthy = false,
                Status = $"http_{response.StatusCode}",
                Error = $"{response.StatusCode}",
                LatencyMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ProviderHealthResult
            {
                Provider = "lmstudio",
                Healthy = false,
                Status = "unreachable",
                Error = ex.Message,
                LatencyMs = (int)sw.ElapsedMilliseconds
            };
        }
    }

    private static string NormalizeEndpoint(string endpoint, string path)
    {
        var baseUrl = endpoint.TrimEnd('/').Replace("/v1", "");
        return $"{baseUrl}{path}";
    }
}
