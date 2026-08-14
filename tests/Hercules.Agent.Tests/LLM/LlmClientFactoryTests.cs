using Hercules.Cache;
using Hercules.Config;
using Hercules.LLM;
using Hercules.LLM.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.LLM;

/// <summary>
/// Тесты LlmClientFactory: создание клиентов по имени провайдера.
/// </summary>
public class LlmClientFactoryTests
{
    private static LlmConfig BuildConfig()
    {
        return new LlmConfig
        {
            Provider = "yandexgpt",
            YandexGpt = new YandexGptConfig
            {
                Endpoint = "https://llm.api.cloud.yandex.net/v1",
                ApiKey = "test-key",
                FolderId = "test-folder",
                Model = "yandexgpt-lite"
            },
            OllamaCloud = new OllamaConfig
            {
                Endpoint = "https://ollama.cloud/api",
                Model = "llama3.1"
            },
            OllamaLocal = new OllamaConfig
            {
                Endpoint = "http://localhost:11434/v1",
                Model = "mistral"
            },
            OpenAICompatible = new OpenAICompatibleConfig
            {
                Endpoint = "http://localhost:1234/v1",
                Model = "llama3.2",
                DisplayName = "LM Studio"
            }
        };
    }

    private static LlmClientFactory CreateFactory(LlmConfig? cfg = null)
        => new(cfg ?? BuildConfig());

    // ---- Provider creation ----

    [Theory]
    [InlineData("yandexgpt", typeof(YandexGPTClient))]
    [InlineData("yandex", typeof(YandexGPTClient))]
    [InlineData("ollama-cloud", typeof(LocalLLMClient))]
    [InlineData("ollama-local", typeof(LocalLLMClient))]
    [InlineData("ollama", typeof(LocalLLMClient))]
    [InlineData("lmstudio", typeof(LMStudioClient))]
    [InlineData("openai-compatible", typeof(OpenAICompatibleClient))]
    public void Create_returns_correct_type(string provider, Type expectedType)
    {
        var factory = CreateFactory();
        var client = factory.Create(provider);
        Assert.IsType(expectedType, client);
    }

    [Fact]
    public void Create_yandexgpt_sets_correct_provider_name()
    {
        var factory = CreateFactory();
        var client = factory.Create("yandexgpt");
        Assert.Equal("yandexgpt", client.ProviderName);
    }

    [Fact]
    public void Create_ollama_cloud_sets_correct_provider_name()
    {
        var factory = CreateFactory();
        var client = factory.Create("ollama-cloud");
        Assert.Equal("ollama-cloud", client.ProviderName);
    }

    [Fact]
    public void Create_lmstudio_sets_correct_provider_name()
    {
        var factory = CreateFactory();
        var client = factory.Create("lmstudio");
        Assert.Equal("lmstudio", client.ProviderName);
    }

    [Fact]
    public void Create_openai_compatible_sets_correct_provider_name()
    {
        var factory = CreateFactory();
        var client = factory.Create("openai-compatible");
        Assert.Equal("openai-compatible", client.ProviderName);
    }

    [Fact]
    public void Create_unknown_throws_ArgumentException()
    {
        var factory = CreateFactory();
        var ex = Assert.Throws<ArgumentException>(() => factory.Create("unknown-provider"));
        Assert.Contains("unknown-provider", ex.Message);
    }

    [Theory]
    [InlineData("YANDEXGPT")]
    [InlineData("OpenAI-Compatible")]
    [InlineData("YandexGPT")]
    public void Create_is_case_insensitive(string provider)
    {
        var factory = CreateFactory();
        var client = factory.Create(provider); // Should not throw
        Assert.NotNull(client);
    }

    [Fact]
    public void Create_withNullCache_UsesNullCacheService()
    {
        // NullCacheService is used when no ICacheService provided
        var factory = new LlmClientFactory(BuildConfig(), null);
        var client = factory.Create("yandexgpt");
        Assert.NotNull(client);
        Assert.Equal("yandexgpt", client.ProviderName);
    }

    [Fact]
    public void Create_Uncached_returnsCorrectType_EvenWithoutCache()
    {
        // CreateUncached bypasses cache — useful for testing
        var factory = CreateFactory();
        var client = factory.CreateUncached("ollama-cloud");
        Assert.IsType<LocalLLMClient>(client);
        Assert.Equal("ollama-cloud", client.ProviderName);
    }

    [Fact]
    public void CreateUncached_Unknown_throws()
    {
        var factory = CreateFactory();
        var ex = Assert.Throws<ArgumentException>(() => factory.CreateUncached("nonexistent"));
        Assert.Contains("nonexistent", ex.Message);
    }
}
