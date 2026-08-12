using System.Text.Json;
using Hercules.Config;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Config;

public class RuntimeConfigStoreTests
{
   private static string NewTempFile()
   {
      var path = Path.Combine(Path.GetTempPath(), $"hercules-rcs-{Guid.NewGuid():N}.json");
      return path;
   }

   private static AppConfig SampleConfig()
   {
      return new AppConfig
      {
         Llm = new LlmConfig { Provider = "yandexgpt" },
         Agent = new AgentConfig { SkillCreationThreshold = 3, ReflectionEveryNCommands = 10 }
      };
   }

   [Fact]
   public void Initial_Config_Is_Available_Through_Current()
   {
      var path = NewTempFile();
      try
      {
         var initial = SampleConfig();
         var store = new RuntimeConfigStore(initial, path, NullLogger<RuntimeConfigStore>.Instance);

         Assert.Same(initial, store.Current);
         Assert.Equal("yandexgpt", store.Current.Llm.Provider);
      }
      finally
      {
         TryDelete(path);
      }
   }

   [Fact]
   public void Update_Replaces_Config_And_Persists_To_File()
   {
      var path = NewTempFile();
      try
      {
         var store = new RuntimeConfigStore(SampleConfig(), path, NullLogger<RuntimeConfigStore>.Instance);
         var next = new AppConfig
         {
            Llm = new LlmConfig { Provider = "ollama-local" },
            Agent = new AgentConfig { SkillCreationThreshold = 5 }
         };

         store.Update(next);

         Assert.Same(next, store.Current);
         Assert.Equal("ollama-local", store.Current.Llm.Provider);
         Assert.Equal(5, store.Current.Agent.SkillCreationThreshold);
         // Файл должен существовать
         Assert.True(File.Exists(path));
      }
      finally
      {
         TryDelete(path);
      }
   }

   [Fact]
   public void Update_Raises_Changed_Event_With_New_Config()
   {
      var path = NewTempFile();
      try
      {
         var store = new RuntimeConfigStore(SampleConfig(), path, NullLogger<RuntimeConfigStore>.Instance);
         AppConfig? received = null;
         store.Changed += (_, cfg) => received = cfg;

         var next = new AppConfig { Llm = new LlmConfig { Provider = "ollama-cloud" } };
         store.Update(next);

         Assert.NotNull(received);
         Assert.Same(next, received);
      }
      finally
      {
         TryDelete(path);
      }
   }

   [Fact]
   public void Patch_Merges_Nested_Objects_Without_Removing_Unpatched_Fields()
   {
      var path = NewTempFile();
      try
      {
         var initial = new AppConfig
         {
            Llm = new LlmConfig
            {
               Provider = "yandexgpt",
               Fallback = ["ollama-cloud", "ollama-local"],
               YandexGpt = new YandexGptConfig { ApiKey = "secret-key", Model = "yandexgpt" }
            },
            Agent = new AgentConfig { SkillCreationThreshold = 3, ReflectionEveryNCommands = 10 }
         };
         var store = new RuntimeConfigStore(initial, path, NullLogger<RuntimeConfigStore>.Instance);

         // Патчим только llm.provider — остальные поля llm должны сохраниться.
         // Merge patch использует camelCase (JsonNamingPolicy.CamelCase в JsonOptions).
         var patch = JsonDocument.Parse("""{"llm":{"provider":"ollama-local"}}""").RootElement;
         store.Patch(patch);

         Assert.Equal("ollama-local", store.Current.Llm.Provider);
         Assert.Equal("secret-key", store.Current.Llm.YandexGpt.ApiKey);
         Assert.Equal("yandexgpt", store.Current.Llm.YandexGpt.Model);
         Assert.Equal(2, store.Current.Llm.Fallback.Count);
         // Agent не затронут
         Assert.Equal(3, store.Current.Agent.SkillCreationThreshold);
         Assert.Equal(10, store.Current.Agent.ReflectionEveryNCommands);
      }
      finally
      {
         TryDelete(path);
      }
   }

   [Fact]
   public void Patch_Null_Value_Removes_Property_From_Merged_Result()
   {
      var path = NewTempFile();
      try
      {
         var initial = new AppConfig
         {
            Agent = new AgentConfig { SystemPrompt = "старый промпт", SkillCreationThreshold = 3 }
         };
         var store = new RuntimeConfigStore(initial, path, NullLogger<RuntimeConfigStore>.Instance);

         // Патчим systemPrompt = null → должен удалиться из результата (camelCase).
         var patch = JsonDocument.Parse("""{"agent":{"systemPrompt":null}}""").RootElement;
         store.Patch(patch);

         // После удаления свойства при десериализации используется дефолтное значение
         Assert.Equal(new AgentConfig().SystemPrompt, store.Current.Agent.SystemPrompt);
         // Другие поля сохраняются
         Assert.Equal(3, store.Current.Agent.SkillCreationThreshold);
      }
      finally
      {
         TryDelete(path);
      }
   }

   [Fact]
   public void Patch_Raises_Changed_Event()
   {
      var path = NewTempFile();
      try
      {
         var store = new RuntimeConfigStore(SampleConfig(), path, NullLogger<RuntimeConfigStore>.Instance);
         var eventCount = 0;
         store.Changed += (_, _) => eventCount++;

         var patch = JsonDocument.Parse("""{"agent":{"skillCreationThreshold":7}}""").RootElement;
         store.Patch(patch);

         Assert.Equal(1, eventCount);
         Assert.Equal(7, store.Current.Agent.SkillCreationThreshold);
      }
      finally
      {
         TryDelete(path);
      }
   }

   [Fact]
   public void Patch_Persists_Merged_Config_To_File()
   {
      var path = NewTempFile();
      try
      {
         var store = new RuntimeConfigStore(SampleConfig(), path, NullLogger<RuntimeConfigStore>.Instance);

         var patch = JsonDocument.Parse("""{"llm":{"provider":"ollama-cloud"}}""").RootElement;
         store.Patch(patch);

         Assert.True(File.Exists(path));
         var savedJson = File.ReadAllText(path);
         using var doc = JsonDocument.Parse(savedJson);
         Assert.Equal("ollama-cloud", doc.RootElement.GetProperty("llm").GetProperty("provider").GetString());
      }
      finally
      {
         TryDelete(path);
      }
   }

   [Fact]
   public void Update_Uses_Atomic_File_Replace()
   {
      // Проверяем, что .tmp-файл не остаётся после успешного сохранения
      var path = NewTempFile();
      try
      {
         var store = new RuntimeConfigStore(SampleConfig(), path, NullLogger<RuntimeConfigStore>.Instance);
         store.Update(new AppConfig { Llm = new LlmConfig { Provider = "ollama-local" } });

         Assert.True(File.Exists(path));
         Assert.False(File.Exists(path + ".tmp"));
      }
      finally
      {
         TryDelete(path);
         TryDelete(path + ".tmp");
      }
   }

   private static void TryDelete(string path)
   {
      try
      {
         if (File.Exists(path)) File.Delete(path);
      }
      catch
      {
         /* best effort */
      }
   }

   [Fact]
   public void Constructor_ThrowsOnNullInitial()
   {
      var ex = Assert.Throws<ArgumentNullException>(() =>
         new RuntimeConfigStore(null!, "path.json", NullLogger<RuntimeConfigStore>.Instance));
      Assert.Equal("initial", ex.ParamName);
   }

   [Fact]
   public void Constructor_ThrowsOnNullFilePath()
   {
      var ex = Assert.Throws<ArgumentNullException>(() =>
         new RuntimeConfigStore(new AppConfig(), null!, NullLogger<RuntimeConfigStore>.Instance));
      Assert.Equal("filePath", ex.ParamName);
   }

   [Fact]
   public void Update_ThrowsOnNullConfig()
   {
      var path = NewTempFile();
      try
      {
         var store = new RuntimeConfigStore(SampleConfig(), path, NullLogger<RuntimeConfigStore>.Instance);
         var ex = Assert.Throws<ArgumentNullException>(() => store.Update(null!));
         Assert.Equal("next", ex.ParamName);
      }
      finally
      {
         TryDelete(path);
      }
   }
}