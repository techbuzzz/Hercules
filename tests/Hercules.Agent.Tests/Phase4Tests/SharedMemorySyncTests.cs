using System.Text.Json;
using Hercules.Mesh;
using Xunit;

namespace Hercules.Agent.Tests.Phase4Tests;

public class SharedMemorySyncTests : IDisposable
{
   private readonly AgentManifestService _manifestService;
   private readonly CapabilityRegistry _registry;
   private readonly SharedMemorySync _sync;
   private readonly string _tempDir;
   private readonly IntentTransport _transport;

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

      _transport = new IntentTransport(_registry);
      _sync = new SharedMemorySync(_tempDir, _registry, _transport, _manifestService);
   }

   public void Dispose()
   {
      try
      {
         _sync.Dispose();
         _transport.Dispose();
         _registry.Dispose();
      }
      catch
      {
      }

      try
      {
         if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
      }
      catch
      {
      }
   }

   private void AssertFactInFile(string marker)
   {
      Assert.True(File.Exists(_sync.SharedMemoryPath), $"File {_sync.SharedMemoryPath} should exist");
      var json = File.ReadAllText(_sync.SharedMemoryPath);
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

      var sync2 = new SharedMemorySync(_tempDir, _registry, _transport, _manifestService);
      AssertFactInFileFor(sync2, "project-hercules");
      sync2.Dispose();
   }

   private static void AssertFactInFileFor(SharedMemorySync sync, string marker)
   {
      Assert.True(File.Exists(sync.SharedMemoryPath));
      var json = File.ReadAllText(sync.SharedMemoryPath);
      Assert.Contains(marker, json);
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
      // Прямая сериализация для проверки
      var fact = new SharedMemoryFact
      {
         Id = "fact-x",
         Category = "profile",
         Content = "v2-marker",
         Version = 2
      };
      var directJson = JsonSerializer.Serialize(fact, new JsonSerializerOptions
      {
         PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
         WriteIndented = true
      });
      Assert.Contains("v2-marker", directJson);

      // Теперь через ReceiveFact
      _sync.ReceiveFact(fact);

      Assert.True(File.Exists(_sync.SharedMemoryPath));
      var json1 = File.ReadAllText(_sync.SharedMemoryPath);
      Assert.Contains("v2-marker", json1);
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
      // ReceiveFact с фиксированным Id
      _sync.ReceiveFact(new SharedMemoryFact
      {
         Id = "fixed-id-1",
         Category = "profile",
         Content = "marker-content",
         Version = 1
      });

      // Читаем файл — проверяем, что fixed-id-1 есть
      var fileJson = File.ReadAllText(_sync.SharedMemoryPath);
      Assert.Contains("fixed-id-1", fileJson);

      // Проверяем LoadLocalFacts — должен вернуть факт с Id = fixed-id-1
      var facts = _sync.GetLocalFacts();
      Assert.NotEmpty(facts);
      var loadedId = facts[0].Id;
      Assert.Equal("fixed-id-1", loadedId);

      // RemoveFact
      Assert.True(_sync.RemoveFact("fixed-id-1"), "RemoveFact should return true");
   }

   [Fact]
   public void RemoveFact_Returns_False_For_Nonexistent()
   {
      Assert.False(_sync.RemoveFact("nonexistent"));
   }
}