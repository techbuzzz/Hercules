using Hercules.Config;
using Hercules.Mesh.Profiles;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Mesh.Profiles;

public class MeshProfileLoaderTests
{
    private readonly Mock<ILogger<MeshProfileLoader>> _logMock;

    public MeshProfileLoaderTests()
    {
        _logMock = new Mock<ILogger<MeshProfileLoader>>();
    }

    [Fact]
    public void ListProfileNames_ContainsLocal()
    {
        var cfg = new MeshProfilesConfig();
        var loader = new MeshProfileLoader(cfg, _logMock.Object);
        var names = loader.ListProfileNames();
        Assert.Contains("local", names);
    }

    [Fact]
    public void GetProfile_Local_ReturnsLocalProfile()
    {
        var cfg = new MeshProfilesConfig();
        var loader = new MeshProfileLoader(cfg, _logMock.Object);
        var profile = loader.GetProfile("local");
        Assert.NotNull(profile);
        Assert.Equal("local", profile.Name);
        Assert.Equal(MeshBackendProfile.Local, profile.Profile);
    }

    [Fact]
    public void GetProfile_Unknown_ReturnsNull()
    {
        var cfg = new MeshProfilesConfig();
        var loader = new MeshProfileLoader(cfg, _logMock.Object);
        var profile = loader.GetProfile("nonexistent");
        Assert.Null(profile);
    }

    [Fact]
    public void GetActiveProfile_NoConfigSet_ReturnsLocal()
    {
        var cfg = new MeshProfilesConfig { ActiveProfile = null };
        var loader = new MeshProfileLoader(cfg, _logMock.Object);
        var profile = loader.GetActiveProfile();
        Assert.Equal("local", profile.Name);
    }

    [Fact]
    public void GetActiveProfile_ConfiguredName_ReturnsNamedProfile()
    {
        var cfg = new MeshProfilesConfig
        {
            Profiles = new Dictionary<string, MeshProfileDefinition>
            {
                ["redis-ha"] = new MeshProfileDefinition
                {
                    Name = "redis-ha",
                    Profile = MeshBackendProfile.Redis,
                    Backends = new Dictionary<string, MeshBackendConfig>
                    {
                        ["bus"] = new() { Kind = "redis", Enabled = true }
                    }
                }
            },
            ActiveProfile = "redis-ha"
        };
        var loader = new MeshProfileLoader(cfg, _logMock.Object);
        var profile = loader.GetActiveProfile();
        Assert.Equal("redis-ha", profile.Name);
        Assert.Equal(MeshBackendProfile.Redis, profile.Profile);
    }

    [Fact]
    public void GetActiveProfile_UnknownActive_DefaultsToLocal()
    {
        var cfg = new MeshProfilesConfig { ActiveProfile = "unknown" };
        var loader = new MeshProfileLoader(cfg, _logMock.Object);
        var profile = loader.GetActiveProfile();
        Assert.Equal("local", profile.Name);
    }

    [Fact]
    public void GetEffectiveBackend_EnabledBackend_ReturnsIt()
    {
        var cfg = new MeshProfilesConfig
        {
            Profiles = new Dictionary<string, MeshProfileDefinition>
            {
                ["test"] = new MeshProfileDefinition
                {
                    Name = "test",
                    Backends = new Dictionary<string, MeshBackendConfig>
                    {
                        ["bus"] = new() { Kind = "redis", Enabled = true, ConnectionString = "localhost:6379" }
                    }
                }
            },
            ActiveProfile = "test"
        };
        var loader = new MeshProfileLoader(cfg, _logMock.Object);
        var backend = loader.GetEffectiveBackend("test", "bus");
        Assert.Equal("redis", backend.Kind);
        Assert.True(backend.Enabled);
        Assert.Equal("localhost:6379", backend.ConnectionString);
    }

    [Fact]
    public void GetEffectiveBackend_DisabledBackend_ReturnsInProcess()
    {
        var cfg = new MeshProfilesConfig
        {
            Profiles = new Dictionary<string, MeshProfileDefinition>
            {
                ["test"] = new MeshProfileDefinition
                {
                    Name = "test",
                    Backends = new Dictionary<string, MeshBackendConfig>
                    {
                        ["bus"] = new() { Kind = "redis", Enabled = false }
                    }
                }
            },
            ActiveProfile = "test"
        };
        var loader = new MeshProfileLoader(cfg, _logMock.Object);
        var backend = loader.GetEffectiveBackend("test", "bus");
        Assert.Equal("in-process", backend.Kind);
        Assert.False(backend.Enabled);
    }

    [Fact]
    public void GetEffectiveBackend_MissingRole_ReturnsInProcess()
    {
        var cfg = new MeshProfilesConfig
        {
            Profiles = new Dictionary<string, MeshProfileDefinition>
            {
                ["test"] = new MeshProfileDefinition
                {
                    Name = "test",
                    Backends = new Dictionary<string, MeshBackendConfig>()
                }
            },
            ActiveProfile = "test"
        };
        var loader = new MeshProfileLoader(cfg, _logMock.Object);
        var backend = loader.GetEffectiveBackend("test", "bus");
        Assert.Equal("in-process", backend.Kind);
        Assert.False(backend.Enabled);
    }

    [Fact]
    public void LocalProfile_HasAllThreeBackends()
    {
        var cfg = new MeshProfilesConfig();
        var loader = new MeshProfileLoader(cfg, _logMock.Object);
        var profile = loader.GetProfile("local")!;
        Assert.Contains("bus", profile.Backends.Keys);
        Assert.Contains("queue", profile.Backends.Keys);
        Assert.Contains("stateStore", profile.Backends.Keys);
        foreach (var backend in profile.Backends.Values)
        {
            Assert.Equal("in-process", backend.Kind);
        }
    }

    [Fact]
    public void ListProfileNames_WithConfigProfiles_IncludesAll()
    {
        var cfg = new MeshProfilesConfig
        {
            Profiles = new Dictionary<string, MeshProfileDefinition>
            {
                ["redis-ha"] = new() { Name = "redis-ha" },
                ["nats-cluster"] = new() { Name = "nats-cluster" }
            }
        };
        var loader = new MeshProfileLoader(cfg, _logMock.Object);
        var names = loader.ListProfileNames();
        Assert.Contains("local", names);
        Assert.Contains("redis-ha", names);
        Assert.Contains("nats-cluster", names);
        Assert.Equal(3, names.Count);
    }
}
