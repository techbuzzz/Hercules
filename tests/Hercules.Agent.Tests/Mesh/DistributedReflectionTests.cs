using Hercules.Config;
using Hercules.LLM;
using Hercules.Mesh;
using Hercules.Mesh.Router;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Mesh;

public class DistributedReflectionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<ILLMClient> _llmMock;
    private readonly Mock<ILogger<DistributedReflection>> _loggerMock;
    private readonly Mock<ILogger<ReflectionProposalStore>> _storeLoggerMock;
    private readonly ReflectionProposalConfig _config;
    private readonly ReflectionProposalStore _store;
    private readonly DistributedReflection _refl;

    public DistributedReflectionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"refl_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _llmMock = new Mock<ILLMClient>();
        _loggerMock = new Mock<ILogger<DistributedReflection>>();
        _storeLoggerMock = new Mock<ILogger<ReflectionProposalStore>>();
        _config = new ReflectionProposalConfig
        {
            GenerateProposals = true,
            EnableLLMAnalysis = false, // disable LLM in unit tests
            LowHealthThreshold = 0.5
        };

        _store = new ReflectionProposalStore(_tempDir, _storeLoggerMock.Object);
        // Use in-memory SQLite for the default reflection instance to avoid file locks
        _refl = new DistributedReflection(
            _llmMock.Object,
            CapabilityRegistry.CreateInMemory(),
            new CircuitBreaker(),
            new RouterHealthTracker(),
            _store,
            _config,
            _loggerMock.Object,
            auditService: null);
    }

    private DistributedReflection CreateReflection(
        CapabilityRegistry? registry = null,
        CircuitBreaker? cb = null,
        RouterHealthTracker? health = null)
    {
        // Use in-memory SQLite for the default registry to avoid file-lock issues in tests
        var reg = registry ?? CapabilityRegistry.CreateInMemory();
        var breaker = cb ?? new CircuitBreaker();
        var tracker = health ?? new RouterHealthTracker();

        return new DistributedReflection(
            _llmMock.Object,
            reg,
            breaker,
            tracker,
            _store,
            _config,
            _loggerMock.Object,
            auditService: null);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task ReflectAsync_NoPeers_ReturnsEmptyMetricsAndNoProposals()
    {
        var result = await _refl.ReflectAsync();

        Assert.NotNull(result);
        Assert.Empty(result.PeerMetrics);
        Assert.Empty(result.FailurePatterns);
        Assert.Empty(result.RoutingPatterns);
        Assert.Empty(result.Proposals);
        Assert.Contains("No peer agents known", result.Markdown);
    }

    [Fact]
    public void CircuitBreaker_AfterFourFailures_StateIsOpen()
    {
        var cb = new CircuitBreaker { FailureThreshold = 3, Cooldown = TimeSpan.FromSeconds(60) };
        for (int i = 0; i < 4; i++) cb.RecordFailure("test-peer");
        var states = cb.GetAllStates();
        Assert.True(states.Count > 0, $"Expected states but got: {string.Join(", ", states.Select(kv => $"{kv.Key}={kv.Value}"))}");
        Assert.Equal(CircuitState.Open, states["test-peer"]);
    }

    [Fact]
    public void CollectFailurePatterns_CircuitOpen_ReturnsCircuitOpenPattern()
    {
        // Arrange: create a registry with a peer, break its circuit
        var reg = CapabilityRegistry.CreateInMemory();
        var cb = new CircuitBreaker { FailureThreshold = 3 };

        reg.Register(new AgentManifest
        {
            AgentId = "peer-alpha",
            DisplayName = "Peer Alpha",
            Description = "Test peer",
            Endpoint = "http://localhost:7000",
            Capabilities = new List<ManifestCapability>
            {
                new() { Name = "code-review", Description = "Reviews code changes" }
            }
        });

        for (int i = 0; i < cb.FailureThreshold + 1; i++)
        {
            cb.RecordFailure("peer-alpha");
        }

        // Debug: verify CB is open
        var allStates = cb.GetAllStates();
        Assert.True(allStates.Count > 0, $"CB states count: {allStates.Count}. All states: {string.Join(", ", allStates.Select(kv => $"{kv.Key}={kv.Value}"))}");

        var refl = CreateReflection(registry: reg, cb: cb);

        // Debug: check what GetLocalSkillRecommendations returns vs direct CB state
        var recsViaMethod = refl.GetLocalSkillRecommendations();
        var cbDirectStates = cb.GetAllStates();
        Assert.True(recsViaMethod.Count > 0, $"GetLocalSkillRecommendations returned [{recsViaMethod.Count}] recs. CB states: {string.Join(", ", cbDirectStates.Select(kv => $"{kv.Key}={kv.Value}"))}");

        // Act
        var patterns = refl.CollectFailurePatterns();

        // Debug: check what's in patterns
        Assert.True(patterns.Count > 0, $"patterns has {patterns.Count} items");
        Assert.Contains(patterns, fp => fp.FailureType == "circuit_open" && fp.PeerAgentId == "peer-alpha");
    }

    [Fact]
    public async Task ReflectAsync_CircuitOpen_GeneratesNewSkillProposal()
    {
        // Arrange: create a registry with a peer, break its circuit
        var reg = CapabilityRegistry.CreateInMemory();
        var cb = new CircuitBreaker { FailureThreshold = 3 };

        // Register a peer agent via AgentManifest
        reg.Register(new AgentManifest
        {
            AgentId = "peer-alpha",
            DisplayName = "Peer Alpha",
            Description = "Test peer",
            Endpoint = "http://localhost:7000",
            Capabilities = new List<ManifestCapability>
            {
                new() { Name = "code-review", Description = "Reviews code changes" }
            }
        });

        // Open the circuit: record failures >= threshold to trip CB
        for (int i = 0; i < cb.FailureThreshold + 1; i++)
        {
            cb.RecordFailure("peer-alpha");
        }

        var tracker = new RouterHealthTracker();
        var refl = CreateReflection(registry: reg, cb: cb, health: tracker);

        // Act
        var result = await refl.ReflectAsync();

        // Assert: circuit-open peer should generate a NewSkill proposal
        Assert.NotEmpty(result.FailurePatterns);
        Assert.Contains(result.FailurePatterns, fp => fp.FailureType == "circuit_open");
        Assert.NotEmpty(result.Proposals);
        Assert.Contains(result.Proposals, p =>
            p.Type == ProposalType.NewSkill &&
            p.Target == "code-review" &&
            p.Status == ProposalStatus.Pending);
    }

    [Fact]
    public async Task ReflectAsync_LowHealthPeer_GeneratesTrustUpdateProposal()
    {
        var reg = CapabilityRegistry.CreateInMemory();
        var cb = new CircuitBreaker();
        var tracker = new RouterHealthTracker();

        reg.Register(new AgentManifest
        {
            AgentId = "slow-peer",
            DisplayName = "Slow Peer",
            Description = "Peer with degraded health",
            Endpoint = "http://localhost:7001",
            Capabilities = new List<ManifestCapability>()
        });

        // Record failures to degrade health score below threshold (default = 0.5)
        for (int i = 0; i < 10; i++)
        {
            tracker.RecordFailure("slow-peer");
        }

        var refl = CreateReflection(registry: reg, cb: cb, health: tracker);
        var result = await refl.ReflectAsync();

        // Assert: low health should generate a TrustUpdate proposal
        Assert.NotEmpty(result.Proposals);
        Assert.Contains(result.Proposals, p =>
            p.Type == ProposalType.TrustUpdate &&
            p.Target == "slow-peer");
    }

    [Fact]
    public async Task ReflectAsync_ProposalsAreSavedAndLoadable()
    {
        var reg = CapabilityRegistry.CreateInMemory();
        var cb = new CircuitBreaker { FailureThreshold = 3 };
        for (int i = 0; i < cb.FailureThreshold + 1; i++) cb.RecordFailure("p1");
        reg.Register(new AgentManifest
        {
            AgentId = "p1",
            DisplayName = "P1",
            Description = "",
            Endpoint = "",
            Capabilities = new List<ManifestCapability>
            {
                new() { Name = "data-analysis", Description = "Analyzes data" }
            }
        });

        var refl = CreateReflection(registry: reg, cb: cb, health: new RouterHealthTracker());
        var r2 = await refl.ReflectAsync();

        Assert.NotEmpty(r2.Proposals);
        var pending = refl.GetPendingProposals();
        Assert.NotEmpty(pending);
    }

    [Fact]
    public void Approve_ExistingProposal_ChangesStatusToApproved()
    {
        var proposal = new ReflectionProposal
        {
            Type = ProposalType.NewSkill,
            Title = "Test",
            Rationale = "Test rationale",
            Content = "",
            Target = "test-cap",
            Priority = ProposalPriority.High,
            Trigger = "test",
            EvidenceJson = "{}",
            GeneratedBy = "unit-test"
        };
        _store.Save(proposal);

        _refl.Approve(proposal.Id, "operator@example.com", "Looks good");

        var loaded = _store.Get(proposal.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ProposalStatus.Approved, loaded.Status);
        Assert.Equal("operator@example.com", loaded.Reviewer);
        Assert.Equal("Looks good", loaded.ReviewComment);
        Assert.NotNull(loaded.ReviewedAt);
    }

    [Fact]
    public void Reject_ExistingProposal_ChangesStatusToRejected()
    {
        var proposal = new ReflectionProposal
        {
            Type = ProposalType.NewSkill,
            Title = "Reject me",
            Rationale = "Test",
            Content = "",
            Target = "cap",
            Priority = ProposalPriority.Low,
            Trigger = "test",
            EvidenceJson = "{}",
            GeneratedBy = "unit-test"
        };
        _store.Save(proposal);

        _refl.Reject(proposal.Id, "admin@example.com", "Not needed now");

        var loaded = _store.Get(proposal.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ProposalStatus.Rejected, loaded.Status);
    }

    [Fact]
    public void GetProposal_ValidId_ReturnsProposal()
    {
        var proposal = new ReflectionProposal
        {
            Type = ProposalType.RoutingRule,
            Title = "Routing test",
            Rationale = "Test",
            Content = "",
            Target = "intent-x",
            Priority = ProposalPriority.Medium,
            Trigger = "test",
            EvidenceJson = "{}",
            GeneratedBy = "unit-test"
        };
        _store.Save(proposal);

        var found = _refl.GetProposal(proposal.Id);

        Assert.NotNull(found);
        Assert.Equal(proposal.Id, found.Id);
    }

    [Fact]
    public void GetProposal_InvalidId_ReturnsNull()
    {
        var found = _refl.GetProposal("nonexistent-id");
        Assert.Null(found);
    }

    [Fact]
    public async Task ReflectAsync_MarkdownContainsSections()
    {
        var result = await _refl.ReflectAsync();

        Assert.Contains("## Peer Performance", result.Markdown);
        Assert.Contains("## Failure Patterns", result.Markdown);
        Assert.Contains("## Routing Patterns", result.Markdown);
        Assert.Contains("## Proposals", result.Markdown);
    }

    [Fact]
    public async Task ReflectAsync_GenerateProposalsDisabled_NoProposals()
    {
        var disabledConfig = new ReflectionProposalConfig
        {
            GenerateProposals = false,
            EnableLLMAnalysis = false
        };

        var disabledRefl = new DistributedReflection(
            _llmMock.Object,
            CapabilityRegistry.CreateInMemory(),
            new CircuitBreaker(),
            new RouterHealthTracker(),
            _store,
            disabledConfig,
            _loggerMock.Object,
            auditService: null);

        var result = await disabledRefl.ReflectAsync();

        Assert.Empty(result.Proposals);
    }

    [Fact]
    public async Task ReflectAsync_ResultHasBackwardCompatibleFields()
    {
        var result = await _refl.ReflectAsync();

        Assert.Equal(0, result.PeerCount);
        Assert.Equal(0, result.OpenCircuitCount);
        Assert.Equal(0, result.TotalCapabilities);
    }

    [Fact]
    public void GetLocalSkillRecommendations_CircuitOpen_ReturnsRecommendations()
    {
        var reg = CapabilityRegistry.CreateInMemory();
        var cb = new CircuitBreaker { FailureThreshold = 3 };

        reg.Register(new AgentManifest
        {
            AgentId = "unavailable-peer",
            DisplayName = "Unavailable",
            Description = "",
            Endpoint = "",
            Capabilities = new List<ManifestCapability>
            {
                new() { Name = "data-mining", Description = "Mining data" }
            }
        });

        for (int i = 0; i < cb.FailureThreshold + 1; i++) cb.RecordFailure("unavailable-peer");

        // Debug: verify CB is reporting Open
        var allStates = cb.GetAllStates();
        Assert.True(allStates.ContainsKey("unavailable-peer"), $"CB states: {string.Join(", ", allStates.Select(kv => $"{kv.Key}={kv.Value}"))}");

        var refl = CreateReflection(registry: reg, cb: cb);
        var recs = refl.GetLocalSkillRecommendations();

        Assert.True(recs.Count > 0, $"Recommendations were empty. CB states: {string.Join(", ", allStates.Select(kv => $"{kv.Key}={kv.Value}"))}");
        Assert.Contains(recs, r => r.Contains("data-mining"));
    }
}
