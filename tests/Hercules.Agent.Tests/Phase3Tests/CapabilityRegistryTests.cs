using Hercules.Mesh;
using Xunit;

namespace Hercules.Agent.Tests.Phase3Tests;

public class CapabilityRegistryTests : IDisposable
{
   private readonly CapabilityRegistry _registry;
   private readonly string _tempDir;

   public CapabilityRegistryTests()
   {
      _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-reg-{Guid.NewGuid():N}");
      Directory.CreateDirectory(_tempDir);
      var dbPath = Path.Combine(_tempDir, "registry.db");
      _registry = new CapabilityRegistry(dbPath);
   }

   public void Dispose()
   {
      try
      {
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
            new() { Name = "code-review", Description = "Code review", PhraseReceivers = phrases }
         }
      };
   }

   [Fact]
   public void Register_And_Get_Agent()
   {
      var manifest = CreateManifest("agent-a", "http://localhost:5001", ["review"]);
      _registry.Register(manifest);

      var found = _registry.Get("agent-a");
      Assert.NotNull(found);
      Assert.Equal("agent-a", found.AgentId);
      Assert.Equal("http://localhost:5001", found.Endpoint);
   }

   [Fact]
   public void Register_Overwrites_Existing_Agent()
   {
      _registry.Register(CreateManifest("agent-b", "http://old:5000", ["old"]));
      _registry.Register(CreateManifest("agent-b", "http://new:6000", ["new"]));

      var found = _registry.Get("agent-b");
      Assert.NotNull(found);
      Assert.Equal("http://new:6000", found.Endpoint);
      Assert.Equal("new", found.Capabilities[0].PhraseReceivers[0]);
   }

   [Fact]
   public void ListAgents_Returns_All_Registered()
   {
      _registry.Register(CreateManifest("alpha", "http://a", ["x"]));
      _registry.Register(CreateManifest("beta", "http://b", ["y"]));

      var agents = _registry.ListAgents();
      Assert.Equal(2, agents.Count);
      Assert.Contains(agents, a => a.AgentId == "alpha");
      Assert.Contains(agents, a => a.AgentId == "beta");
   }

   [Fact]
   public void FindByCapability_Returns_Agents_With_Capability()
   {
      _registry.Register(CreateManifest("reviewer-1", "http://r1", ["review"]));
      _registry.Register(CreateManifest("reviewer-2", "http://r2", ["review"]));
      _registry.Register(new AgentManifest
      {
         AgentId = "other-agent",
         DisplayName = "Other",
         Endpoint = "http://other",
         Capabilities = new List<ManifestCapability>
         {
            new() { Name = "other-cap", Description = "Other", PhraseReceivers = ["other"] }
         }
      });

      var found = _registry.FindByCapability("code-review");
      Assert.Equal(2, found.Count);
      Assert.All(found, a => Assert.Contains("reviewer", a.AgentId));
   }

   [Fact]
   public void FindByPhrase_Returns_Agents_With_Matching_Phrase()
   {
      _registry.Register(CreateManifest("coder", "http://coder", ["refactor", "clean up"]));
      _registry.Register(CreateManifest("writer", "http://writer", ["write", "compose"]));

      var found = _registry.FindByPhrase("refactor");
      Assert.Single(found);
      Assert.Equal("coder", found[0].AgentId);
   }

   [Fact]
   public void Remove_Deletes_Agent_From_Registry()
   {
      _registry.Register(CreateManifest("to-remove", "http://rem", ["x"]));
      Assert.NotNull(_registry.Get("to-remove"));

      var removed = _registry.Remove("to-remove");
      Assert.True(removed);
      Assert.Null(_registry.Get("to-remove"));
   }

   [Fact]
   public void ListCapabilities_Returns_Capabilities_For_Agent()
   {
      var manifest = new AgentManifest
      {
         AgentId = "multi-cap",
         DisplayName = "Multi",
         Endpoint = "http://multi",
         Capabilities = new List<ManifestCapability>
         {
            new() { Name = "cap-a", Description = "A", PhraseReceivers = ["a"] },
            new() { Name = "cap-b", Description = "B", PhraseReceivers = ["b"] },
            new() { Name = "cap-c", Description = "C", PhraseReceivers = ["c"] }
         }
      };
      _registry.Register(manifest);

      var caps = _registry.ListCapabilities("multi-cap");
      Assert.Equal(3, caps.Count);
      Assert.Contains(caps, c => c.Name == "cap-a");
      Assert.Contains(caps, c => c.Name == "cap-b");
      Assert.Contains(caps, c => c.Name == "cap-c");
   }

   [Fact]
   public void Touch_Updates_LastSeen()
   {
      _registry.Register(CreateManifest("touch-test", "http://t", ["x"]));
      var before = _registry.ListAgents().First(a => a.AgentId == "touch-test").LastSeen;

      Thread.Sleep(50);
      _registry.Touch("touch-test");

      var after = _registry.ListAgents().First(a => a.AgentId == "touch-test").LastSeen;
      Assert.NotEqual(before, after);
   }

   [Fact]
   public void Get_Returns_Null_For_Nonexistent_Agent()
   {
      Assert.Null(_registry.Get("nonexistent"));
   }

   [Fact]
   public void Persistence_Survives_New_Instance()
   {
      var dbPath = Path.Combine(_tempDir, "persist.db");
      using (var reg1 = new CapabilityRegistry(dbPath))
      {
         reg1.Register(CreateManifest("persistent", "http://persist", ["survive"]));
      }

      using var reg2 = new CapabilityRegistry(dbPath);
      var found = reg2.Get("persistent");
      Assert.NotNull(found);
      Assert.Equal("persistent", found.AgentId);
   }
}