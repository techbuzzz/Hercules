using Hercules.Mesh;
using Xunit;

namespace Hercules.Agent.Tests.Phase3Tests;

public class AgentManifestTests : IDisposable
{
    private readonly string _tempDir;

    public AgentManifestTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-mani-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    private AgentManifestService CreateService(Func<List<ManifestCapability>>? capsProvider = null)
    {
        return new AgentManifestService(
            agentId: "hercules-test",
            displayName: "Test Agent",
            description: "Test agent for unit tests",
            endpoint: "http://localhost:5000",
            manifestDir: _tempDir,
            capabilitiesProvider: capsProvider ?? (() => new List<ManifestCapability>
            {
                new() { Name = "code-review", Description = "Code review", PhraseReceivers = ["review", "проверь код"] },
                new() { Name = "refactor", Description = "Refactoring", PhraseReceivers = ["refactor", "улучшить код"] }
            }),
            primaryModel: "yandexgpt",
            fallbackModels: ["ollama-local"]);
    }

    [Fact]
    public void Current_Returns_Manifest_With_Capabilities()
    {
        var svc = CreateService();
        var manifest = svc.Current;

        Assert.Equal("hercules-test", manifest.AgentId);
        Assert.Equal("Test Agent", manifest.DisplayName);
        Assert.Equal("http://localhost:5000", manifest.Endpoint);
        Assert.Equal(2, manifest.Capabilities.Count);
        Assert.Equal("code-review", manifest.Capabilities[0].Name);
    }

    [Fact]
    public void Save_Writes_Manifest_To_File()
    {
        var svc = CreateService();
        var manifest = svc.Save();

        Assert.True(File.Exists(svc.ManifestPath));
        var json = File.ReadAllText(svc.ManifestPath);
        Assert.Contains("hercules-test", json);
        Assert.Contains("code-review", json);
    }

    [Fact]
    public void Validate_Returns_No_Errors_For_Valid_Manifest()
    {
        var svc = CreateService();
        var errors = svc.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_Returns_Errors_For_Empty_Capabilities()
    {
        var svc = CreateService(() => new List<ManifestCapability>());
        var errors = svc.Validate();
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("Capabilities пуст"));
    }

    [Fact]
    public void UpdateEndpoint_Changes_Endpoint_And_Health()
    {
        var svc = CreateService();
        svc.UpdateEndpoint("http://192.168.1.100:5000");

        var manifest = svc.Current;
        Assert.Equal("http://192.168.1.100:5000", manifest.Endpoint);
        Assert.Equal("http://192.168.1.100:5000/api/health", manifest.Health);
    }

    [Fact]
    public void Capabilities_Updated_Dynamically_Through_Provider()
    {
        var count = 1;
        var svc = CreateService(() => Enumerable.Range(0, count)
            .Select(i => new ManifestCapability { Name = $"cap-{i}", Description = $"Cap {i}", PhraseReceivers = [$"trigger-{i}"] })
            .ToList());

        Assert.Single(svc.Current.Capabilities);
        count = 3;
        Assert.Equal(3, svc.Current.Capabilities.Count);
    }
}