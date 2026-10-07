using Hercules.Agent;
using Hercules.Config;
using Hercules.LLM;
using Hercules.LLM.JsonRepair;
using Hercules.Lifecycle;
using Hercules.Mesh;
using Hercules.Mesh.Transport;
using Hercules.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Lifecycle;

public class LifecycleServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteSessionStore _sessions;
    private readonly FileSkillRepository _skillRepo;
    private readonly SkillManager _skillManager;
    private readonly CapabilityRegistry _registry;
    private readonly StubTransport _transport;
    private readonly AgentCore _agentCore;
    private readonly LifecycleService _svc;

    public LifecycleServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-lifecycle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var storageCfg = new StorageConfig { DataRoot = _tempDir };
        _sessions = new SqliteSessionStore(storageCfg);
        _skillRepo = new FileSkillRepository(storageCfg, NullLogger<FileSkillRepository>.Instance);
        _skillManager = new SkillManager(
            _skillRepo,
            new StubLlmClient("test"),
            new AgentConfig { SkillEvaluationWindow = 5 },
            new JsonRepairService());
        _registry = CapabilityRegistry.CreateInMemory();
        _transport = new StubTransport();

        var router = new SkillRouter(_skillManager);
        var memory = new MemoryManager(
            new MemoryStore(storageCfg),
            new StubLlmClient("mem"));

        _agentCore = new AgentCore(
            new StubLlmClient("agent"),
            router,
            _skillManager,
            memory,
            _sessions,
            new AgentConfig(),
            NullLogger<AgentCore>.Instance,
            new JsonRepairService());

        _svc = new LifecycleService(
            _agentCore,
            _skillManager,
            _registry,
            _transport,
            NullLogger<LifecycleService>.Instance,
            new AgentLifecycleStateHolder(),
            new InFlightTracker());
    }

    public void Dispose()
    {
        try { _sessions.Dispose(); } catch { /* best effort */ }
        try { _registry.Dispose(); } catch { /* best effort */ }
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  Agent inventory
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAgentInventoryAsync_LocalAgent_ReturnsLocalEntry()
    {
        var inventory = await _svc.GetAgentInventoryAsync();

        Assert.Single(inventory);
        Assert.Equal(_agentCore.SessionId, inventory[0].AgentId);
        Assert.Equal("Local Agent", inventory[0].DisplayName);
        Assert.Equal("Running", inventory[0].State);
        Assert.Equal("Healthy", inventory[0].HealthStatus);
        Assert.NotNull(inventory[0].StartedAt);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  Start
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task StartAgentAsync_WhenStopped_TransitionsToRunning()
    {
        await _svc.StopAgentAsync(_agentCore.SessionId);

        var result = await _svc.StartAgentAsync(_agentCore.SessionId);

        Assert.True(result.Success);
        Assert.Equal("Stopped", result.PreviousState);
        Assert.Equal("Running", result.NewState);
        Assert.Contains("started", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartAgentAsync_WhenRunning_Fails()
    {
        var result = await _svc.StartAgentAsync(_agentCore.SessionId);

        Assert.False(result.Success);
        Assert.Contains("Cannot start", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  Stop
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task StopAgentAsync_WhenRunning_Succeeds()
    {
        var result = await _svc.StopAgentAsync(_agentCore.SessionId);

        Assert.True(result.Success);
        Assert.Equal("Running", result.PreviousState);
        Assert.Equal("Stopped", result.NewState);
    }

    [Fact]
    public async Task StopAgentAsync_WhenDraining_CompletesStop()
    {
        // task_080: DrainAgentAsync now waits for in-flight and transitions to Stopped
        // before returning. To exercise the "stop from Draining" path we set the state
        // directly to Draining so the lifecycle service sees a still-draining agent.
        _svc.State.SetState(AgentLifecycleState.Draining);
        var result = await _svc.StopAgentAsync(_agentCore.SessionId);

        Assert.True(result.Success);
        Assert.Equal("Draining", result.PreviousState);
        Assert.Equal("Stopped", result.NewState);
    }

    [Fact]
    public async Task StopAgentAsync_WhenStopped_Fails()
    {
        await _svc.StopAgentAsync(_agentCore.SessionId);
        var result = await _svc.StopAgentAsync(_agentCore.SessionId);

        Assert.False(result.Success);
        Assert.Contains("Cannot stop", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  Drain
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DrainAgentAsync_WhenRunning_Succeeds()
    {
        // task_080: with no in-flight requests, DrainAgentAsync transitions all the way
        // to Stopped (per spec step 5). With in-flight work, the intermediate Draining
        // state is observable — covered in LifecycleDrainTests.
        var result = await _svc.DrainAgentAsync(_agentCore.SessionId);

        Assert.True(result.Success);
        Assert.Equal("Running", result.PreviousState);
        Assert.Equal("Stopped", result.NewState);
        Assert.Contains("drained", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("true", result.Metadata["drainedCleanly"]);
    }

    [Fact]
    public async Task DrainAgentAsync_WhenStopped_Fails()
    {
        await _svc.StopAgentAsync(_agentCore.SessionId);
        var result = await _svc.DrainAgentAsync(_agentCore.SessionId);

        Assert.False(result.Success);
        Assert.Contains("Cannot drain", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  Decommission
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DecommissionAgentAsync_Succeeds()
    {
        var result = await _svc.DecommissionAgentAsync(_agentCore.SessionId);

        Assert.True(result.Success);
        Assert.Equal("Decommissioned", result.NewState);
        Assert.Contains("decommissioned", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  Health check
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CheckHealthAsync_Local_ReturnsHealthyOrDegraded()
    {
        // Agent has no skills loaded, so health is Degraded (issue: "No skills loaded")
        // The agent itself is in Running state, so not Unhealthy
        var result = await _svc.CheckHealthAsync(_agentCore.SessionId);

        Assert.Equal(_agentCore.SessionId, result.AgentId);
        Assert.NotEqual("Unhealthy", result.Status);
        Assert.NotEmpty(result.Details);
        Assert.Contains(result.Issues, i => i.Contains("No skills loaded"));
    }

    [Fact]
    public async Task CheckHealthAsync_NoAgentId_ChecksLocal()
    {
        var result = await _svc.CheckHealthAsync();

        Assert.Equal(_agentCore.SessionId, result.AgentId);
        Assert.NotEmpty(result.Details);
    }

    [Fact]
    public async Task CheckHealthAsync_DecommissionedAgent_ReturnsUnhealthy()
    {
        await _svc.DecommissionAgentAsync(_agentCore.SessionId);

        var result = await _svc.CheckHealthAsync(_agentCore.SessionId);

        Assert.Equal("Unhealthy", result.Status);
        Assert.Contains(result.Issues, i => i.Contains("decommissioned"));
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  Skill package lifecycle
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateSkillPackageAsync_UnknownPackage_Fails()
    {
        var result = await _svc.UpdateSkillPackageAsync("unknown-pkg", _agentCore.SessionId);

        Assert.False(result.Success);
        Assert.Contains("not found", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeployCanaryAsync_UnknownPackage_Fails()
    {
        var result = await _svc.DeployCanaryAsync("unknown-pkg", _agentCore.SessionId);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task PromoteCanaryAsync_NoCanary_Fails()
    {
        var result = await _svc.PromoteCanaryAsync("test-pkg", _agentCore.SessionId);

        Assert.False(result.Success);
        Assert.Contains("No canary", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RollbackSkillPackageAsync_UnknownPackage_Fails()
    {
        var result = await _svc.RollbackSkillPackageAsync("unknown", _agentCore.SessionId);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task RollbackAgentAsync_LocalAgent_Fails()
    {
        var result = await _svc.RollbackAgentAsync(_agentCore.SessionId);

        Assert.False(result.Success);
        Assert.Contains("process restart", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  Stubs
    // ══════════════════════════════════════════════════════════════════════════════

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

    private sealed class StubTransport : ITransport
    {
        public TransportKind Kind => TransportKind.Http;
        public bool SupportsBidirectionalStreaming => false;
        public DeliveryGuarantee DeliveryGuarantee => DeliveryGuarantee.AtMostOnce;

        public Task<TransportResult> SendAsync(string targetAgentId, IntentEnvelope envelope, CancellationToken ct = default)
            => Task.FromResult(TransportResult.Ok(
                IntentResponse.Ok(envelope.RequestId, targetAgentId, "{}"),
                0,
                TransportKind.Http));

        public void Dispose() { }
    }
}
