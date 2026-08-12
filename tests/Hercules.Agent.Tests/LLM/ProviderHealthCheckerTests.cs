using Hercules.Config;
using Hercules.LLM;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.LLM;

/// <summary>
/// Тесты ProviderHealthChecker: health check для всех провайдеров.
/// </summary>
public class ProviderHealthCheckerTests
{
    private static LlmConfig BuildConfig()
    {
        return new LlmConfig
        {
            Provider = "yandexgpt",
            YandexGpt = new YandexGptConfig
            {
                Endpoint = "https://llm.api.cloud.yandex.net/v1",
                ApiKey = "test-key"
            },
            OllamaCloud = new OllamaConfig
            {
                Endpoint = "http://localhost:11434/v1"
            },
            OllamaLocal = new OllamaConfig
            {
                Endpoint = "http://localhost:11435/v1"
            },
            OpenAICompatible = new OpenAICompatibleConfig
            {
                Endpoint = "http://localhost:1234/v1"
            }
        };
    }

    private static ProviderHealthChecker CreateChecker(LlmConfig? cfg = null)
        => new(cfg ?? BuildConfig(), Mock.Of<ILogger<ProviderHealthChecker>>());

    // ---- Health check by provider ----

    [Theory]
    [InlineData("yandexgpt")]
    [InlineData("ollama-cloud")]
    [InlineData("ollama-local")]
    [InlineData("lmstudio")]
    [InlineData("openai-compatible")]
    public async Task CheckAsync_returns_result_for_all_known_providers(string provider)
    {
        var checker = CreateChecker();
        var result = await checker.CheckAsync(provider);
        Assert.Equal(provider.ToLowerInvariant(), result.Provider.ToLowerInvariant());
        Assert.NotNull(result.Status);
        Assert.True(result.CheckedAt <= DateTime.UtcNow && result.CheckedAt > DateTime.UtcNow.AddMinutes(-1));
        Assert.True(result.LatencyMs >= 0);
    }

    [Fact]
    public async Task CheckAsync_unknown_provider_returns_unhealthy()
    {
        var checker = CreateChecker();
        var result = await checker.CheckAsync("non-existent");
        Assert.Equal("non-existent", result.Provider);
        Assert.False(result.Healthy);
        Assert.Equal("unknown", result.Status);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task CheckAllAsync_returns_all_configured_providers()
    {
        var cfg = BuildConfig();
        cfg.Fallback = ["ollama-cloud", "openai-compatible"];
        var checker = CreateChecker(cfg);

        var results = await checker.CheckAllAsync();

        Assert.Contains(results, r => r.Provider.Equals("yandexgpt", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(results, r => r.Provider.Equals("ollama-cloud", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(results, r => r.Provider.Equals("openai-compatible", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CheckAllAsync_deduplicates_providers()
    {
        var cfg = BuildConfig();
        cfg.Fallback = ["yandexgpt", "yandexgpt"]; // duplicate
        var checker = CreateChecker(cfg);

        var results = await checker.CheckAllAsync();

        var yandexCount = results.Count(r => r.Provider.Equals("yandexgpt", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, yandexCount);
    }

    [Fact]
    public async Task CheckAsync_includes_latency()
    {
        var checker = CreateChecker();
        var result = await checker.CheckAsync("lmstudio");
        Assert.True(result.LatencyMs >= 0);
    }
}
