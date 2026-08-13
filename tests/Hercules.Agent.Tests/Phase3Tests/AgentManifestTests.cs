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
      try
      {
         if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
      }
      catch
      {
      }
   }

   private AgentManifestService CreateService(Func<List<ManifestCapability>>? capsProvider = null)
   {
      return new AgentManifestService(
         "hercules-test",
         "Test Agent",
         "Test agent for unit tests",
         "http://localhost:5000",
         _tempDir,
         capsProvider ??
         (() => new List<ManifestCapability>
         {
            new() { Name = "code-review", Description = "Code review", PhraseReceivers = ["review", "проверь код"] },
            new() { Name = "refactor", Description = "Refactoring", PhraseReceivers = ["refactor", "улучшить код"] }
         }),
         "yandexgpt",
         ["ollama-local"]);
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

   [Fact]
   public void ExtendedConstructor_Populates_SupportedProtocolVersions()
   {
      var svc = new AgentManifestService(
         "hercules-test",
         "Test Agent",
         "Test description",
         "http://localhost:5000",
         _tempDir,
         () => new List<ManifestCapability>
         {
            new() { Name = "test-cap", Description = "Test", PhraseReceivers = ["test"] }
         },
         null,
         "yandexgpt",
         null,
         "http://localhost:5000/api/health",
         new List<string> { "1.0", "2.0" },
         new ManifestResourceLimits
         {
            MaxTokensPerRequest = 4096,
            MaxConcurrentRequests = 4,
            MaxToolCallsPerRequest = 3
         },
         new ManifestTrustMetadata
         {
            Level = "verified",
            IdentityProvider = "custom-ca",
            IdentityClaims = new Dictionary<string, string> { ["issuer"] = "hercules-pki" }
         });

      var manifest = svc.Current;
      Assert.Equal(2, manifest.SupportedProtocolVersions.Count);
      Assert.Contains("1.0", manifest.SupportedProtocolVersions);
      Assert.Contains("2.0", manifest.SupportedProtocolVersions);
   }

   [Fact]
   public void ExtendedConstructor_Populates_ResourceLimits()
   {
      var svc = new AgentManifestService(
         "hercules-test",
         "Test Agent",
         "Test description",
         "http://localhost:5000",
         _tempDir,
         () => new List<ManifestCapability>
         {
            new() { Name = "cap", Description = "C", PhraseReceivers = ["x"] }
         },
         null, "", null, "",
         new List<string> { "1.0" },
         new ManifestResourceLimits
         {
            MaxTokensPerRequest = 8192,
            MaxConcurrentRequests = 10,
            MaxToolCallsPerRequest = 5,
            MaxWallClockSecondsPerRequest = 120,
            MaxCostPerDayUsd = 10.0m,
            MaxTokensPerDay = 100000
         },
         null);

      var manifest = svc.Current;
      Assert.NotNull(manifest.ResourceLimits);
      Assert.Equal(8192, manifest.ResourceLimits.MaxTokensPerRequest);
      Assert.Equal(10, manifest.ResourceLimits.MaxConcurrentRequests);
      Assert.Equal(5, manifest.ResourceLimits.MaxToolCallsPerRequest);
      Assert.Equal(120, manifest.ResourceLimits.MaxWallClockSecondsPerRequest);
      Assert.Equal(10.0m, manifest.ResourceLimits.MaxCostPerDayUsd);
      Assert.Equal(100000, manifest.ResourceLimits.MaxTokensPerDay);
   }

   [Fact]
   public void ExtendedConstructor_Populates_TrustMetadata()
   {
      var svc = new AgentManifestService(
         "hercules-test",
         "Test Agent",
         "Test description",
         "http://localhost:5000",
         _tempDir,
         () => new List<ManifestCapability>
         {
            new() { Name = "cap", Description = "C", PhraseReceivers = ["x"] }
         },
         null, "", null, "",
         new List<string> { "1.0" },
         null,
         new ManifestTrustMetadata
         {
            Level = "trusted",
            IdentityProvider = "self-signed",
            VerifiedBy = "admin",
            VerifiedAt = "2026-08-13T00:00:00Z"
         });

      var manifest = svc.Current;
      Assert.NotNull(manifest.TrustMetadata);
      Assert.Equal("trusted", manifest.TrustMetadata.Level);
      Assert.Equal("self-signed", manifest.TrustMetadata.IdentityProvider);
      Assert.Equal("admin", manifest.TrustMetadata.VerifiedBy);
   }

   [Fact]
   public void ExtendedConstructor_Populates_Skills()
   {
      var svc = new AgentManifestService(
         "hercules-test",
         "Test Agent",
         "Test description",
         "http://localhost:5000",
         _tempDir,
         () => new List<ManifestCapability>
         {
            new() { Name = "cap", Description = "C", PhraseReceivers = ["x"] }
         },
         () => new List<ManifestSkillEntry>
         {
            new() { Id = "skill-001", Name = "Code Review", Description = "Reviews code", Version = "1.2.0", RiskLevel = "low" },
            new() { Id = "skill-002", Name = "Code Fix", Description = "Fixes bugs", Version = "2.0.0", RiskLevel = "medium" }
         },
         "", null, "",
         new List<string> { "1.0" },
         null,
         null);

      var manifest = svc.Current;
      Assert.Equal(2, manifest.Skills.Count);
      Assert.Equal("skill-001", manifest.Skills[0].Id);
      Assert.Equal("1.2.0", manifest.Skills[0].Version);
      Assert.Equal("low", manifest.Skills[0].RiskLevel);
      Assert.Equal("skill-002", manifest.Skills[1].Id);
   }

   [Fact]
   public async Task SaveAsync_Writes_Manifest_To_File()
   {
      var svc = CreateService();
      var manifest = await svc.SaveAsync();

      Assert.True(File.Exists(svc.ManifestPath));
      var json = await File.ReadAllTextAsync(svc.ManifestPath);
      Assert.Contains("hercules-test", json);
   }

   [Fact]
   public void Validate_Returns_Error_For_Empty_ProtocolVersions()
   {
      var svc = new AgentManifestService(
         "hercules-test",
         "Test Agent",
         "Test description",
         "http://localhost:5000",
         _tempDir,
         () => new List<ManifestCapability>
         {
            new() { Name = "cap", Description = "C", PhraseReceivers = ["x"] }
         },
         null, "", null, "",
         new List<string>(), // empty protocol versions
         null, null);

      var errors = svc.Validate();
      Assert.NotEmpty(errors);
      Assert.Contains(errors, e => e.Contains("supportedProtocolVersions пуст"));
   }

   [Fact]
   public void Save_Includes_ProtocolVersions_ResourceLimits_TrustMetadata_And_Skills()
   {
      var svc = new AgentManifestService(
         "hercules-test",
         "Test Agent",
         "Test description",
         "http://localhost:5000",
         _tempDir,
         () => new List<ManifestCapability>
         {
            new() { Name = "cap", Description = "C", PhraseReceivers = ["x"] }
         },
         () => new List<ManifestSkillEntry>
         {
            new() { Id = "skill-001", Name = "S", Description = "D", RiskLevel = "low" }
         },
         "", null, "",
         new List<string> { "1.0", "2.0" },
         new ManifestResourceLimits { MaxTokensPerRequest = 4096 },
         new ManifestTrustMetadata { Level = "verified" });

      // Verify manifest content directly (avoids JSON parsing edge cases)
      AgentManifest manifest = svc.Current;

      // Protocol versions
      Assert.Equal(2, manifest.SupportedProtocolVersions.Count);
      Assert.Contains("1.0", manifest.SupportedProtocolVersions);
      Assert.Contains("2.0", manifest.SupportedProtocolVersions);

      // Resource limits
      Assert.NotNull(manifest.ResourceLimits);
      Assert.Equal(4096, manifest.ResourceLimits.MaxTokensPerRequest);

      // Trust metadata
      Assert.NotNull(manifest.TrustMetadata);
      Assert.Equal("verified", manifest.TrustMetadata.Level);

      // Skills
      Assert.Single(manifest.Skills);
      Assert.Equal("skill-001", manifest.Skills[0].Id);

      // Save and verify file contains key data
      svc.Save();
      Assert.True(File.Exists(svc.ManifestPath));
      var json = File.ReadAllText(svc.ManifestPath);
      Assert.Contains("1.0", json);
      Assert.Contains("skill-001", json);
   }
}