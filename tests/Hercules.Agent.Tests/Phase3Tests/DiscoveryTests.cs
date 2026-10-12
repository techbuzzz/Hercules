using Hercules.Config;
using Hercules.Mesh;
using Hercules.Mesh.Discovery;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Phase3Tests;

public class DiscoveryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CapabilityRegistry _registry;
    private readonly Mock<ILogger<StaticDiscoverySource>> _staticLogger = new();
    private readonly Mock<ILogger<RegistryDiscoverySource>> _registryLogger = new();
    private readonly Mock<ILogger<MdnsDiscoverySource>> _mdnsLogger = new();
    private readonly Mock<ILogger<DiscoveryService>> _serviceLogger = new();
    private readonly Mock<ILogger<NoopMdnsClient>> _noopMdnsLogger = new();

    public DiscoveryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-disc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _registry = new CapabilityRegistry(Path.Combine(_tempDir, "registry.db"));
    }

    public void Dispose()
    {
        _registry.Dispose();
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    // === StaticDiscoverySource tests ===

    [Fact]
    public void StaticDiscoverySource_IsEnabled_FalseWhenNoPeers()
    {
        var meshCfg = new MeshConfig { Peers = new List<MeshPeerConfig>() };
        using var http = new HttpClient();
        var src = new StaticDiscoverySource(meshCfg, http, "self", _staticLogger.Object);

        Assert.False(src.IsEnabled);
    }

    [Fact]
    public void StaticDiscoverySource_IsEnabled_TrueWhenPeersConfigured()
    {
        var meshCfg = new MeshConfig
        {
            Peers = new List<MeshPeerConfig> { new() { AgentId = "peer1", Endpoint = "http://peer1:8421" } }
        };
        using var http = new HttpClient();
        var src = new StaticDiscoverySource(meshCfg, http, "self", _staticLogger.Object);

        Assert.True(src.IsEnabled);
    }

    [Fact]
    public async Task StaticDiscoverySource_SkipsSelfAgent()
    {
        var meshCfg = new MeshConfig
        {
            Peers = new List<MeshPeerConfig>
            {
                new() { AgentId = "self", Endpoint = "http://self:8421" },
                new() { AgentId = "peer1", Endpoint = "http://peer1:8421" }
            }
        };
        using var http = new HttpClient();
        var src = new StaticDiscoverySource(meshCfg, http, "self", _staticLogger.Object);

        DiscoveryResult result = await src.DiscoverAsync();

        Assert.Single(result.Agents);
        Assert.Equal("peer1", result.Agents[0].AgentId);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task StaticDiscoverySource_SkipsEmptyPeers()
    {
        var meshCfg = new MeshConfig
        {
            Peers = new List<MeshPeerConfig>
            {
                new() { AgentId = "", Endpoint = "" },
                new() { AgentId = "peer1", Endpoint = "http://peer1:8421" }
            }
        };
        using var http = new HttpClient();
        var src = new StaticDiscoverySource(meshCfg, http, "self", _staticLogger.Object);

        DiscoveryResult result = await src.DiscoverAsync();

        Assert.Single(result.Agents);
    }

    [Fact]
    public async Task StaticDiscoverySource_SetsManifestUrl()
    {
        var meshCfg = new MeshConfig
        {
            Peers = new List<MeshPeerConfig>
            {
                new() { AgentId = "peer1", Endpoint = "http://peer1:8421" }
            }
        };
        using var http = new HttpClient();
        var src = new StaticDiscoverySource(meshCfg, http, "self", _staticLogger.Object);

        DiscoveryResult result = await src.DiscoverAsync();

        Assert.Single(result.Agents);
        Assert.Equal("http://peer1:8421/agent.manifest.json", result.Agents[0].ManifestUrl);
    }

    // === RegistryDiscoverySource tests ===

    [Fact]
    public async Task RegistryDiscoverySource_ReturnsOnlyNonSelfAgents()
    {
        _registry.Register(CreateManifest("self", "http://self:8421", new List<string> { "self-phrase" }));
        _registry.Register(CreateManifest("peer1", "http://peer1:8421", new List<string> { "peer1-phrase" }));
        _registry.Register(CreateManifest("peer2", "http://peer2:8421", new List<string> { "peer2-phrase" }));

        var src = new RegistryDiscoverySource(_registry, "self", _registryLogger.Object);

        DiscoveryResult result = await src.DiscoverAsync();

        Assert.Equal(2, result.Agents.Count);
        Assert.All(result.Agents, a => Assert.NotEqual("self", a.AgentId));
        Assert.True(result.Success);
    }

    [Fact]
    public async Task RegistryDiscoverySource_IsAlwaysEnabled()
    {
        var src = new RegistryDiscoverySource(_registry, "self", _registryLogger.Object);
        Assert.True(src.IsEnabled);
    }

    [Fact]
    public async Task RegistryDiscoverySource_ReturnsCorrectProperties()
    {
        _registry.Register(CreateManifest("peer1", "http://peer1:8421", new List<string> { "phrase" }));

        var src = new RegistryDiscoverySource(_registry, "self", _registryLogger.Object);
        DiscoveryResult result = await src.DiscoverAsync();

        DiscoveredAgent agent = result.Agents.Single();
        Assert.Equal("peer1", agent.AgentId);
        Assert.Equal("http://peer1:8421", agent.Endpoint);
        Assert.Equal(DiscoverySourceKind.Registry, agent.Source);
    }

    // === MdnsDiscoverySource tests ===

    [Fact]
    public void MdnsDiscoverySource_IsDisabledByDefault()
    {
        var config = new DiscoveryConfig { EnableMdns = false };
        var mdns = new NoopMdnsClient(_noopMdnsLogger.Object);
        var src = new MdnsDiscoverySource(mdns, config, _mdnsLogger.Object);

        Assert.False(src.IsEnabled);
    }

    [Fact]
    public void MdnsDiscoverySource_IsEnabledWhenConfigured()
    {
        var config = new DiscoveryConfig { EnableMdns = true };
        var mdns = new NoopMdnsClient(_noopMdnsLogger.Object);
        var src = new MdnsDiscoverySource(mdns, config, _mdnsLogger.Object);

        Assert.True(src.IsEnabled);
    }

    [Fact]
    public async Task MdnsDiscoverySource_ReturnsEmptyWhenDisabled()
    {
        var config = new DiscoveryConfig { EnableMdns = false };
        var mdns = new NoopMdnsClient(_noopMdnsLogger.Object);
        var src = new MdnsDiscoverySource(mdns, config, _mdnsLogger.Object);

        DiscoveryResult result = await src.DiscoverAsync();

        Assert.Empty(result.Agents);
        Assert.True(result.Success);
    }

    // === DiscoveryService tests ===

    [Fact]
    public async Task DiscoveryService_ReturnsEmptyWhenNoSourcesEnabled()
    {
        var config = new DiscoveryConfig { EnableMdns = false };
        var sources = new List<IDiscoverySource>
        {
            new MdnsDiscoverySource(new NoopMdnsClient(_noopMdnsLogger.Object), config, _mdnsLogger.Object)
        };
        var svc = new DiscoveryService(sources, config, _serviceLogger.Object);

        IReadOnlyList<DiscoveredAgent> agents = await svc.GetAgentsAsync();

        Assert.Empty(agents);
    }

    [Fact]
    public async Task DiscoveryService_DeduplicatesAgentsById()
    {
        var config = new DiscoveryConfig { EnableMdns = false };
        var peer1Static = new DiscoveredAgent
        {
            AgentId = "peer1",
            DisplayName = "Peer 1 (static)",
            Source = DiscoverySourceKind.Static,
            ManifestLoaded = true
        };
        var peer1Registry = new DiscoveredAgent
        {
            AgentId = "peer1",
            DisplayName = "Peer 1 (registry)",
            Source = DiscoverySourceKind.Registry,
            ManifestLoaded = false
        };
        var mockSrc = new Mock<IDiscoverySource>();
        mockSrc.Setup(s => s.Source).Returns(DiscoverySourceKind.Static);
        mockSrc.Setup(s => s.Name).Returns("Mock");
        mockSrc.Setup(s => s.IsEnabled).Returns(true);
        mockSrc.Setup(s => s.DiscoverAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DiscoveryResult
            {
                Source = DiscoverySourceKind.Static,
                SourceName = "Mock",
                Success = true,
                Agents = new List<DiscoveredAgent> { peer1Static, peer1Registry }
            });

        var svc = new DiscoveryService(new[] { mockSrc.Object }, config, _serviceLogger.Object);
        IReadOnlyList<DiscoveredAgent> agents = await svc.GetAgentsAsync();

        Assert.Single(agents);
        Assert.Equal("peer1", agents[0].AgentId);
        // Should prefer the one with manifest loaded
        Assert.True(agents[0].ManifestLoaded);
    }

    [Fact]
    public async Task DiscoveryService_CachesResults()
    {
        var config = new DiscoveryConfig { EnableMdns = false, CacheTtlSeconds = 300 };
        var callCount = 0;
        var mockSrc = new Mock<IDiscoverySource>();
        mockSrc.Setup(s => s.Source).Returns(DiscoverySourceKind.Static);
        mockSrc.Setup(s => s.Name).Returns("Mock");
        mockSrc.Setup(s => s.IsEnabled).Returns(true);
        mockSrc.Setup(s => s.DiscoverAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return new DiscoveryResult
                {
                    Source = DiscoverySourceKind.Static,
                    SourceName = "Mock",
                    Success = true,
                    Agents = new List<DiscoveredAgent>
                    {
                        new() { AgentId = $"peer{callCount}", DisplayName = $"P{callCount}", Source = DiscoverySourceKind.Static }
                    }
                };
            });

        var svc = new DiscoveryService(new[] { mockSrc.Object }, config, _serviceLogger.Object);

        IReadOnlyList<DiscoveredAgent> agents1 = await svc.GetAgentsAsync();
        IReadOnlyList<DiscoveredAgent> agents2 = await svc.GetAgentsAsync();

        Assert.Single(agents1);
        Assert.Single(agents2);
        Assert.Equal(1, callCount); // cached — no second call
        Assert.True(svc.IsCacheFresh);
    }

    [Fact]
    public async Task DiscoveryService_RefreshBypassesCache()
    {
        var config = new DiscoveryConfig { EnableMdns = false, CacheTtlSeconds = 300 };
        var callCount = 0;
        var mockSrc = new Mock<IDiscoverySource>();
        mockSrc.Setup(s => s.Source).Returns(DiscoverySourceKind.Static);
        mockSrc.Setup(s => s.Name).Returns("Mock");
        mockSrc.Setup(s => s.IsEnabled).Returns(true);
        mockSrc.Setup(s => s.DiscoverAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return new DiscoveryResult
                {
                    Source = DiscoverySourceKind.Static,
                    SourceName = "Mock",
                    Success = true,
                    Agents = new List<DiscoveredAgent>
                    {
                        new() { AgentId = $"peer{callCount}", DisplayName = $"P{callCount}", Source = DiscoverySourceKind.Static }
                    }
                };
            });

        var svc = new DiscoveryService(new[] { mockSrc.Object }, config, _serviceLogger.Object);

        await svc.GetAgentsAsync(); // warm up cache
        await svc.RefreshAsync();    // force refresh

        Assert.Equal(2, callCount);
    }

    [Fact]
    public void DiscoveryService_GetSourceStatuses_ReturnsAllSources()
    {
        var config = new DiscoveryConfig { EnableMdns = false };
        var staticSrc = new StaticDiscoverySource(
            new MeshConfig { Peers = new List<MeshPeerConfig> { new() { AgentId = "peer1", Endpoint = "http://p1:8421" } } },
            new HttpClient(), "self", _staticLogger.Object);
        var registrySrc = new RegistryDiscoverySource(_registry, "self", _registryLogger.Object);
        var mdnsSrc = new MdnsDiscoverySource(
            new NoopMdnsClient(_noopMdnsLogger.Object), config, _mdnsLogger.Object);

        var svc = new DiscoveryService(
            new IDiscoverySource[] { staticSrc, registrySrc, mdnsSrc },
            config, _serviceLogger.Object);

        IReadOnlyList<SourceStatus> statuses = svc.GetSourceStatuses();

        Assert.Equal(3, statuses.Count);
        Assert.Equal(DiscoverySourceKind.Static, statuses[0].Source);
        Assert.True(statuses[0].Enabled); // Has peers configured
        Assert.Equal(DiscoverySourceKind.Registry, statuses[1].Source);
        Assert.True(statuses[1].Enabled);
        Assert.Equal(DiscoverySourceKind.Mdns, statuses[2].Source);
        Assert.False(statuses[2].Enabled); // EnableMdns = false
    }

    [Fact]
    public async Task DiscoveryService_GetAgentsBySource_ReturnsOnlyThatSource()
    {
        var config = new DiscoveryConfig { EnableMdns = false };
        var mockSrc = new Mock<IDiscoverySource>();
        mockSrc.Setup(s => s.Source).Returns(DiscoverySourceKind.Static);
        mockSrc.Setup(s => s.Name).Returns("Mock");
        mockSrc.Setup(s => s.IsEnabled).Returns(true);
        mockSrc.Setup(s => s.DiscoverAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DiscoveryResult
            {
                Source = DiscoverySourceKind.Static,
                SourceName = "Mock",
                Success = true,
                Agents = new List<DiscoveredAgent>
                {
                    new() { AgentId = "p1", DisplayName = "P1", Source = DiscoverySourceKind.Static }
                }
            });

        var svc = new DiscoveryService(new[] { mockSrc.Object }, config, _serviceLogger.Object);
        IReadOnlyList<DiscoveredAgent> agents = await svc.GetAgentsBySourceAsync(DiscoverySourceKind.Static);

        Assert.Single(agents);
        Assert.Equal("p1", agents[0].AgentId);
    }

    [Fact]
    public async Task DiscoveryService_ReturnsEmptyForUnknownSource()
    {
        var config = new DiscoveryConfig { EnableMdns = false };
        var svc = new DiscoveryService(Array.Empty<IDiscoverySource>(), config, _serviceLogger.Object);
        IReadOnlyList<DiscoveredAgent> agents = await svc.GetAgentsBySourceAsync(DiscoverySourceKind.Mdns);
        Assert.Empty(agents);
    }

    private static AgentManifest CreateManifest(string agentId, string endpoint, List<string> phrases)
    {
        return new AgentManifest
        {
            AgentId = agentId,
            DisplayName = agentId,
            Description = $"Agent {agentId}",
            Endpoint = endpoint,
            Capabilities = new List<ManifestCapability>
            {
                new() { Name = $"{agentId}-cap", Description = "Test capability", PhraseReceivers = phrases }
            }
        };
    }
}
