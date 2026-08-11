using Hercules.Agent;
using Hercules.Config;
using Hercules.LLM;
using Hercules.Mesh;
using Hercules.Storage;
using Xunit;

namespace Hercules.Agent.Tests.Phase4Tests;

public class MeshRouterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CapabilityRegistry _registry;
    private readonly AgentManifestService _manifestService;
    private readonly IntentTransport _transport;
    private readonly AgentCore _agent;
    private readonly CircuitBreaker _breaker;
    private readonly RetryPolicy _retry;
    private readonly MeshRouter _router;
    private readonly FileSkillRepository _repo;
    private readonly SkillManager _skillManager;
    private readonly SqliteSessionStore _sessions;
    private readonly MemoryManager _memory;

    public MeshRouterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-mr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var storageCfg = new StorageConfig { DataRoot = _tempDir };
        _repo = new FileSkillRepository(storageCfg);
        _sessions = new SqliteSessionStore(storageCfg);
        _skillManager = new SkillManager(_repo, new StubLlmMesh("test", "medium"), new AgentConfig());
        var router = new SkillRouter(_skillManager);
        _memory = new MemoryManager(new MemoryStore(storageCfg), new StubLlmMesh("mem"));
        _agent = new AgentCore(new StubLlmMesh("Ответ", "high"), router, _skillManager, _memory, _sessions, new AgentConfig());

        var dbPath = Path.Combine(_tempDir, "registry.db");
        _registry = new CapabilityRegistry(dbPath);

        _manifestService = new AgentManifestService(
            agentId: "hercules-main",
            displayName: "Main",
            description: "Main agent",
            endpoint: "http://localhost:5000",
            manifestDir: _tempDir,
            capabilitiesProvider: () => new List<ManifestCapability>());

        _transport = new IntentTransport(_registry);
        _breaker = new CircuitBreaker { FailureThreshold = 10, Cooldown = TimeSpan.FromSeconds(1) };
        _retry = new RetryPolicy { MaxAttempts = 1 };
        _router = new MeshRouter(_agent, _registry, _transport, _manifestService,
            new StubLlmMesh("judge", "high"), _breaker, _retry);
    }

    public void Dispose()
    {
        try { _transport.Dispose(); _registry.Dispose(); _sessions.Dispose(); } catch { }
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public async Task RouteWithFanOut_NoPeers_ProcessesLocally()
    {
        var envelope = new IntentEnvelope(
            RequestId: IntentIds.NewRequestId(),
            Sender: "hercules-main",
            Intent: "anything",
            Payload: "привет");

        var result = await _router.RouteWithFanOutAsync(envelope);

        Assert.True(result.HasWinner);
        Assert.Equal("local", result.SelectionMethod);
        Assert.Single(result.AllResponses);
    }

    [Fact]
    public async Task RouteWithFanOut_SinglePeer_SendsToPeer()
    {
        // Регистрируем одного peer'а (недоступного — нет endpoint)
        _registry.Register(new AgentManifest
        {
            AgentId = "peer-1",
            DisplayName = "Peer 1",
            Endpoint = "http://nonexistent:9999",
            Capabilities = new List<ManifestCapability>
            {
                new() { Name = "review", Description = "Code review", PhraseReceivers = ["review"] }
            }
        });

        var envelope = new IntentEnvelope(
            RequestId: IntentIds.NewRequestId(),
            Sender: "hercules-main",
            Intent: "review",
            Payload: "review this code",
            TimeoutMs: 1000);

        var result = await _router.RouteWithFanOutAsync(envelope);

        // Single peer (< MinPeersForFanOut = 2) — отправляется одному
        Assert.Equal("single-peer", result.SelectionMethod);
        // Peer недоступен — winner null
        Assert.False(result.HasWinner);
    }

    [Fact]
    public async Task RouteWithFanOut_MultiplePeers_DoesFanOut()
    {
        // Регистрируем 2 peer'ов с одинаковой capability
        for (int i = 1; i <= 2; i++)
        {
            _registry.Register(new AgentManifest
            {
                AgentId = $"peer-{i}",
                DisplayName = $"Peer {i}",
                Endpoint = $"http://nonexistent-{i}:9999",
                Capabilities = new List<ManifestCapability>
                {
                    new() { Name = "translate", Description = "Translation", PhraseReceivers = ["translate"] }
                }
            });
        }

        _router.MinPeersForFanOut = 2;
        _router.FanOutTimeoutMs = 2000;

        var envelope = new IntentEnvelope(
            RequestId: IntentIds.NewRequestId(),
            Sender: "hercules-main",
            Intent: "translate",
            Payload: "translate hello",
            TimeoutMs: 1000);

        var result = await _router.RouteWithFanOutAsync(envelope);

        // Fan-out произошёл (2 peer'а >= MinPeersForFanOut)
        Assert.NotEqual("local", result.SelectionMethod);
        Assert.Equal(2, result.AllResponses.Count);
        // Оба peer'а недоступны — нет winner
        Assert.False(result.HasWinner);
    }

    [Fact]
    public async Task RouteWithFanOut_CircuitBreaker_Filters_Unavailable_Peers()
    {
        // Регистрируем 3 peer'ов
        for (int i = 1; i <= 3; i++)
        {
            _registry.Register(new AgentManifest
            {
                AgentId = $"cb-peer-{i}",
                DisplayName = $"CB Peer {i}",
                Endpoint = $"http://nonexistent-{i}:9999",
                Capabilities = new List<ManifestCapability>
                {
                    new() { Name = "test-cap", Description = "Test", PhraseReceivers = ["test"] }
                }
            });
        }

        // Размыкаем цепь для cb-peer-1
        _breaker.FailureThreshold = 1;
        _breaker.RecordFailure("cb-peer-1");
        Assert.Equal(CircuitState.Open, _breaker.GetState("cb-peer-1"));

        _router.MinPeersForFanOut = 2;
        _router.FanOutTimeoutMs = 2000;

        var envelope = new IntentEnvelope(
            RequestId: IntentIds.NewRequestId(),
            Sender: "hercules-main",
            Intent: "test-cap",
            Payload: "test",
            TimeoutMs: 1000);

        var result = await _router.RouteWithFanOutAsync(envelope);

        // cb-peer-1 отфильтрован circuit breaker'ом — только 2 peer'а участвуют
        Assert.Equal(2, result.AllResponses.Count);
        Assert.DoesNotContain(result.AllResponses, r => r.Agent == "cb-peer-1");
    }

    [Fact]
    public void FanOutStrategy_Default_Is_HighestConfidence()
    {
        Assert.Equal(FanOutStrategy.HighestConfidence, _router.Strategy);
    }
}

/// <summary>Stub LLM для MeshRouter-тестов.</summary>
internal sealed class StubLlmMesh : ILLMClient
{
    private readonly string _response;
    private readonly string _confidence;

    public StubLlmMesh(string response, string confidence = "medium")
    {
        _response = response;
        _confidence = confidence;
    }

    public string ProviderName => "stub";
    public string ModelName => "stub-model";

    public Task<LlmResponse> CompleteAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
        => Task.FromResult(new LlmResponse($"{_response} [confidence: {_confidence}]", ProviderName, ModelName));

    public Task<LlmResponse> CompleteAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
        => CompleteAsync(Roles.Main, messages, ct);

    public IAsyncEnumerable<string> StreamAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
        => StreamAsync(messages, ct);

    public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatTurn> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        yield return _response;
    }
}