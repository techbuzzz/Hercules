using System.Net;
using System.Net.Http;
using Hercules.Config;
using Hercules.Degradation;
using Hercules.LLM;
using Hercules.LLM.Providers;
using Hercules.Mesh;
using Hercules.Mesh.Transport;
using Hercules.Offline;
using Hercules.Skills;
using Hercules.Skills.Marketplace;
using Hercules.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;

namespace Hercules.Agent.Tests.Http;

/// <summary>
///     task_078: integration tests for <see cref="IHttpClientFactory"/> adoption
///     across <c>HttpTool</c>, <c>A2AClient</c>, <c>NetworkMonitor</c>,
///     <c>OperatorNotificationService</c>, <c>ProviderHealthChecker</c>,
///     <c>ProviderCapabilityDetector</c>, <c>LMStudioClient</c>,
///     <c>SkillMarketplace</c>. Verifies that DI-injected factory is used
///     (named clients) and that the resilience handler retries on transient
///     failures.
/// </summary>
public class HttpClientFactoryTests
{
    private static HttpMessageHandler CreateMockHandler(Func<HttpRequestMessage, HttpResponseMessage> handlerFn)
    {
        var mock = new Mock<HttpMessageHandler>();
        mock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) => handlerFn(req));
        return mock.Object;
    }

    /// <summary>
    ///     <see cref="IHttpClientFactory"/> stand-in, создающий клиенты с заданным
    ///     <see cref="HttpMessageHandler"/>. Поддерживает named clients, чтобы
    ///     можно было проверять, что используется именно ожидаемый HttpClientName.
    /// </summary>
    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly Dictionary<string, HttpMessageHandler> _handlers;

        public TestHttpClientFactory(Dictionary<string, HttpMessageHandler> handlers)
        {
            _handlers = handlers;
        }

        public List<string> RequestedNames { get; } = new();

        public HttpClient CreateClient(string name)
        {
            RequestedNames.Add(name);
            if (!_handlers.TryGetValue(name, out var handler))
            {
                throw new InvalidOperationException($"No mock handler registered for client '{name}'.");
            }
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        }
    }

    // ---------- HttpTool: uses named client "http-tool" via factory ----------

    [Fact]
    public async Task HttpTool_WithoutFactory_UsesLegacyClient()
    {
        // Legacy ctor (no factory) — инструмент должен работать без DI
        // и не должен бросать NRE.
        var cfg = new HttpConfig { AllowedDomains = new List<string> { "example.com" } };
        var tool = new HttpTool(cfg, NullLogger<HttpTool>.Instance);
        // Не выполняем ExecuteAsync — просто проверяем, что конструктор не упал.
        Assert.Equal("http", tool.Name);
    }

    [Fact]
    public async Task HttpTool_WithFactory_UsesNamedClient()
    {
        var handler = CreateMockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("hello")
        });
        var factory = new TestHttpClientFactory(new Dictionary<string, HttpMessageHandler>
        {
            [HttpTool.HttpClientName] = handler
        });

        var cfg = new HttpConfig
        {
            AllowedDomains = new List<string> { "example.com" },
            TimeoutSeconds = 5
        };
        var tool = new HttpTool(cfg, NullLogger<HttpTool>.Instance, factory);

        var args = """{"method":"GET","url":"https://example.com/"}""";
        var result = await tool.ExecuteAsync(args);

        Assert.True(result.Success);
        Assert.Contains("hello", result.Output);
        Assert.Contains(HttpTool.HttpClientName, factory.RequestedNames);
    }

    [Fact]
    public async Task HttpTool_RespectsAllowList_EvenWithFactory()
    {
        var handler = CreateMockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var factory = new TestHttpClientFactory(new Dictionary<string, HttpMessageHandler>
        {
            [HttpTool.HttpClientName] = handler
        });

        var cfg = new HttpConfig
        {
            AllowedDomains = new List<string> { "allowed.example" }
        };
        var tool = new HttpTool(cfg, NullLogger<HttpTool>.Instance, factory);

        var args = """{"method":"GET","url":"https://blocked.example/"}""";
        var result = await tool.ExecuteAsync(args);

        Assert.False(result.Success);
        Assert.Empty(factory.RequestedNames); // not even CreateClient should be called
    }

    // ---------- A2AClient: named client "a2a-client" ----------

    [Fact]
    public void A2AClient_WithoutFactory_Constructs()
    {
        var client = new A2AClient(new A2AConfig());
        Assert.Equal("a2a", client.Name);
    }

    [Fact]
    public async Task A2AClient_WithFactory_UsesNamedClient_UnknownAgentFailsBeforeCall()
    {
        var handler = CreateMockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var factory = new TestHttpClientFactory(new Dictionary<string, HttpMessageHandler>
        {
            [A2AClient.HttpClientName] = handler
        });

        var cfg = new A2AConfig();
        var client = new A2AClient(cfg, factory);

        var args = """{"agent":"unknown","task":"hello"}""";
        var result = await client.ExecuteAsync(args);

        Assert.False(result.Success);
        Assert.Empty(factory.RequestedNames); // unknown agent → no HTTP call
    }

    // ---------- NetworkMonitor: named client "network-monitor" ----------

    [Fact]
    public void NetworkMonitor_WithoutFactory_Constructs()
    {
        var mon = new NetworkMonitor(new OfflineSyncConfig { NetworkPollTimeoutSeconds = 5 }, NullLogger<NetworkMonitor>.Instance);
        Assert.False(mon.IsOnline); // starts offline
    }

    [Fact]
    public async Task NetworkMonitor_WithFactory_UsesNamedClient_AndReports()
    {
        var handler = CreateMockHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var factory = new TestHttpClientFactory(new Dictionary<string, HttpMessageHandler>
        {
            [NetworkMonitor.HttpClientName] = handler
        });

        var mon = new NetworkMonitor(
            new OfflineSyncConfig { NetworkPollTimeoutSeconds = 5, NetworkPollUrl = "https://example.com/" },
            NullLogger<NetworkMonitor>.Instance,
            factory);

        var reached = await mon.CheckOnceAsync();

        Assert.True(reached);
        Assert.Contains(NetworkMonitor.HttpClientName, factory.RequestedNames);
    }

    // ---------- OperatorNotificationService: named client "operator-notify" ----------

    [Fact]
    public void OperatorNotificationService_WithoutFactory_Constructs()
    {
        using var svc = new OperatorNotificationService(
            new DegradationConfig { Notifications = new NotificationConfig { Enabled = false } },
            NullLogger<OperatorNotificationService>.Instance);
        // Should not throw.
    }

    [Fact]
    public async Task OperatorNotificationService_WithFactory_UsesNamedClient_DisabledDoesNotCall()
    {
        var handler = CreateMockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var factory = new TestHttpClientFactory(new Dictionary<string, HttpMessageHandler>
        {
            [OperatorNotificationService.HttpClientName] = handler
        });

        var svc = new OperatorNotificationService(
            new DegradationConfig { Notifications = new NotificationConfig { Enabled = false } },
            NullLogger<OperatorNotificationService>.Instance,
            factory);

        await svc.NotifyModeChangeAsync(DegradationMode.Full, DegradationMode.Degraded);
        Assert.Empty(factory.RequestedNames); // disabled
    }

    // ---------- ProviderHealthChecker: named client "llm-health" ----------

    [Fact]
    public void ProviderHealthChecker_WithoutFactory_Constructs()
    {
        var cfg = new LlmConfig
        {
            Provider = "yandexgpt",
            YandexGpt = new YandexGptConfig { Endpoint = "https://example.invalid/v1" }
        };
        var checker = new ProviderHealthChecker(cfg, NullLogger<ProviderHealthChecker>.Instance);
        Assert.Equal("yandexgpt", cfg.Provider);
    }

    [Fact]
    public async Task ProviderHealthChecker_WithFactory_UsesNamedClient()
    {
        var handler = CreateMockHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var factory = new TestHttpClientFactory(new Dictionary<string, HttpMessageHandler>
        {
            [ProviderHealthChecker.HttpClientName] = handler
        });

        var cfg = new LlmConfig
        {
            Provider = "yandexgpt",
            YandexGpt = new YandexGptConfig { Endpoint = "https://example.invalid/v1" }
        };
        var checker = new ProviderHealthChecker(cfg, NullLogger<ProviderHealthChecker>.Instance, factory);

        var result = await checker.CheckAsync("yandexgpt");

        Assert.False(result.Healthy);
        Assert.Contains(ProviderHealthChecker.HttpClientName, factory.RequestedNames);
    }

    // ---------- HttpClientName constants sanity ----------

    [Fact]
    public void NamedClientNames_AreUnique()
    {
        // Защита от случайной дубликации имён named-клиентов: каждый named-клиент
        // должен иметь уникальное имя, иначе factory.CreateClient() вернёт один
        // и тот же pipeline для разных компонентов.
        var names = new[]
        {
            HttpTool.HttpClientName,
            A2AClient.HttpClientName,
            NetworkMonitor.HttpClientName,
            OperatorNotificationService.HttpClientName,
            ProviderHealthChecker.HttpClientName,
            ProviderCapabilityDetector.HttpClientName,
            LMStudioClient.HttpClientName,
            IntentTransport.HttpClientName,
            HttpTransportAdapter.HttpClientName,
            GrpcTransportAdapter.HttpClientName,
            SkillMarketplace.HttpClientName
        };

        Assert.Equal(names.Length, names.Distinct().Count());
    }

    // ---------- Resilience handler retry behavior ----------

    [Fact]
    public async Task HttpClient_WithStandardResilience_RetriesOnTransient503()
    {
        // Проверяем, что AddStandardResilienceHandler действительно срабатывает:
        // первая попытка → 503, вторая попытка → 200. Без resilience handler
        // счётчик был бы равен 1.
        var attempts = 0;
        var handler = CreateMockHandler(_ =>
        {
            attempts++;
            return attempts < 2
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("recovered")
                };
        });

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        // ConfigurePrimaryHttpMessageHandler must be called on the IHttpClientBuilder,
        // before AddStandardResilienceHandler (which returns a different builder type).
        services.AddHttpClient("retry-test", c => c.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddStandardResilienceHandler(o =>
            {
                o.Retry.MaxRetryAttempts = 3;
                o.Retry.Delay = TimeSpan.FromMilliseconds(50);
                o.Retry.UseJitter = false;
            });

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("retry-test");
        var resp = await client.GetAsync("https://example.invalid/");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(attempts >= 2, $"Resilience handler should have retried; attempts={attempts}");
        Assert.True(attempts <= 4, $"Resilience handler should respect MaxRetryAttempts; attempts={attempts}");
    }
}
