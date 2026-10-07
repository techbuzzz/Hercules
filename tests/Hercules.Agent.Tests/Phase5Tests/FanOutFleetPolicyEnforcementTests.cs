using Hercules.Fleet;
using Hercules.Mesh;
using Hercules.Mesh.Aggregation;
using Hercules.Mesh.Observability;
using Hercules.Mesh.Router;
using Hercules.Mesh.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Phase5Tests;

/// <summary>
///     Unit tests for task_087 sub-task #4 — FanOutOrchestrator enforces
///     <c>FleetPolicy.MaxFanOutWidth</c> by truncating the routed peer list
///     to the configured cap before fanning out in parallel. Without this
///     guard a generous <c>MeshRouter.MaxPeerCandidates</c> could accidentally
///     fan out to every reachable peer.
/// </summary>
public class FanOutFleetPolicyEnforcementTests
{
    private static FanOutOptions DefaultOptions() => new()
    {
        Enabled = true,
        MaxConcurrency = 8,
        MinPeersForFanOut = 2,
        BudgetCeilingUsd = 1.00m,
        DefaultTimeoutMs = 30_000,
        Strategy = FanOutSelectionStrategy.Deterministic,
        DeterministicCriterion = DeterministicCriterion.HighestConfidence,
        MinResponsesForVoting = 3,
        VotingThreshold = 0.51,
        EnableSchemaValidation = true,
        EnableLlmJudgeFallback = false,
        MaxFanOutWidth = 0 // default = unlimited unless fleet policy overrides
    };

    private static FanOutOrchestrator Build(FanOutOptions opts, IFleetTemplateManager? fleet = null)
    {
        var router = new Mock<IMeshRouter>();
        var transport = new Mock<ITransport>();
        var aggregator = new ResponseAggregator(opts, null, NullLogger<ResponseAggregator>.Instance);
        return new FanOutOrchestrator(
            router.Object,
            transport.Object,
            aggregator,
            opts,
            new CircuitBreaker(),
            NullLogger<FanOutOrchestrator>.Instance,
            observability: null,
            fleetTemplates: fleet);
    }

    private static IEnumerable<PeerCandidate> NPeers(int n) =>
        Enumerable.Range(0, n).Select(i => new PeerCandidate
        {
            AgentId = $"peer-{i:00}",
            DisplayName = $"peer-{i:00}",
            Endpoint = $"http://localhost:9{i:000}",
            CompositeScore = 1.0 - i * 0.01,
            CostHintUsd = 0.01m,
            CircuitState = CircuitState.Closed
        });

    [Fact]
    public async Task MaxFanOutWidth_FromOptions_CapsPeersContacted()
    {
        // [task_087] A non-zero MaxFanOutWidth on FanOutOptions takes
        // precedence and truncates the peer list before fan-out.
        var opts = DefaultOptions();
        opts.MaxFanOutWidth = 2;

        var router = new Mock<IMeshRouter>();
        router.Setup(r => r.RouteAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NPeers(5).ToList());

        var transport = new Mock<ITransport>();
        var sentAgents = new System.Collections.Concurrent.ConcurrentBag<string>();
        transport.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()))
            .Callback<string, IntentEnvelope, CancellationToken>((agent, _, _) => sentAgents.Add(agent))
            .ReturnsAsync((string agent, IntentEnvelope env, CancellationToken _) =>
                TransportResult.Ok(IntentResponse.Ok(env.RequestId, agent, "ok", "direct", confidence: 0.7), 0, TransportKind.Http));

        var aggregator = new ResponseAggregator(opts, null, NullLogger<ResponseAggregator>.Instance);
        var orchestrator = new FanOutOrchestrator(
            router.Object, transport.Object, aggregator, opts,
            new CircuitBreaker(), NullLogger<FanOutOrchestrator>.Instance);

        var envelope = IntentEnvelope.Create("req1", "hercules", "test", "payload");
        var result = await orchestrator.OrchestrateFanOutAsync(envelope, null, null, null);

        // Should be capped to MaxFanOutWidth=2.
        Assert.Equal(2, result.PeersContacted);
        Assert.Equal(2, sentAgents.Count);
    }

    [Fact]
    public async Task MaxFanOutWidth_FromFleetTemplateManager_TakesEffect()
    {
        // [task_087] When FanOutOptions.MaxFanOutWidth=0 (unlimited) and a
        // fleet template with MaxFanOutWidth=3 is present, the template wins.
        var opts = DefaultOptions();
        opts.MaxFanOutWidth = 0;
        var fleet = new StubFleetTemplateManager
        {
            Manifests = new()
            {
                ["greenhouse.fleettemplate"] = new FleetManifest
                {
                    Name = "Greenhouse",
                    Vertical = "greenhouse",
                    Policy = new FleetPolicy { MaxFanOutWidth = 3 }
                }
            }
        };
        var router = new Mock<IMeshRouter>();
        router.Setup(r => r.RouteAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NPeers(7).ToList());
        var transport = new Mock<ITransport>();
        transport.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string agent, IntentEnvelope env, CancellationToken _) =>
                TransportResult.Ok(IntentResponse.Ok(env.RequestId, agent, "ok", "direct", confidence: 0.7), 0, TransportKind.Http));

        var aggregator = new ResponseAggregator(opts, null, NullLogger<ResponseAggregator>.Instance);
        var orchestrator = new FanOutOrchestrator(
            router.Object, transport.Object, aggregator, opts,
            new CircuitBreaker(), NullLogger<FanOutOrchestrator>.Instance,
            observability: null,
            fleetTemplates: fleet);

        var envelope = IntentEnvelope.Create("req1", "hercules", "test", "payload");
        var result = await orchestrator.OrchestrateFanOutAsync(envelope, null, null, null);

        Assert.Equal(3, result.PeersContacted);
    }

    [Fact]
    public async Task MaxFanOutWidth_FleetTemplateFailure_KeepsOptionsDefault()
    {
        // [task_087] If the fleet template manager throws, the orchestrator
        // must log and continue with the FanOutOptions value (or no cap) —
        // never let a template read fault a fan-out.
        var opts = DefaultOptions();
        opts.MaxFanOutWidth = 0;
        var fleet = new ThrowingFleetTemplateManager();
        var router = new Mock<IMeshRouter>();
        router.Setup(r => r.RouteAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NPeers(3).ToList());
        var transport = new Mock<ITransport>();
        transport.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string agent, IntentEnvelope env, CancellationToken _) =>
                TransportResult.Ok(IntentResponse.Ok(env.RequestId, agent, "ok", "direct", confidence: 0.7), 0, TransportKind.Http));

        var aggregator = new ResponseAggregator(opts, null, NullLogger<ResponseAggregator>.Instance);
        var orchestrator = new FanOutOrchestrator(
            router.Object, transport.Object, aggregator, opts,
            new CircuitBreaker(), NullLogger<FanOutOrchestrator>.Instance,
            observability: null,
            fleetTemplates: fleet);

        var envelope = IntentEnvelope.Create("req1", "hercules", "test", "payload");
        var result = await orchestrator.OrchestrateFanOutAsync(envelope, null, null, null);

        // No cap applied — all 3 peers contacted.
        Assert.Equal(3, result.PeersContacted);
    }

    [Fact]
    public async Task MaxFanOutWidth_NoFleetTemplate_AllowsUnlimited()
    {
        // [task_087] When no fleet template manager is wired (default in
        // unit tests / edge deployments) and FanOutOptions.MaxFanOutWidth=0,
        // no cap is applied.
        var opts = DefaultOptions();
        var router = new Mock<IMeshRouter>();
        router.Setup(r => r.RouteAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NPeers(4).ToList());
        var transport = new Mock<ITransport>();
        transport.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string agent, IntentEnvelope env, CancellationToken _) =>
                TransportResult.Ok(IntentResponse.Ok(env.RequestId, agent, "ok", "direct", confidence: 0.7), 0, TransportKind.Http));

        var aggregator = new ResponseAggregator(opts, null, NullLogger<ResponseAggregator>.Instance);
        var orchestrator = new FanOutOrchestrator(
            router.Object, transport.Object, aggregator, opts,
            new CircuitBreaker(), NullLogger<FanOutOrchestrator>.Instance);

        var envelope = IntentEnvelope.Create("req1", "hercules", "test", "payload");
        var result = await orchestrator.OrchestrateFanOutAsync(envelope, null, null, null);

        Assert.Equal(4, result.PeersContacted);
    }

    [Fact]
    public void FanOutOptions_MaxFanOutWidth_DefaultsToZero()
    {
        // [task_087] The default (0) means "unlimited unless overridden",
        // preserving the previous behaviour for deployments that have not yet
        // pushed a fleet policy.
        var opts = new FanOutOptions();
        Assert.Equal(0, opts.MaxFanOutWidth);
    }

    // ── Stubs ───────────────────────────────────────────────────────────────

    private sealed class StubFleetTemplateManager : IFleetTemplateManager
    {
        public Dictionary<string, FleetManifest> Manifests { get; set; } = new();

        public string GetFleetTemplatesDir() => "/tmp/fake";
        public IReadOnlyList<FleetTemplateEntry> List() =>
            Manifests.Keys
                .Select(k => new FleetTemplateEntry(k, k, "", "", 1, $"/tmp/fake/{k}"))
                .ToList();
        public FleetManifest GetManifest(string fileName) => Manifests[fileName];
        public ApplyFleetTemplateResult Apply(string fileName, FleetConflictResolution conflict = FleetConflictResolution.Rename)
            => new() { TemplateName = fileName };
    }

    private sealed class ThrowingFleetTemplateManager : IFleetTemplateManager
    {
        public string GetFleetTemplatesDir() => "/tmp/fake";
        public IReadOnlyList<FleetTemplateEntry> List() => throw new InvalidOperationException("simulated");
        public FleetManifest GetManifest(string fileName) => throw new InvalidOperationException("simulated");
        public ApplyFleetTemplateResult Apply(string fileName, FleetConflictResolution conflict = FleetConflictResolution.Rename)
            => throw new InvalidOperationException("simulated");
    }
}
