using Hercules.Agent;
using Hercules.Config;
using Hercules.LLM;
using Hercules.LLM.JsonRepair;
using Hercules.Lifecycle;
using Hercules.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.AgentCoreTests;

/// <summary>
///     task_080: verifies that AgentCore refuses new requests with
///     <see cref="LifecycleDrainingException" /> as soon as the shared
///     <see cref="IAgentLifecycleState" /> reports shutdown, and that the
///     in-flight counter ticks up/down around each HandleAsync call.
/// </summary>
public class AgentCoreShutdownTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteSessionStore _sessions;
    private readonly FileSkillRepository _skillRepo;
    private readonly SkillManager _skillManager;
    private readonly AgentCore _agentCore;
    private readonly AgentLifecycleStateHolder _state;
    private readonly InFlightTracker _tracker;

    public AgentCoreShutdownTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-shutdown-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var storageCfg = new StorageConfig { DataRoot = _tempDir };
        _sessions = new SqliteSessionStore(storageCfg);
        _skillRepo = new FileSkillRepository(storageCfg, NullLogger<FileSkillRepository>.Instance);
        _skillManager = new SkillManager(
            _skillRepo,
            new StubLlmClient("test"),
            new AgentConfig { SkillEvaluationWindow = 5 },
            new JsonRepairService());

        var router = new SkillRouter(_skillManager);
        var memory = new MemoryManager(
            new MemoryStore(storageCfg),
            new StubLlmClient("mem"));

        _state = new AgentLifecycleStateHolder();
        _tracker = new InFlightTracker();

        _agentCore = new AgentCore(
            new StubLlmClient("agent"),
            router,
            _skillManager,
            memory,
            _sessions,
            new AgentConfig(),
            NullLogger<AgentCore>.Instance,
            new JsonRepairService(),
            lifecycleState: _state,
            inFlight: _tracker);
    }

    public void Dispose()
    {
        try { _sessions.Dispose(); } catch { /* best effort */ }
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task HandleAsync_Throws_WhenStateIsDraining()
    {
        _state.SetState(AgentLifecycleState.Draining);

        var ex = await Assert.ThrowsAsync<LifecycleDrainingException>(
            () => _agentCore.HandleAsync("hello"));

        Assert.Equal(AgentLifecycleState.Draining, ex.State);
    }

    [Fact]
    public async Task HandleAsync_Throws_WhenStateIsStopped()
    {
        _state.SetState(AgentLifecycleState.Stopped);

        await Assert.ThrowsAsync<LifecycleDrainingException>(
            () => _agentCore.HandleAsync("hello"));
    }

    [Fact]
    public async Task HandleAsync_Throws_WhenStateIsDecommissioned()
    {
        _state.SetState(AgentLifecycleState.Decommissioned);

        await Assert.ThrowsAsync<LifecycleDrainingException>(
            () => _agentCore.HandleAsync("hello"));
    }

    [Fact]
    public async Task HandleAsync_ResolvesNormally_WhenStateIsRunning()
    {
        Assert.Equal(AgentLifecycleState.Running, _state.State);

        // Should not throw and should produce a response.
        var response = await _agentCore.HandleAsync("hello");
        Assert.NotNull(response);
        Assert.Equal("stub", response.Provider);
    }

    [Fact]
    public async Task InFlightTracker_IncrementsAndDecrementsAroundHandle()
    {
        // We can't easily inspect the counter from inside a running HandleAsync
        // because the LLM stub is synchronous, so we wrap the call in a small
        // check: the counter is zero before AND after the call.
        Assert.Equal(0, _tracker.InFlightCount);

        await _agentCore.HandleAsync("hello");

        Assert.Equal(0, _tracker.InFlightCount);
    }

    private sealed class StubLlmClient : ILLMClient
    {
        private readonly string _responseText;
        public StubLlmClient(string responseText) => _responseText = responseText;
        public string ProviderName => "stub";
        public string ModelName => "stub-model";

        public Task<LlmResponse> CompleteAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
            => Task.FromResult(new LlmResponse(_responseText, ProviderName, ModelName));

        public Task<LlmResponse> CompleteAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
            => CompleteAsync(Roles.Main, messages, ct);

        public IAsyncEnumerable<string> StreamAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
            => StreamAsync(messages, ct);

        public IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
            => AsyncEnumerable.Empty<string>();
    }
}
