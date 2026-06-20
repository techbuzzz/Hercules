using System.Net;
using System.Text;
using System.Text.Json;
using HerculesBus;
using HerculesBus.Http;
using HerculesBus.InMemory;
using Xunit;

namespace Hercules.Agent.Tests.Bus;

/// <summary>
///     Интеграционные тесты HTTP-сервера HerculesBus.
///     Поднимает реальный HttpListener на localhost (ephemeral port) и тестирует REST + SSE.
/// </summary>
public class HerculesBusHttpServerTests : IAsyncLifetime
{
    private readonly HerculesBusHttpServer _server;
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _token = "test-token-123";

    public HerculesBusHttpServerTests()
    {
        var port = GetFreePort();
        _baseUrl = $"http://localhost:{port}/";

        var bus = new global::HerculesBus.HerculesBus(new InMemoryChannelStore(), new InMemoryAgentRegistry(), new InMemoryEventBus());
        _server = new HerculesBusHttpServer(bus, _baseUrl);
        _server.AddToken(_token);

        _http = new HttpClient { BaseAddress = new Uri(_baseUrl) };
        _http.DefaultRequestHeaders.Add("X-Hercules-Token", _token);
    }

    public async Task InitializeAsync()
    {
        await _server.StartAsync();
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task Healthz_NoAuth_Returns_200()
    {
        using var anon = new HttpClient { BaseAddress = new Uri(_baseUrl) };
        var resp = await anon.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task No_Token_Returns_401()
    {
        using var anon = new HttpClient { BaseAddress = new Uri(_baseUrl) };
        var resp = await anon.GetAsync("/agents");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Register_Agent_Via_Post()
    {
        var dto = JsonSerializer.Serialize(new
        {
            agent_id = "alice",
            display_name = "Alice",
            roles = new[] { "hercules-agent" },
            token = "alice-tok"
        });

        var resp = await _http.PostAsync("/agents/register",
            new StringContent(dto, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Create_Channel_And_Send_Message_Via_HTTP()
    {
        await _http.PostAsync("/agents/register", new StringContent(JsonSerializer.Serialize(new
        {
            agent_id = "alice",
            display_name = "Alice",
            roles = new[] { "hercules-agent" },
            token = "tok"
        }), Encoding.UTF8, "application/json"));

        var channelResp = await _http.PostAsync("/channels", new StringContent(JsonSerializer.Serialize(new
        {
            name = "main",
            description = "General chat",
            is_private = false,
            created_by = "alice"
        }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, channelResp.StatusCode);

        var msgResp = await _http.PostAsync("/messages", new StringContent(JsonSerializer.Serialize(new
        {
            id = "",
            channel = "main",
            sender_agent_id = "alice",
            sender_name = "Alice",
            kind = "text",
            body = "hello from HTTP"
        }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, msgResp.StatusCode);

        var recentResp = await _http.GetAsync("/channels/main/recent?limit=10");
        Assert.Equal(HttpStatusCode.OK, recentResp.StatusCode);
        var body = await recentResp.Content.ReadAsStringAsync();
        Assert.Contains("hello from HTTP", body);
    }

    [Fact]
    public async Task SSE_Stream_Delivers_New_Messages()
    {
        await _http.PostAsync("/agents/register", new StringContent(JsonSerializer.Serialize(new
        {
            agent_id = "alice",
            display_name = "Alice",
            roles = new[] { "hercules-agent" },
            token = "tok"
        }), Encoding.UTF8, "application/json"));

        await _http.PostAsync("/channels", new StringContent(JsonSerializer.Serialize(new
        {
            name = "sse-test",
            description = "",
            is_private = false,
            created_by = "alice"
        }), Encoding.UTF8, "application/json"));

        var received = new List<string>();
        var sseClient = new HttpClient { BaseAddress = new Uri(_baseUrl), Timeout = TimeSpan.FromSeconds(5) };
        sseClient.DefaultRequestHeaders.Add("X-Hercules-Token", _token);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var sseTask = Task.Run(async () =>
        {
            using var stream = await sseClient.GetStreamAsync("/channels/sse-test/stream", cts.Token);
            using var reader = new StreamReader(stream);
            while (!cts.IsCancellationRequested && received.Count < 1)
            {
                var line = await reader.ReadLineAsync();
                if (line != null && line.StartsWith("data: "))
                    received.Add(line.Substring(6));
            }
        }, cts.Token);

        await Task.Delay(300); // дать SSE-подписчику зарегистрироваться

        await _http.PostAsync("/messages", new StringContent(JsonSerializer.Serialize(new
        {
            id = "",
            channel = "sse-test",
            sender_agent_id = "alice",
            sender_name = "Alice",
            kind = "text",
            body = "sse-message"
        }), Encoding.UTF8, "application/json"));

        await sseTask.WaitAsync(TimeSpan.FromSeconds(5));
        sseClient.Dispose();

        Assert.NotEmpty(received);
        Assert.Contains(received, s => s.Contains("sse-message"));
    }
}
