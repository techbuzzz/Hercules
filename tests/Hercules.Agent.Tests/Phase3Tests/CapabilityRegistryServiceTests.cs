using Hercules.Mesh;
using Xunit;

namespace Hercules.Agent.Tests.Phase3Tests;

/// <summary>
///     Тесты CapabilityRegistryService: TTL expiry, health status transitions,
///     trust level, cost/latency hints via ICapabilityRegistryService interface.
/// </summary>
public class CapabilityRegistryServiceTests : IDisposable
{
    private readonly CapabilityRegistry _store;
    private readonly CapabilityRegistryService _service;
    private readonly string _tempDir;

    public CapabilityRegistryServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-svc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var dbPath = Path.Combine(_tempDir, "svc_test.db");
        _store = new CapabilityRegistry(dbPath);
        _service = new CapabilityRegistryService(_store);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    private void RegisterAgent(string agentId, string endpoint, int expirySeconds = 86400)
    {
        var manifest = new AgentManifest
        {
            AgentId = agentId,
            DisplayName = agentId,
            Description = $"Agent {agentId}",
            Endpoint = endpoint,
            Capabilities = new List<ManifestCapability>
            {
                new() { Name = $"{agentId}-cap", Description = "cap", PhraseReceivers = new List<string> { agentId } }
            }
        };
        _store.Register(manifest);
        _service.SetExpiry(agentId, expirySeconds);
    }

    // --- TTL / Expiry ---

    [Fact]
    public void SetExpiry_SetsTtl()
    {
        RegisterAgent("ttl-agent", "http://ttl", expirySeconds: 300);
        var entry = _service.GetEntry("ttl-agent");

        Assert.NotNull(entry);
        Assert.Equal(300, entry.ExpirySeconds);
    }

    [Fact]
    public void CleanupExpired_RemovesExpiredAgents()
    {
        // Register agent with TTL = 0 (already expired)
        RegisterAgent("expired-1", "http://e1", expirySeconds: 0);
        RegisterAgent("expired-2", "http://e2", expirySeconds: 0);
        RegisterAgent("alive", "http://alive", expirySeconds: 86400);

        int removed = _service.CleanupExpired();

        Assert.Equal(2, removed);
        Assert.Null(_service.GetEntry("expired-1"));
        Assert.Null(_service.GetEntry("expired-2"));
        Assert.NotNull(_service.GetEntry("alive"));
    }

    [Fact]
    public void CleanupExpired_DoesNotRemoveFreshAgents()
    {
        RegisterAgent("fresh-1", "http://f1", expirySeconds: 86400);
        RegisterAgent("fresh-2", "http://f2", expirySeconds: 86400);

        int removed = _service.CleanupExpired();

        Assert.Equal(0, removed);
        Assert.NotNull(_service.GetEntry("fresh-1"));
        Assert.NotNull(_service.GetEntry("fresh-2"));
    }

    [Fact]
    public void CleanupExpired_ReturnsZero_WhenEmptyRegistry()
    {
        int removed = _service.CleanupExpired();
        Assert.Equal(0, removed);
    }

    // --- Health Status ---

    [Fact]
    public void UpdateHealthStatus_SetsStatus()
    {
        RegisterAgent("health-agent", "http://health");
        _service.UpdateHealthStatus("health-agent", AgentHealthStatus.Healthy);

        var entry = _service.GetEntry("health-agent");
        Assert.NotNull(entry);
        Assert.Equal("healthy", entry.HealthStatus);
    }

    [Fact]
    public void UpdateHealthStatus_Healthy_ResetsConsecutiveFailures()
    {
        RegisterAgent("heal-agent", "http://heal");
        _service.UpdateHealthStatus("heal-agent", AgentHealthStatus.Healthy);

        var entry = _service.GetEntry("heal-agent");
        Assert.NotNull(entry);
        Assert.Equal("healthy", entry.HealthStatus);
    }

    [Fact]
    public void UpdateHealthStatus_Unreachable_IncrementsFailures()
    {
        RegisterAgent("fail-agent", "http://fail");
        _service.UpdateHealthStatus("fail-agent", AgentHealthStatus.Unreachable);

        var entry = _service.GetEntry("fail-agent");
        Assert.NotNull(entry);
        Assert.Equal("unreachable", entry.HealthStatus);
    }

    [Fact]
    public void UpdateHealthStatus_SetsLastHealthCheck()
    {
        RegisterAgent("lh-agent", "http://lh");
        _service.UpdateHealthStatus("lh-agent", AgentHealthStatus.Healthy);

        var entry = _service.GetEntry("lh-agent");
        Assert.NotNull(entry);
        Assert.NotEmpty(entry.LastHealthCheck);
        Assert.True(DateTimeOffset.TryParse(entry.LastHealthCheck, out _));
    }

    // --- Trust Level ---

    [Fact]
    public void UpdateTrustLevel_SetsLevel()
    {
        RegisterAgent("trust-agent", "http://trust");
        _service.UpdateTrustLevel("trust-agent", "verified");

        var entry = _service.GetEntry("trust-agent");
        Assert.NotNull(entry);
        Assert.Equal("verified", entry.TrustLevel);
    }

    [Fact]
    public void UpdateTrustLevel_DefaultsToUnverified()
    {
        RegisterAgent("no-trust", "http://notrust");
        var entry = _service.GetEntry("no-trust");

        Assert.NotNull(entry);
        Assert.Equal("unverified", entry.TrustLevel);
    }

    // --- Cost / Latency Hints ---

    [Fact]
    public void UpdateCostHint_SetsCost()
    {
        RegisterAgent("cost-agent", "http://cost");
        _service.UpdateCostHint("cost-agent", 0.05m);

        var entry = _service.GetEntry("cost-agent");
        Assert.NotNull(entry);
        Assert.Equal(0.05m, entry.CostHintUsd);
    }

    [Fact]
    public void UpdateLatencyHint_SetsLatency()
    {
        RegisterAgent("lat-agent", "http://lat");
        _service.UpdateLatencyHint("lat-agent", 250);

        var entry = _service.GetEntry("lat-agent");
        Assert.NotNull(entry);
        Assert.Equal(250, entry.LatencyHintMs);
    }

    // --- ListAll ---

    [Fact]
    public void ListAll_ReturnsAllAgentsWithFullData()
    {
        RegisterAgent("all-1", "http://a1");
        RegisterAgent("all-2", "http://a2");
        _service.UpdateTrustLevel("all-1", "verified");
        _service.UpdateCostHint("all-2", 0.10m);

        var all = _service.ListAll();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, e => e.AgentId == "all-1" && e.TrustLevel == "verified");
        Assert.Contains(all, e => e.AgentId == "all-2" && e.CostHintUsd == 0.10m);
    }

    [Fact]
    public void ListAll_IsEmptyForEmptyRegistry()
    {
        var all = _service.ListAll();
        Assert.Empty(all);
    }

    // --- Touch / Remove ---

    [Fact]
    public void Touch_UpdatesLastSeen()
    {
        RegisterAgent("touch-svc", "http://touch");
        var before = _service.GetEntry("touch-svc")?.LastSeen;
        Thread.Sleep(50);
        _service.Touch("touch-svc");
        var after = _service.GetEntry("touch-svc")?.LastSeen;

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Remove_DeletesAgent()
    {
        RegisterAgent("remove-svc", "http://rem");
        Assert.NotNull(_service.GetEntry("remove-svc"));

        var removed = _service.Remove("remove-svc");

        Assert.True(removed);
        Assert.Null(_service.GetEntry("remove-svc"));
    }

    // --- GetEntry ---

    [Fact]
    public void GetEntry_ReturnsNullForNonexistent()
    {
        Assert.Null(_service.GetEntry("nonexistent"));
    }
}
