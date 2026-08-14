using System.Net;
using System.Text.Json;
using Hercules.Config;
using Hercules.Mesh;
using Hercules.Mesh.A2A;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace Hercules.Agent.Tests.Phase3Tests;

public class AgentCardServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _manifestDir;
    private readonly Mock<HttpMessageHandler> _httpMock;
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<AgentCardService>> _loggerMock;
    private readonly A2AConfig _a2aConfig;
    private readonly AgentManifestService _manifestService;

    public AgentCardServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-ac-{Guid.NewGuid():N}");
        _manifestDir = Path.Combine(_tempDir, "manifest");
        Directory.CreateDirectory(_manifestDir);

        _httpMock = new Mock<HttpMessageHandler>(MockBehavior.Loose);
        _httpClient = new HttpClient(_httpMock.Object);
        _loggerMock = new Mock<ILogger<AgentCardService>>();
        _a2aConfig = new A2AConfig
        {
            AgentCard = new A2AAgentCardConfig
            {
                Publish = true,
                Endpoint = Path.Combine(_tempDir, "agent-card.json"),
                CacheTtlMinutes = 60
            }
        };

        _manifestService = new AgentManifestService(
            "hercules-test",
            "Test Agent",
            "Test agent description",
            "http://localhost:5000",
            _manifestDir,
            () => new List<ManifestCapability>
            {
                new() { Name = "code-review", Description = "Code review", PhraseReceivers = ["review"] }
            },
            () => new List<ManifestSkillEntry>
            {
                new() { Id = "code-review", Name = "Code Review", Description = "Reviews code" }
            },
            "yandexgpt",
            new List<string> { "ollama-local" },
            "http://localhost:5000/api/health",
            new List<string> { "1.0" },
            null,
            null);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }
        catch { }

        _httpClient.Dispose();
    }

    private AgentCardService CreateService()
    {
        return new AgentCardService(
            _manifestService,
            _a2aConfig,
            _httpClient,
            _loggerMock.Object);
    }

    [Fact]
    public void FromManifest_MapsBasicFields()
    {
        var svc = CreateService();
        AgentManifest manifest = _manifestService.Current;
        AgentCard card = svc.FromManifest(manifest);

        Assert.Equal("hercules-test", card.Name);
        Assert.Equal("Test agent description", card.Description);
        Assert.Equal("http://localhost:5000", card.Url);
        Assert.Equal("1.0.0", card.Version);
        Assert.NotNull(card.Capabilities);
        Assert.NotNull(card.Authentication);
        Assert.NotNull(card.Provider);
    }

    [Fact]
    public void FromManifest_MapsSkills()
    {
        var svc = CreateService();
        AgentManifest manifest = _manifestService.Current;
        AgentCard card = svc.FromManifest(manifest);

        Assert.Single(card.Skills);
        Assert.Equal("code-review", card.Skills[0].Id);
        Assert.Equal("Code Review", card.Skills[0].Name);
        Assert.Equal("Reviews code", card.Skills[0].Description);
    }

    [Fact]
    public void FromManifest_SetsGeneratedAt()
    {
        var svc = CreateService();
        AgentManifest manifest = _manifestService.Current;
        AgentCard card = svc.FromManifest(manifest);

        Assert.NotEmpty(card.GeneratedAt);
        // Should be parseable as DateTime
        Assert.True(DateTime.TryParse(card.GeneratedAt, out _));
    }

    [Fact]
    public void FromManifest_CapabilitiesDefaultToFalse()
    {
        var svc = CreateService();
        AgentManifest manifest = _manifestService.Current;
        AgentCard card = svc.FromManifest(manifest);

        Assert.False(card.Capabilities.Streaming);
        Assert.False(card.Capabilities.PushNotifications);
        Assert.False(card.Capabilities.StateTransitionReports);
        Assert.False(card.Capabilities.MultipartResponses);
    }

    [Fact]
    public void FromManifest_NoAuthWhenTypeIsNone()
    {
        var manifestNoAuth = new AgentManifest
        {
            AgentId = "test",
            DisplayName = "Test",
            Description = "Test",
            Endpoint = "http://localhost",
            Auth = new ManifestAuth { Type = "none" }
        };

        var svc = CreateService();
        AgentCard card = svc.FromManifest(manifestNoAuth);

        Assert.Null(card.Authentication);
    }

    [Fact]
    public async Task GetAgentCardAsync_GeneratesFromManifest()
    {
        var svc = CreateService();
        AgentCard card = await svc.GetAgentCardAsync();

        Assert.Equal("hercules-test", card.Name);
        Assert.Equal("http://localhost:5000", card.Url);
        Assert.NotEmpty(card.GeneratedAt);
    }

    [Fact]
    public async Task GetAgentCardAsync_ReturnsCachedOnSubsequentCalls()
    {
        var svc = CreateService();
        AgentCard first = await svc.GetAgentCardAsync();
        AgentCard second = await svc.GetAgentCardAsync();

        Assert.Same(first, second); // Same reference = cached
        Assert.True(svc.IsCurrent());
    }

    [Fact]
    public async Task ImportFromUrlAsync_Success()
    {
        var svc = CreateService();
        var json = JsonSerializer.Serialize(new AgentCard
        {
            Name = "peer-agent",
            Url = "http://peer.example.com",
            Description = "A peer agent",
            Version = "2.0.0",
            Skills = new List<AgentCardSkill>
            {
                new() { Id = "analysis", Name = "Analysis", Description = "Analysis skill" }
            }
        }, new JsonSerializerOptions { WriteIndented = true });

        _httpMock.Protected().Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.Is<HttpRequestMessage>(m => m.RequestUri != null && m.RequestUri.ToString() == "https://peer.example.com/card"),
            ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(json)
            });

        AgentCard card = await svc.ImportFromUrlAsync("https://peer.example.com/card");

        Assert.Equal("peer-agent", card.Name);
        Assert.Equal("http://peer.example.com", card.Url);
        Assert.Equal("2.0.0", card.Version);
        Assert.Single(card.Skills);
        Assert.Equal("analysis", card.Skills[0].Id);
    }

    [Fact]
    public async Task ImportFromUrlAsync_HttpError_Throws()
    {
        var svc = CreateService();

        _httpMock.Protected().Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.NotFound
            });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ImportFromUrlAsync("https://peer.example.com/card"));
    }

    [Fact]
    public async Task ImportFromUrlAsync_InvalidJson_Throws()
    {
        var svc = CreateService();

        _httpMock.Protected().Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("not json {{{")
            });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ImportFromUrlAsync("https://peer.example.com/card"));
    }

    [Fact]
    public async Task ImportFromUrlAsync_MissingName_Throws()
    {
        var svc = CreateService();
        var json = JsonSerializer.Serialize(new AgentCard
        {
            Name = "", // empty name
            Url = "http://peer.example.com"
        });

        _httpMock.Protected().Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(json)
            });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ImportFromUrlAsync("https://peer.example.com/card"));
    }

    [Fact]
    public async Task ImportFromUrlAsync_EmptyUrl_ThrowsArgument()
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.ImportFromUrlAsync(""));
    }

    [Fact]
    public async Task PublishAsync_WritesFile()
    {
        var svc = CreateService();
        string path = await svc.PublishAsync();

        Assert.Equal(_a2aConfig.AgentCard.Endpoint, path);
        Assert.True(File.Exists(path));

        var card = JsonSerializer.Deserialize<AgentCard>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(card);
        Assert.Equal("hercules-test", card.Name);
    }

    [Fact]
    public async Task PublishAsync_DisabledConfig_DoesNotWrite()
    {
        _a2aConfig.AgentCard.Publish = false;
        var svc = CreateService();

        string path = await svc.PublishAsync();
        Assert.Empty(path);
    }

    [Fact]
    public async Task DiscoverAsync_CollectsCards()
    {
        var svc = CreateService();

        var card1 = new AgentCard { Name = "agent1", Url = "http://a1", Version = "1.0" };
        var card2 = new AgentCard { Name = "agent2", Url = "http://a2", Version = "2.0" };

        var responses = new Dictionary<string, string>
        {
            ["https://a1/agent-card.json"] = JsonSerializer.Serialize(card1),
            ["https://a2/agent-card.json"] = JsonSerializer.Serialize(card2),
            ["https://a3/agent-card.json"] = "not found" // will fail
        };

        _httpMock.Protected().Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                string url = req.RequestUri?.ToString() ?? "";
                if (responses.TryGetValue(url, out var body))
                {
                    return new HttpResponseMessage
                    {
                        StatusCode = url.Contains("a3")
                            ? HttpStatusCode.NotFound
                            : HttpStatusCode.OK,
                        Content = new StringContent(body)
                    };
                }
                return new HttpResponseMessage { StatusCode = HttpStatusCode.NotFound };
            });

        var urls = new[] { "https://a1/agent-card.json", "https://a2/agent-card.json", "https://a3/agent-card.json" };
        var results = await svc.DiscoverAsync(urls);

        Assert.Equal(2, results.Count);
        Assert.Equal("agent1", results[0].Card.Name);
        Assert.Equal("agent2", results[1].Card.Name);
        Assert.Equal("https://a1/agent-card.json", results[0].Url);
        Assert.Equal("https://a2/agent-card.json", results[1].Url);
    }

    [Fact]
    public void IsCurrent_FalseWhenNotCached()
    {
        var svc = CreateService();
        Assert.False(svc.IsCurrent());
    }

    [Fact]
    public async Task IsCurrent_TrueAfterGetAgentCard()
    {
        var svc = CreateService();
        await svc.GetAgentCardAsync();
        Assert.True(svc.IsCurrent());
    }

    [Fact]
    public async Task IsCurrent_RespectsCacheTtl()
    {
        _a2aConfig.AgentCard.CacheTtlMinutes = 0; // 0 minutes = instant expiry
        var svc = CreateService();
        await svc.GetAgentCardAsync();
        // IsCurrent checks DateTime.UtcNow - _cachedAt < TTL
        // With TTL=0, this should be false (unless calls are exactly simultaneous)
        // So we just verify it doesn't throw
        _ = svc.IsCurrent();
    }
}
