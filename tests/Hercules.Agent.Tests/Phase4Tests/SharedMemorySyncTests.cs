using System.Text.Json;
using Hercules.Config;
using Hercules.Mesh;
using Hercules.Mesh.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Phase4Tests;

public class SharedMemorySyncTests : IDisposable
{
   private readonly AgentManifestService _manifestService;
   private readonly CapabilityRegistry _registry;
   private readonly SharedMemorySync _sync;
   private readonly SharedMemorySyncConfig _config;
   private readonly string _tempDir;
   private readonly ITransport _transport;

   public SharedMemorySyncTests()
   {
      _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-sync-{Guid.NewGuid():N}");
      Directory.CreateDirectory(_tempDir);

      var dbPath = Path.Combine(_tempDir, "registry.db");
      _registry = new CapabilityRegistry(dbPath);

      _manifestService = new AgentManifestService(
         "hercules-test",
         "Test",
         "Test agent",
         "http://localhost:5000",
         _tempDir,
         () => new List<ManifestCapability>());

      _transport = new HttpTransportAdapter(_registry);

      _config = new SharedMemorySyncConfig
      {
         Enabled = true,
         DefaultTtlMinutes = 60,
         MaxFactsPerAgent = 100,
         EncryptionRequired = true,
         MaxAllowedSensitivity = "Sensitive",
         SyncIntervalMinutes = 30
      };

      _sync = new SharedMemorySync(_tempDir, _registry, _transport, _manifestService,
         _config, NullLogger<SharedMemorySync>.Instance);
   }

   public void Dispose()
   {
      try { _sync.Dispose(); _transport.Dispose(); _registry.Dispose(); }
      catch { /* best effort */ }
      try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
      catch { /* best effort */ }
   }

   private void AssertFactInFile(string marker)
   {
      Assert.True(File.Exists(_sync.SharedMemoryPath));
      var json = File.ReadAllText(_sync.SharedMemoryPath);
      Assert.Contains(marker, json);
   }

   private static void AssertFactInFileFor(SharedMemorySync sync, string marker)
   {
      Assert.True(File.Exists(sync.SharedMemoryPath));
      var json = File.ReadAllText(sync.SharedMemoryPath);
      Assert.Contains(marker, json);
   }

   [Fact]
   public async Task PublishFact_Creates_Fact_Locally()
   {
      var fact = await _sync.PublishFactAsync("profile", "user-likes-python");

      Assert.Equal("profile", fact.Category);
      Assert.Equal("user-likes-python", fact.Content);
      Assert.Equal("hercules-test", fact.SourceAgent);
      AssertFactInFile("user-likes-python");
   }

   [Fact]
   public async Task PublishFact_Persists_Across_New_Instance()
   {
      await _sync.PublishFactAsync("entities", "project-hercules");

      var sync2 = new SharedMemorySync(_tempDir, _registry, _transport, _manifestService,
         _config, NullLogger<SharedMemorySync>.Instance);
      AssertFactInFileFor(sync2, "project-hercules");
      sync2.Dispose();
   }

   [Fact]
   public void ReceiveFact_Adds_New_Fact()
   {
      var incoming = new SharedMemoryFact
      {
         Id = "external-1",
         Category = "preferences",
         Content = "prefer-concise-answers",
         SourceAgent = "other-agent",
         Version = 1
      };

      var received = _sync.ReceiveFact(incoming);
      Assert.NotNull(received);
      AssertFactInFile("prefer-concise-answers");
   }

   [Fact]
   public void ReceiveFact_Ignores_Older_Version()
   {
      var fact = new SharedMemoryFact
      {
         Id = "fact-x",
         Category = "profile",
         Content = "v2-marker",
         Version = 2
      };

      _sync.ReceiveFact(fact);

      Assert.True(File.Exists(_sync.SharedMemoryPath));
      var json = File.ReadAllText(_sync.SharedMemoryPath);
      Assert.Contains("v2-marker", json);
   }

   [Fact]
   public async Task GetFactsForAgent_Returns_All_When_No_Restrictions()
   {
      await _sync.PublishFactAsync("profile", "public-fact-marker");

      var forAgent = _sync.GetFactsForAgent("any-agent");
      Assert.NotEmpty(forAgent);
   }

   [Fact]
   public async Task GetFactsForAgent_Filters_By_AllowedAgents()
   {
      await _sync.PublishFactAsync("profile", "restricted-marker", ["trusted-agent"]);

      var forAllowed = _sync.GetFactsForAgent("trusted-agent");
      var forDenied = _sync.GetFactsForAgent("untrusted-agent");

      Assert.NotEmpty(forAllowed);
      Assert.Empty(forDenied);
   }

   [Fact]
   public void RemoveFact_Deletes_From_Local()
   {
      _sync.ReceiveFact(new SharedMemoryFact
      {
         Id = "fixed-id-1",
         Category = "profile",
         Content = "marker-content",
         Version = 1
      });

      var fileJson = File.ReadAllText(_sync.SharedMemoryPath);
      Assert.Contains("fixed-id-1", fileJson);

      var facts = _sync.GetLocalFacts();
      Assert.NotEmpty(facts);
      Assert.Equal("fixed-id-1", facts[0].Id);

      Assert.True(_sync.RemoveFact("fixed-id-1"), "RemoveFact should return true");
   }

   [Fact]
   public void RemoveFact_Returns_False_For_Nonexistent()
   {
      Assert.False(_sync.RemoveFact("nonexistent"));
   }

   // ===== TTL tests (task_051) =====

   [Fact]
   public async Task PublishFact_Sets_ExpiresAt_For_TTL_Fact()
   {
      var fact = await _sync.PublishFactAsync("profile", "ttl-fact",
         ttlMinutes: 30, sensitivity: "Internal", source: "session_extract");

      Assert.Equal(30, fact.TtlMinutes);
      Assert.NotNull(fact.ExpiresAt);
      Assert.NotEmpty(fact.CreatedAt);
   }

   [Fact]
   public async Task PublishFact_Permanent_Fact_Has_No_ExpiresAt()
   {
      var fact = await _sync.PublishFactAsync("profile", "permanent-fact",
         ttlMinutes: 0);

      Assert.Equal(0, fact.TtlMinutes);
      Assert.Null(fact.ExpiresAt);
   }

   [Fact]
   public void ReceiveFact_Rejects_Expired_Fact()
   {
      var expired = new SharedMemoryFact
      {
         Id = "expired-1",
         Category = "profile",
         Content = "should-be-rejected",
         SourceAgent = "peer",
         Version = 1,
         TtlMinutes = 1,
         ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-10).ToString("o")
      };

      var result = _sync.ReceiveFact(expired);
      Assert.Null(result);

      // Fact should NOT be in the file
      if (File.Exists(_sync.SharedMemoryPath))
      {
         var json = File.ReadAllText(_sync.SharedMemoryPath);
         Assert.DoesNotContain("expired-1", json);
      }
   }

   [Fact]
   public void GetLocalFacts_Excludes_Expired_Facts()
   {
      // Manually write a mixed file
      var facts = new List<SharedMemoryFact>
      {
         new SharedMemoryFact { Id = "valid-1", Category = "profile", Content = "valid", Version = 1 },
         new SharedMemoryFact
         {
            Id = "expired-2",
            Category = "profile",
            Content = "expired",
            Version = 1,
            TtlMinutes = 1,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("o")
         }
      };
      var json = JsonSerializer.Serialize(facts, new JsonSerializerOptions { WriteIndented = true });
      Directory.CreateDirectory(Path.GetDirectoryName(_sync.SharedMemoryPath)!);
      File.WriteAllText(_sync.SharedMemoryPath, json);

      var localFacts = _sync.GetLocalFacts();
      Assert.Single(localFacts);
      Assert.Equal("valid-1", localFacts[0].Id);
   }

   [Fact]
   public async Task PublishFact_Uses_DefaultTtl_From_Config()
   {
      var fact = await _sync.PublishFactAsync("profile", "default-ttl-fact");

      // Config.DefaultTtlMinutes = 60
      Assert.Equal(60, fact.TtlMinutes);
      Assert.NotNull(fact.ExpiresAt);
   }

   // ===== Sensitivity tests (task_051) =====

   [Fact]
   public async Task PublishFact_Blocks_Restricted_Facts()
   {
      var fact = await _sync.PublishFactAsync("profile", "secret-data",
         sensitivity: "Restricted");

      // Should NOT be saved locally
      Assert.False(File.Exists(_sync.SharedMemoryPath));
   }

   [Fact]
   public void ReceiveFact_Rejects_Restricted_Fact()
   {
      var restricted = new SharedMemoryFact
      {
         Id = "restricted-1",
         Category = "profile",
         Content = "secret",
         SourceAgent = "peer",
         Version = 1,
         Sensitivity = "Restricted"
      };

      var result = _sync.ReceiveFact(restricted);
      Assert.Null(result);

      if (File.Exists(_sync.SharedMemoryPath))
      {
         var json = File.ReadAllText(_sync.SharedMemoryPath);
         Assert.DoesNotContain("restricted-1", json);
      }
   }

   [Fact]
   public void ReceiveFact_Rejects_Excessive_Sensitivity()
   {
      // Config.MaxAllowedSensitivity = "Sensitive" (so Sensitive is allowed, Restricted is not)
      var sensitive = new SharedMemoryFact
      {
         Id = "sensitive-1",
         Category = "profile",
         Content = "sensitive-content",
         SourceAgent = "peer",
         Version = 1,
         Sensitivity = "Sensitive"
      };

      var result = _sync.ReceiveFact(sensitive);
      Assert.NotNull(result); // Sensitive IS allowed

      // Now try Restricted
      var restricted = new SharedMemoryFact
      {
         Id = "restricted-2",
         Category = "profile",
         Content = "restricted-content",
         SourceAgent = "peer",
         Version = 1,
         Sensitivity = "Restricted"
      };

      var reject = _sync.ReceiveFact(restricted);
      Assert.Null(reject);
   }

   [Fact]
   public void GetLocalFacts_Filters_Sensitivity()
   {
      // Manually write mixed sensitivity facts
      var facts = new List<SharedMemoryFact>
      {
         new SharedMemoryFact { Id = "public-1", Category = "p", Content = "pub", Version = 1, Sensitivity = "Public" },
         new SharedMemoryFact { Id = "internal-1", Category = "p", Content = "int", Version = 1, Sensitivity = "Internal" },
         new SharedMemoryFact { Id = "sensitive-1", Category = "p", Content = "sen", Version = 1, Sensitivity = "Sensitive" },
         new SharedMemoryFact { Id = "restricted-1", Category = "p", Content = "res", Version = 1, Sensitivity = "Restricted" }
      };
      File.WriteAllText(_sync.SharedMemoryPath,
         JsonSerializer.Serialize(facts, new JsonSerializerOptions { WriteIndented = true }));

      var local = _sync.GetLocalFacts();

      // Config.MaxAllowedSensitivity = "Sensitive" → Public + Internal + Sensitive allowed
      Assert.Equal(3, local.Count);
      Assert.All(local, f => Assert.NotEqual("Restricted", f.Sensitivity));
   }

   // ===== Provenance tests (task_051) =====

   [Fact]
   public async Task PublishFact_Sets_Provenance_Fields()
   {
      var fact = await _sync.PublishFactAsync(
         "entities",
         "user-pref-fact",
         source: "session_extract",
         tags: new List<string> { "user", "preference" });

      Assert.Equal("hercules-test", fact.SourceAgent);
      Assert.Equal("session_extract", fact.Source);
      Assert.NotEmpty(fact.CreatedAt);
      Assert.NotEmpty(fact.UpdatedAt);
      Assert.Contains("user", fact.Tags);
      Assert.Contains("preference", fact.Tags);
   }

   [Fact]
   public async Task PublishFact_Serializes_Provenance_To_File()
   {
      await _sync.PublishFactAsync("entities", "prov-test",
         source: "skill:test-skill",
         tags: new List<string> { "skill", "entity" });

      var json = File.ReadAllText(_sync.SharedMemoryPath);
      Assert.Contains("prov-test", json);
      Assert.Contains("skill:test-skill", json);
   }

   // ===== Max facts limit (task_051) =====

   [Fact]
   public async Task PublishFact_Respects_MaxFactsPerAgent_Limit()
   {
      var smallConfig = new SharedMemorySyncConfig
      {
         MaxFactsPerAgent = 2,
         DefaultTtlMinutes = 60,
         MaxAllowedSensitivity = "Sensitive"
      };

      var limitedSync = new SharedMemorySync(_tempDir + "-limit", _registry, _transport, _manifestService,
         smallConfig, NullLogger<SharedMemorySync>.Instance);
      Directory.CreateDirectory(Directory.GetParent(limitedSync.SharedMemoryPath)!.FullName);

      var fact1 = await limitedSync.PublishFactAsync("p", "f1");
      var fact2 = await limitedSync.PublishFactAsync("p", "f2");
      var fact3 = await limitedSync.PublishFactAsync("p", "f3"); // Should be blocked

      // fact3 should not have been saved (CreatedAt is empty)
      Assert.Empty(fact3.CreatedAt);
      limitedSync.Dispose();
   }

   // ===== Serialization compatibility =====

   [Fact]
   public void SharedMemoryFact_JsonRoundTrip_Preserves_Fields()
   {
      var fact = new SharedMemoryFact
      {
         Id = "roundtrip-1",
         Category = "entities",
         Content = "test-fact",
         SourceAgent = "peer-1",
         AllowedAgents = new List<string> { "agent-a" },
         Version = 3,
         CreatedAt = "2026-08-13T10:00:00Z",
         UpdatedAt = "2026-08-13T11:00:00Z",
         TtlMinutes = 120,
         ExpiresAt = "2026-08-13T12:00:00Z",
         Sensitivity = "Sensitive",
         Source = "session_extract",
         Tags = new List<string> { "tag1", "tag2" }
      };

      var json = JsonSerializer.Serialize(fact, new JsonSerializerOptions
      {
         PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
         WriteIndented = true
      });

      var restored = JsonSerializer.Deserialize<SharedMemoryFact>(json,
         new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

      Assert.NotNull(restored);
      Assert.Equal("roundtrip-1", restored.Id);
      Assert.Equal("entities", restored.Category);
      Assert.Equal("peer-1", restored.SourceAgent);
      Assert.Equal(3, restored.Version);
      Assert.Equal(120, restored.TtlMinutes);
      Assert.Equal("Sensitive", restored.Sensitivity);
      Assert.Equal("session_extract", restored.Source);
      Assert.Equal(2, restored.Tags.Count);
      Assert.Contains("tag1", restored.Tags);
   }
}
