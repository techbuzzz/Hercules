using Hercules.Config;
using Hercules.Mesh;
using Hercules.Mesh.Policy;
using Hercules.Mesh.Router;
using Hercules.Mesh.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Phase4Tests;

/// <summary>
///     Unit tests for the Phase 4 Mesh Router (task_043).
///     Tests: RouterRanking, RouterHealthTracker, MeshRouter (IMeshRouter).
/// </summary>
public class RouterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CapabilityRegistry _registry;
    private readonly CircuitBreaker _breaker;
    private readonly ITrustAdmissionPolicy _trustPolicy;
    private readonly RouterHealthTracker _healthTracker;
    private readonly MeshRouterOptions _options;
    private readonly CapabilityMeshRouter _router;

    public RouterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-router-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var dbPath = Path.Combine(_tempDir, "registry.db");
        _registry = new CapabilityRegistry(dbPath);

        _breaker = new CircuitBreaker { FailureThreshold = 5 };
        _trustPolicy = new TrustAdmissionPolicyEngine(
            new TrustAdmissionConfig { Enabled = true, PolicyMode = "DryRun" },
            NullLogger<TrustAdmissionPolicyEngine>.Instance);

        _healthTracker = new RouterHealthTracker();
        _options = new MeshRouterOptions
        {
            Enabled = true,
            MinConfidenceThreshold = 0.1,
            MaxPeerCandidates = 10,
            Weights = new RouterWeights { Health = 0.3, Latency = 0.2, Quality = 0.25, Trust = 0.25 }
        };

        _router = new CapabilityMeshRouter(
            _registry,
            _breaker,
            _trustPolicy,
            _healthTracker,
            _options,
            NullLogger<CapabilityMeshRouter>.Instance);
    }

    public void Dispose()
    {
        _registry.Dispose();
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { }
    }

    // =====================================================================
    // RouterRanking tests
    // =====================================================================

    [Fact]
    public void ComputeScore_FullyHealthyPeer_ReturnsHighScore()
    {
        var candidate = new PeerCandidate
        {
            AgentId = "peer-1",
            HealthScore = 1.0,
            LatencyMs = 50,
            QualityScore = 1.0,
            TrustScore = 1.0,
            CompositeScore = 0
        };

        double score = RouterRanking.ComputeScore(candidate, _options.Weights);

        Assert.True(score > 0.8, $"Expected > 0.8, got {score:F4}");
    }

    [Fact]
    public void ComputeScore_UnhealthyPeer_ReturnsLowScore()
    {
        var candidate = new PeerCandidate
        {
            AgentId = "peer-2",
            HealthScore = 0.1,
            LatencyMs = 5000,
            QualityScore = 0.1,
            TrustScore = 0.1,
            CompositeScore = 0
        };

        double score = RouterRanking.ComputeScore(candidate, _options.Weights);

        Assert.True(score < 0.3, $"Expected < 0.3, got {score:F4}");
    }

    [Fact]
    public void ComputeScore_ZeroWeights_ReturnsZero()
    {
        var candidate = new PeerCandidate
        {
            AgentId = "peer-3",
            HealthScore = 1.0,
            LatencyMs = 50,
            QualityScore = 1.0,
            TrustScore = 1.0,
            CompositeScore = 0
        };

        var zeroWeights = new RouterWeights { Health = 0, Latency = 0, Quality = 0, Trust = 0 };
        double score = RouterRanking.ComputeScore(candidate, zeroWeights);

        Assert.Equal(0, score);
    }

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(10, 1.0)]
    [InlineData(5000, 0.5)]
    [InlineData(10000, 0.0)]
    [InlineData(20000, 0.0)]
    public void LatencyScore_VariousLatencies_ReturnsCorrectScore(int latencyMs, double expected)
    {
        double score = RouterRanking.LatencyScore(latencyMs);

        Assert.Equal(expected, score, precision: 2);
    }

    [Theory]
    [InlineData("verified", 1.0)]
    [InlineData("trusted", 0.9)]
    [InlineData("provisionally-trusted", 0.7)]
    [InlineData("unverified", 0.5)]
    [InlineData("suspicious", 0.2)]
    [InlineData("denied", 0.0)]
    [InlineData("unknown-level", 0.5)]
    public void NormaliseTrust_VariousLevels_ReturnsCorrectScore(string level, double expected)
    {
        double score = RouterRanking.NormaliseTrust(level);

        Assert.Equal(expected, score);
    }

    [Fact]
    public void ComputeScore_NormalisedWeights_SumsToOne()
    {
        var weights = new RouterWeights { Health = 0.3, Latency = 0.2, Quality = 0.25, Trust = 0.25 };
        double total = weights.Health + weights.Latency + weights.Quality + weights.Trust;
        Assert.Equal(1.0, total);
    }

    // =====================================================================
    // RouterHealthTracker tests
    // =====================================================================

    [Fact]
    public void RouterHealthTracker_UnknownPeer_ReturnsFullHealth()
    {
        double score = _healthTracker.GetHealthScore("unknown-peer");

        Assert.Equal(1.0, score);
    }

    [Fact]
    public void RouterHealthTracker_AllSuccesses_ReturnsFullHealth()
    {
        for (var i = 0; i < 10; i++)
        {
            _healthTracker.RecordSuccess("peer-a");
        }

        double score = _healthTracker.GetHealthScore("peer-a");

        Assert.Equal(1.0, score);
    }

    [Fact]
    public void RouterHealthTracker_AllFailures_ReturnsZeroHealth()
    {
        for (var i = 0; i < 10; i++)
        {
            _healthTracker.RecordFailure("peer-b");
        }

        double score = _healthTracker.GetHealthScore("peer-b");

        Assert.Equal(0.0, score);
    }

    [Fact]
    public void RouterHealthTracker_MixedResults_ReturnsCorrectHealth()
    {
        // 7 successes, 3 failures → 0.7
        for (var i = 0; i < 7; i++) _healthTracker.RecordSuccess("peer-c");
        for (var i = 0; i < 3; i++) _healthTracker.RecordFailure("peer-c");

        double score = _healthTracker.GetHealthScore("peer-c");

        Assert.Equal(0.7, score);
    }

    [Fact]
    public void RouterHealthTracker_RecordsLatency()
    {
        _healthTracker.RecordSuccess("peer-d", latencyMs: 100);
        _healthTracker.RecordSuccess("peer-d", latencyMs: 200);
        _healthTracker.RecordSuccess("peer-d", latencyMs: 300);

        int avg = _healthTracker.GetAverageLatencyMs("peer-d");

        Assert.Equal(200, avg);
    }

    [Fact]
    public void RouterHealthTracker_IsCircuitOpen_Closed_ReturnsFalse()
    {
        bool open = _healthTracker.IsCircuitOpen("peer", CircuitState.Closed);

        Assert.False(open);
    }

    [Fact]
    public void RouterHealthTracker_IsCircuitOpen_Open_ReturnsTrue()
    {
        bool open = _healthTracker.IsCircuitOpen("peer", CircuitState.Open);

        Assert.True(open);
    }

    // =====================================================================
    // MeshRouter (IMeshRouter) tests
    // =====================================================================

    [Fact]
    public async Task RouteAsync_DisabledRouter_ReturnsEmpty()
    {
        var disabledOptions = new MeshRouterOptions { Enabled = false };
        var disabledRouter = new CapabilityMeshRouter(
            _registry, _breaker, _trustPolicy, _healthTracker,
            disabledOptions, NullLogger<CapabilityMeshRouter>.Instance);

        var result = await disabledRouter.RouteAsync("any-capability");

        Assert.Empty(result);
    }

    [Fact]
    public async Task RouteAsync_NoMatchingAgents_ReturnsEmpty()
    {
        var result = await _router.RouteAsync("non-existent-capability");

        Assert.Empty(result);
    }

    [Fact]
    public async Task RouteAsync_WithRegisteredAgents_ReturnsCandidates()
    {
        // Register a peer agent
        var manifest = new AgentManifest
        {
            AgentId = "peer-alpha",
            DisplayName = "Alpha Peer",
            Endpoint = "http://localhost:6000",
            Capabilities = new List<ManifestCapability>
            {
                new() { Name = "alpha-cap", Description = "Alpha capability" }
            }
        };
        _registry.Register(manifest);

        var result = await _router.RouteAsync("alpha-cap");

        Assert.Single(result);
        Assert.Equal("peer-alpha", result[0].AgentId);
        Assert.True(result[0].CompositeScore > 0);
        Assert.True(result[0].TrustPassed);
    }

    [Fact]
    public async Task RouteAsync_WithBudgetConstraint_ExcludesExpensivePeers()
    {
        var manifest = new AgentManifest
        {
            AgentId = "expensive-peer",
            DisplayName = "Expensive Peer",
            Endpoint = "http://localhost:6001",
            Capabilities = new List<ManifestCapability>
            {
                new() { Name = "costly-cap", Description = "Costly capability" }
            }
        };
        _registry.Register(manifest);
        _registry.UpdateCostHint("expensive-peer", 100m);

        var result = await _router.RouteAsync("costly-cap", maxCostUsd: 10m);

        Assert.Empty(result);
    }

    [Fact]
    public async Task RouteAsync_WithinBudget_IncludesPeer()
    {
        var manifest = new AgentManifest
        {
            AgentId = "cheap-peer",
            DisplayName = "Cheap Peer",
            Endpoint = "http://localhost:6002",
            Capabilities = new List<ManifestCapability>
            {
                new() { Name = "cheap-cap", Description = "Cheap capability" }
            }
        };
        _registry.Register(manifest);
        _registry.UpdateCostHint("cheap-peer", 5m);

        var result = await _router.RouteAsync("cheap-cap", maxCostUsd: 10m);

        Assert.Single(result);
        Assert.Equal("cheap-peer", result[0].AgentId);
    }

    [Fact]
    public async Task RouteAsync_ExcludesDeniedTrustLevel()
    {
        var manifest = new AgentManifest
        {
            AgentId = "denied-peer",
            DisplayName = "Denied Peer",
            Endpoint = "http://localhost:6003",
            Capabilities = new List<ManifestCapability>
            {
                new() { Name = "denied-cap", Description = "Denied capability" }
            }
        };
        _registry.Register(manifest);
        _registry.UpdateTrustLevel("denied-peer", "denied");

        var result = await _router.RouteAsync("denied-cap");

        Assert.Empty(result);
    }

    [Fact]
    public async Task RouteAsync_ExcludesCircuitOpenPeers()
    {
        var manifest = new AgentManifest
        {
            AgentId = "broken-peer",
            DisplayName = "Broken Peer",
            Endpoint = "http://localhost:6004",
            Capabilities = new List<ManifestCapability>
            {
                new() { Name = "broken-cap", Description = "Broken capability" }
            }
        };
        _registry.Register(manifest);

        // Trip the circuit breaker
        for (var i = 0; i < 5; i++) _breaker.RecordFailure("broken-peer");

        var result = await _router.RouteAsync("broken-cap");

        Assert.Empty(result);
    }

    [Fact]
    public async Task RouteAsync_ResultsSortedByCompositeScoreDescending()
    {
        // Register two peers with different quality
        _registry.Register(new AgentManifest
        {
            AgentId = "slow-peer",
            DisplayName = "Slow Peer",
            Endpoint = "http://localhost:6010",
            Capabilities = new List<ManifestCapability> { new() { Name = "dual-cap" } }
        });
        _registry.UpdateCostHint("slow-peer", 0);
        _registry.UpdateTrustLevel("slow-peer", "verified");

        _registry.Register(new AgentManifest
        {
            AgentId = "fast-peer",
            DisplayName = "Fast Peer",
            Endpoint = "http://localhost:6011",
            Capabilities = new List<ManifestCapability> { new() { Name = "dual-cap" } }
        });
        _registry.UpdateCostHint("fast-peer", 0);
        _registry.UpdateTrustLevel("fast-peer", "verified");

        // Record health: fast-peer better
        for (var i = 0; i < 9; i++) _healthTracker.RecordSuccess("fast-peer");
        for (var i = 0; i < 2; i++) _healthTracker.RecordFailure("slow-peer");

        var result = await _router.RouteAsync("dual-cap");

        Assert.Equal(2, result.Count);
        Assert.True(result[0].CompositeScore >= result[1].CompositeScore,
            $"Expected first score >= second: {result[0].CompositeScore:F4} vs {result[1].CompositeScore:F4}");
    }

    [Fact]
    public async Task RouteAsync_RespectsMaxPeerCandidates()
    {
        var limitedOptions = new MeshRouterOptions
        {
            Enabled = true,
            MaxPeerCandidates = 2,
            Weights = _options.Weights
        };
        var limitedRouter = new CapabilityMeshRouter(
            _registry, _breaker, _trustPolicy, _healthTracker,
            limitedOptions, NullLogger<CapabilityMeshRouter>.Instance);

        // Register 5 peers
        for (var i = 0; i < 5; i++)
        {
            _registry.Register(new AgentManifest
            {
                AgentId = $"peer-{i}",
                DisplayName = $"Peer {i}",
                Endpoint = $"http://localhost:{6100 + i}",
                Capabilities = new List<ManifestCapability> { new() { Name = "limited-cap" } }
            });
            _registry.UpdateCostHint($"peer-{i}", 0);
            _registry.UpdateTrustLevel($"peer-{i}", "verified");
        }

        var result = await limitedRouter.RouteAsync("limited-cap");

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task RouteAsync_FallbackToPhrase_WhenCapabilityNotFound()
    {
        _registry.Register(new AgentManifest
        {
            AgentId = "phrase-peer",
            DisplayName = "Phrase Peer",
            Endpoint = "http://localhost:6200",
            Capabilities = new List<ManifestCapability>
            {
                new() { Name = "other-cap", PhraseReceivers = new List<string> { "hello world" } }
            }
        });
        _registry.UpdateCostHint("phrase-peer", 0);
        _registry.UpdateTrustLevel("phrase-peer", "verified");

        var result = await _router.RouteAsync("hello world");

        Assert.Single(result);
        Assert.Equal("phrase-peer", result[0].AgentId);
    }
}
