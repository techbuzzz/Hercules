using Hercules.Config;
using Hercules.Mesh;
using Hercules.Mesh.Policy;
using Hercules.Mesh.Router;
using Hercules.Mesh.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Phase4Tests;

/// <summary>
///     Unit tests for Phase 4 Complexity Router (task_044).
///     Tests: ComplexityClassifier (rule-based heuristics), ComplexityRouter (path selection),
///     ComplexityRouterOptions defaults, and IComplexityClassifier interface compliance.
/// </summary>
public class ComplexityRouterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CapabilityRegistry _registry;
    private readonly CircuitBreaker _breaker;
    private readonly ITrustAdmissionPolicy _trustPolicy;
    private readonly RouterHealthTracker _healthTracker;
    private readonly MeshRouterOptions _meshRouterOptions;
    private readonly ComplexityRouterOptions _options;
    private readonly IComplexityClassifier _classifier;
    private readonly CapabilityMeshRouter _meshRouter;
    private readonly IComplexityRouter _router;

    public ComplexityRouterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-complexity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var dbPath = Path.Combine(_tempDir, "registry.db");
        _registry = new CapabilityRegistry(dbPath);
        _breaker = new CircuitBreaker();
        _trustPolicy = new TrustAdmissionPolicyEngine(
            new TrustAdmissionConfig { Enabled = false },
            NullLogger<TrustAdmissionPolicyEngine>.Instance);
        _healthTracker = new RouterHealthTracker();
        _meshRouterOptions = new MeshRouterOptions { Enabled = true, MinConfidenceThreshold = 0.05 };
        _options = new ComplexityRouterOptions
        {
            Enabled = true,
            SimpleMaxPayloadBytes = 512,
            SimpleMaxCostUsd = 0.001m,
            ModerateMaxPayloadBytes = 4096,
            ModerateMaxCostUsd = 0.10m,
            MaxPeersForFanOut = 5,
            EnableFanOut = true,
            SmallModelCostThresholdUsd = 0.0005m,
            LargeModelCostThresholdUsd = 0.01m,
            MaxBudgetPerRequestUsd = 1.00m,
            DefaultPeerRetryCount = 2
        };

        _classifier = new ComplexityClassifier(_options, NullLogger<ComplexityClassifier>.Instance);
        _meshRouter = new CapabilityMeshRouter(
            _registry, _breaker, _trustPolicy, _healthTracker,
            _meshRouterOptions, NullLogger<CapabilityMeshRouter>.Instance);
        _router = new ComplexityRouter(
            _classifier, _meshRouter, _options,
            NullLogger<ComplexityRouter>.Instance);

        // Peers are registered on-demand in specific tests via RegisterPeerWithCapability().
        // Tests that verify peer routing (SinglePeer/FanOut) must call RegisterPeerWithCapability() first.
        // Tests that verify no-peer fallback (LocalLargeModel) rely on no peers being registered.
    }

    /// <summary>Register a test peer with a given capability name (for tests that verify peer routing).</summary>
    private void RegisterPeerWithCapability(string capabilityName)
    {
        var agentId = $"peer-{Guid.NewGuid():N}";
        _registry.Register(new AgentManifest
        {
            AgentId = agentId,
            DisplayName = agentId,
            Endpoint = $"http://localhost:{9000 + _registry.FindByCapability("*").Count + 1}",
            Capabilities = new List<ManifestCapability>
            {
                new() { Name = capabilityName, Description = capabilityName, PhraseReceivers = new List<string> { capabilityName } }
            }
        });
        _registry.UpdateTrustLevel(agentId, "verified");
    }

    public void Dispose()
    {
        _registry.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    // ─── IComplexityClassifier.AnalyzeAsync ──────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_TrivialIntent_ReturnsSimpleLevel()
    {
        var analysis = await _classifier.AnalyzeAsync(
            intent: "hello",
            payloadSizeBytes: 10,
            toolCount: 0,
            estimatedCostUsd: 0.0001m,
            requestsPeer: false,
            noLocalCapability: false);

        Assert.NotNull(analysis);
        Assert.Equal("hello", analysis.Intent);
        Assert.Equal(10, analysis.PayloadSizeBytes);
        Assert.Equal(0, analysis.ToolCount);
        Assert.False(analysis.HasSafetySensitiveTool);
        Assert.False(analysis.MentionsMultiStep);
        Assert.InRange(analysis.EstimatedComplexityScore, 0, 1);
    }

    [Fact]
    public async Task AnalyzeAsync_ComplexKeywords_ReturnsHighScore()
    {
        var analysis = await _classifier.AnalyzeAsync(
            intent: "refactor the database schema and migrate to postgres",
            payloadSizeBytes: 5000,
            toolCount: 3,
            estimatedCostUsd: 0.50m,
            requestsPeer: false,
            noLocalCapability: false);

        Assert.NotNull(analysis);
        Assert.True(analysis.EstimatedComplexityScore > 0.5,
            $"Expected score > 0.5, got {analysis.EstimatedComplexityScore}");
        Assert.True(analysis.IntentKeywords.Count > 0,
            "Expected keyword extraction from complex intent");
    }

    [Fact]
    public async Task AnalyzeAsync_SafetySensitiveKeywords_FlagsHasSafetySensitiveTool()
    {
        var analysis = await _classifier.AnalyzeAsync(
            intent: "delete all records from the users table",
            payloadSizeBytes: 100,
            toolCount: 1,
            estimatedCostUsd: 0.001m,
            requestsPeer: false,
            noLocalCapability: false);

        Assert.True(analysis.HasSafetySensitiveTool,
            "Expected safety-sensitive flag for 'delete' keyword");
    }

    [Fact]
    public async Task AnalyzeAsync_MultiStepKeywords_FlagsMentionsMultiStep()
    {
        var analysis = await _classifier.AnalyzeAsync(
            intent: "step by step, first analyze then refactor the api",
            payloadSizeBytes: 1000,
            toolCount: 2,
            estimatedCostUsd: 0.01m,
            requestsPeer: false,
            noLocalCapability: false);

        Assert.True(analysis.MentionsMultiStep,
            "Expected multi-step flag for 'step by step' keyword");
    }

    [Fact]
    public async Task AnalyzeAsync_PeerRequest_FlagsRequestsPeer()
    {
        var analysis = await _classifier.AnalyzeAsync(
            intent: "route to another agent",
            payloadSizeBytes: 50,
            toolCount: 0,
            estimatedCostUsd: 0.001m,
            requestsPeer: true,
            noLocalCapability: false);

        Assert.True(analysis.RequestsPeer);
    }

    // ─── IComplexityClassifier.Classify ─────────────────────────────────────

    [Theory]
    [InlineData(0.05, ComplexityLevel.Simple)]
    [InlineData(0.15, ComplexityLevel.Simple)]
    [InlineData(0.29, ComplexityLevel.Moderate)]
    [InlineData(0.30, ComplexityLevel.Moderate)]
    [InlineData(0.45, ComplexityLevel.Moderate)]
    [InlineData(0.59, ComplexityLevel.Complex)]
    [InlineData(0.60, ComplexityLevel.Complex)]
    [InlineData(0.80, ComplexityLevel.Complex)]
    [InlineData(0.99, ComplexityLevel.Complex)]
    public void Classify_ScoreThresholds_MapsCorrectly(double score, ComplexityLevel expected)
    {
        var analysis = new ComplexityIntentAnalysis
        {
            Intent = "test",
            EstimatedComplexityScore = score
        };

        ComplexityLevel result = _classifier.Classify(analysis);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ClassifyFast_SmallPayload_ReturnsSimple()
    {
        var result = ((ComplexityClassifier)_classifier).ClassifyFast(100, 0, 0.0001m);
        Assert.Equal(ComplexityLevel.Simple, result);
    }

    [Fact]
    public void ClassifyFast_LargePayload_ReturnsComplex()
    {
        // 4 tools is enough to push score above 0.50 with large payload + high cost
        var result = ((ComplexityClassifier)_classifier).ClassifyFast(100_000, 4, 1.00m);
        Assert.Equal(ComplexityLevel.Complex, result);
    }

    // ─── ComplexityRouterOptions defaults ────────────────────────────────────

    [Fact]
    public void ComplexityRouterOptions_DefaultsAreReasonable()
    {
        var opts = new ComplexityRouterOptions();

        Assert.True(opts.Enabled);
        Assert.Equal(512, opts.SimpleMaxPayloadBytes);
        Assert.Equal(0.001m, opts.SimpleMaxCostUsd);
        Assert.Equal(4096, opts.ModerateMaxPayloadBytes);
        Assert.Equal(0.10m, opts.ModerateMaxCostUsd);
        Assert.Equal(5, opts.MaxPeersForFanOut);
        Assert.True(opts.EnableFanOut);
        Assert.Equal(0.0005m, opts.SmallModelCostThresholdUsd);
        Assert.Equal(0.01m, opts.LargeModelCostThresholdUsd);
        Assert.Equal(1.00m, opts.MaxBudgetPerRequestUsd);
        Assert.Equal(2, opts.DefaultPeerRetryCount);
    }

    // ─── IComplexityRouter.ClassifyAndRouteAsync ────────────────────────────

    [Fact]
    public async Task ClassifyAndRouteAsync_Disabled_ReturnsLocalDirect()
    {
        var disabledOpts = new ComplexityRouterOptions { Enabled = false };
        var classifier = new ComplexityClassifier(disabledOpts, NullLogger<ComplexityClassifier>.Instance);
        var disabledRouter = new ComplexityRouter(
            classifier, _meshRouter, disabledOpts, NullLogger<ComplexityRouter>.Instance);

        var decision = await disabledRouter.ClassifyAndRouteAsync(
            intent: "hello",
            payloadSizeBytes: 10,
            toolCount: 0,
            estimatedCostUsd: 0.0001m,
            costBudgetUsd: null,
            requestsPeer: false,
            noLocalCapability: false);

        Assert.Equal(ComplexityLevel.Simple, decision.Complexity);
        Assert.Equal(ExecutionPath.LocalDirect, decision.ExecutionPath);
        Assert.Equal("Complexity router is disabled", decision.Reason);
        Assert.False(decision.WasDowngraded);
    }

    [Fact]
    public async Task ClassifyAndRouteAsync_TrivialIntent_ReturnsLocalDirect()
    {
        var decision = await _router.ClassifyAndRouteAsync(
            intent: "hello world",
            payloadSizeBytes: 10,
            toolCount: 0,
            estimatedCostUsd: 0.0001m,
            costBudgetUsd: null,
            requestsPeer: false,
            noLocalCapability: false);

        Assert.Equal(ComplexityLevel.Simple, decision.Complexity);
        Assert.Equal(ExecutionPath.LocalDirect, decision.ExecutionPath);
        Assert.True(decision.WithinBudget);
        Assert.NotEmpty(decision.Reason);
        Assert.Equal(0, decision.SuggestedMaxRetries);
    }

    [Fact]
    public async Task ClassifyAndRouteAsync_ExplicitPeerRequest_ReturnsSinglePeer()
    {
        // Disable fan-out to ensure SinglePeer (not FanOut) for explicit peer request
        var singlePeerOpts = new ComplexityRouterOptions { EnableFanOut = false };
        var singlePeerClassifier = new ComplexityClassifier(singlePeerOpts, NullLogger<ComplexityClassifier>.Instance);
        var singlePeerRouter = new ComplexityRouter(
            singlePeerClassifier, _meshRouter, singlePeerOpts, NullLogger<ComplexityRouter>.Instance);
        RegisterPeerWithCapability("refactor-api");
        var decision = await singlePeerRouter.ClassifyAndRouteAsync(
            intent: "refactor-api",
            payloadSizeBytes: 100,
            toolCount: 0,
            estimatedCostUsd: 0.001m,
            costBudgetUsd: null,
            requestsPeer: true,
            noLocalCapability: false);

        Assert.Equal(ExecutionPath.SinglePeer, decision.ExecutionPath);
        Assert.Contains("peer", decision.Reason.ToLowerInvariant());
    }

    [Fact]
    public async Task ClassifyAndRouteAsync_NoLocalCapability_ReturnsSinglePeer()
    {
        // Register a peer so mesh router can find it; noLocalCapability forces peer routing
        RegisterPeerWithCapability("refactor-api");
        var decision = await _router.ClassifyAndRouteAsync(
            intent: "refactor-api",
            payloadSizeBytes: 100,
            toolCount: 0,
            estimatedCostUsd: 0.001m,
            costBudgetUsd: null,
            requestsPeer: false,
            noLocalCapability: true);

        Assert.True(decision.ExecutionPath is ExecutionPath.SinglePeer or ExecutionPath.FanOut,
            $"Expected peer path, got {decision.ExecutionPath}");
    }

    [Fact]
    public async Task ClassifyAndRouteAsync_ComplexWithSafetyTools_ReturnsFanOut()
    {
        // Register a peer with matching capability so mesh router finds it
        RegisterPeerWithCapability("database-migrate");
        var decision = await _router.ClassifyAndRouteAsync(
            intent: "database-migrate",
            payloadSizeBytes: 5000,
            toolCount: 3,
            estimatedCostUsd: 0.50m,
            costBudgetUsd: null,
            requestsPeer: false,
            noLocalCapability: true);

        Assert.True(decision.ExecutionPath is ExecutionPath.FanOut or ExecutionPath.SinglePeer,
            $"Expected fan-out or single peer, got {decision.ExecutionPath}");
        Assert.Equal(ComplexityLevel.Complex, decision.Complexity);
    }

    [Fact]
    public async Task ClassifyAndRouteAsync_BudgetExceeded_ReturnsBlocked()
    {
        var decision = await _router.ClassifyAndRouteAsync(
            intent: "run full benchmark suite",
            payloadSizeBytes: 10_000,
            toolCount: 5,
            estimatedCostUsd: 5.00m, // way over $1 budget
            costBudgetUsd: 1.00m,
            requestsPeer: false,
            noLocalCapability: false);

        Assert.Equal(ExecutionPath.Blocked, decision.ExecutionPath);
        Assert.Contains("exceeds", decision.Reason.ToLowerInvariant());
        Assert.True(decision.Confidence > 0.5);
    }

    [Fact]
    public async Task ClassifyAndRouteAsync_SimpleWithTool_ChecksCostThreshold()
    {
        // Below small model threshold → LocalDirect
        var below = await _router.ClassifyAndRouteAsync(
            intent: "fix bug",
            payloadSizeBytes: 200,
            toolCount: 1,
            estimatedCostUsd: 0.0001m, // < SmallModelCostThresholdUsd
            costBudgetUsd: null,
            requestsPeer: false,
            noLocalCapability: false);

        Assert.Equal(ExecutionPath.LocalDirect, below.ExecutionPath);

        // Above small model threshold → LocalSmallModel
        var above = await _router.ClassifyAndRouteAsync(
            intent: "fix bug",
            payloadSizeBytes: 200,
            toolCount: 1,
            estimatedCostUsd: 0.005m, // > SmallModelCostThresholdUsd
            costBudgetUsd: null,
            requestsPeer: false,
            noLocalCapability: false);

        Assert.Equal(ExecutionPath.LocalSmallModel, above.ExecutionPath);
    }

    [Fact]
    public async Task ClassifyAndRouteAsync_ModerateIntent_SelectsSmallOrLargeModel()
    {
        // Moderate + cheap → small model
        var cheap = await _router.ClassifyAndRouteAsync(
            intent: "review and improve this function",
            payloadSizeBytes: 500,
            toolCount: 2,
            estimatedCostUsd: 0.005m, // < LargeModelCostThresholdUsd
            costBudgetUsd: null,
            requestsPeer: false,
            noLocalCapability: false);

        Assert.True(cheap.ExecutionPath is ExecutionPath.LocalSmallModel or ExecutionPath.LocalLargeModel,
            $"Expected small or large model, got {cheap.ExecutionPath}");
        Assert.Equal(ComplexityLevel.Moderate, cheap.Complexity);

        // Moderate + expensive → large model
        var expensive = await _router.ClassifyAndRouteAsync(
            intent: "review and improve this function",
            payloadSizeBytes: 500,
            toolCount: 2,
            estimatedCostUsd: 0.50m, // > LargeModelCostThresholdUsd
            costBudgetUsd: null,
            requestsPeer: false,
            noLocalCapability: false);

        Assert.Equal(ExecutionPath.LocalLargeModel, expensive.ExecutionPath);
    }

    [Fact]
    public async Task ClassifyAndRouteAsync_SuggestedRetries_MatchesLevel()
    {
        var simpleOpts = new ComplexityRouterOptions { MaxRetriesForSimple = 0, MaxRetriesForModerate = 1, MaxRetriesForComplex = 2 };
        var simpleClassifier = new ComplexityClassifier(simpleOpts, NullLogger<ComplexityClassifier>.Instance);
        var simpleRouter = new ComplexityRouter(
            simpleClassifier, _meshRouter, simpleOpts, NullLogger<ComplexityRouter>.Instance);

        var simple = await simpleRouter.ClassifyAndRouteAsync(
            "hello", 10, 0, 0.0001m, null, false, false);
        Assert.Equal(0, simple.SuggestedMaxRetries);

        var moderate = await simpleRouter.ClassifyAndRouteAsync(
            "fix the bug carefully", 500, 2, 0.005m, null, false, false);
        Assert.Equal(1, moderate.SuggestedMaxRetries);
    }

    [Fact]
    public async Task ClassifyAndRouteAsync_FanOutMaxPeers_LimitedByConfig()
    {
        RegisterPeerWithCapability("analyze-architecture");
        var fanOutOpts = new ComplexityRouterOptions
        {
            MaxPeersForFanOut = 3,
            EnableFanOut = true
        };
        var fanOutClassifier = new ComplexityClassifier(fanOutOpts, NullLogger<ComplexityClassifier>.Instance);
        var fanOutRouter = new ComplexityRouter(
            fanOutClassifier, _meshRouter, fanOutOpts, NullLogger<ComplexityRouter>.Instance);

        var decision = await fanOutRouter.ClassifyAndRouteAsync(
            intent: "analyze-architecture",
            payloadSizeBytes: 5000,
            toolCount: 3,
            estimatedCostUsd: 0.50m,
            costBudgetUsd: null,
            requestsPeer: true, // forces peer routing
            noLocalCapability: true);

        Assert.True(decision.MaxFanOutPeers <= 3,
            $"Expected max fan-out ≤ 3, got {decision.MaxFanOutPeers}");
    }

    [Fact]
    public async Task ClassifyAndRouteAsync_FanOutDisabled_DowngradesToSinglePeer()
    {
        RegisterPeerWithCapability("refactor-api");
        var noFanOutOpts = new ComplexityRouterOptions { EnableFanOut = false };
        var noFanOutClassifier = new ComplexityClassifier(noFanOutOpts, NullLogger<ComplexityClassifier>.Instance);
        var noFanOutRouter = new ComplexityRouter(
            noFanOutClassifier, _meshRouter, noFanOutOpts, NullLogger<ComplexityRouter>.Instance);

        var decision = await noFanOutRouter.ClassifyAndRouteAsync(
            intent: "refactor-api",
            payloadSizeBytes: 5000,
            toolCount: 3,
            estimatedCostUsd: 0.50m,
            costBudgetUsd: null,
            requestsPeer: true,
            noLocalCapability: true);

        Assert.Equal(ExecutionPath.SinglePeer, decision.ExecutionPath);
    }

    [Fact]
    public async Task ClassifyAndRouteAsync_NoPeersAvailable_DowngradesToLocalLargeModel()
    {
        // No peers registered in registry → mesh router returns empty
        var decision = await _router.ClassifyAndRouteAsync(
            intent: "refactor-api",
            payloadSizeBytes: 500,
            toolCount: 1,
            estimatedCostUsd: 0.10m,
            costBudgetUsd: null,
            requestsPeer: false,
            noLocalCapability: true); // forces peer routing

        Assert.Equal(ExecutionPath.LocalLargeModel, decision.ExecutionPath);
        Assert.True(decision.WasDowngraded);
        Assert.Empty(decision.PeerCandidates ?? Array.Empty<PeerCandidate>());
    }

    // ─── ComplexityLevel enum values ────────────────────────────────────────

    [Theory]
    [InlineData(ComplexityLevel.Simple, 0)]
    [InlineData(ComplexityLevel.Moderate, 1)]
    [InlineData(ComplexityLevel.Complex, 2)]
    public void ComplexityLevel_HasCorrectIntValue(ComplexityLevel level, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)level);
    }

    // ─── ExecutionPath enum values ──────────────────────────────────────────

    [Theory]
    [InlineData(ExecutionPath.LocalDirect, 0)]
    [InlineData(ExecutionPath.LocalSmallModel, 1)]
    [InlineData(ExecutionPath.LocalLargeModel, 2)]
    [InlineData(ExecutionPath.SinglePeer, 3)]
    [InlineData(ExecutionPath.FanOut, 4)]
    [InlineData(ExecutionPath.Blocked, 5)]
    public void ExecutionPath_HasCorrectIntValue(ExecutionPath path, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)path);
    }

    // ─── ComplexityRoutingDecision record ───────────────────────────────────

    [Fact]
    public void ComplexityRoutingDecision_RecordProperties_Work()
    {
        var peers = new List<PeerCandidate>();
        var decision = new ComplexityRoutingDecision
        {
            Complexity = ComplexityLevel.Moderate,
            ExecutionPath = ExecutionPath.LocalSmallModel,
            Reason = "Test reason",
            EstimatedCostUsd = 0.005m,
            Confidence = 0.75,
            SuggestedMaxRetries = 1,
            PeerCandidates = peers,
            MaxFanOutPeers = null,
            WithinBudget = true,
            WasDowngraded = false
        };

        Assert.Equal(ComplexityLevel.Moderate, decision.Complexity);
        Assert.Equal(ExecutionPath.LocalSmallModel, decision.ExecutionPath);
        Assert.Equal("Test reason", decision.Reason);
        Assert.Equal(0.005m, decision.EstimatedCostUsd);
        Assert.Equal(0.75, decision.Confidence);
        Assert.Equal(1, decision.SuggestedMaxRetries);
        Assert.Empty(decision.PeerCandidates!);
        Assert.Null(decision.MaxFanOutPeers);
        Assert.True(decision.WithinBudget);
        Assert.False(decision.WasDowngraded);
    }

    // ─── ComplexityIntentAnalysis record ────────────────────────────────────

    [Fact]
    public void ComplexityIntentAnalysis_RecordProperties_Work()
    {
        var keywords = new List<string> { "refactor", "database" };
        var analysis = new ComplexityIntentAnalysis
        {
            Intent = "refactor database",
            PayloadSizeBytes = 5000,
            ToolCount = 3,
            EstimatedCostUsd = 0.50m,
            IntentKeywords = keywords,
            HasSafetySensitiveTool = true,
            EstimatedComplexityScore = 0.85,
            MentionsMultiStep = true,
            IsIdempotent = false,
            RequestsPeer = false,
            NoLocalCapability = true
        };

        Assert.Equal("refactor database", analysis.Intent);
        Assert.Equal(5000, analysis.PayloadSizeBytes);
        Assert.Equal(3, analysis.ToolCount);
        Assert.Equal(0.50m, analysis.EstimatedCostUsd);
        Assert.Equal(2, analysis.IntentKeywords.Count);
        Assert.True(analysis.HasSafetySensitiveTool);
        Assert.Equal(0.85, analysis.EstimatedComplexityScore);
        Assert.True(analysis.MentionsMultiStep);
        Assert.False(analysis.IsIdempotent);
        Assert.False(analysis.RequestsPeer);
        Assert.True(analysis.NoLocalCapability);
    }
}
