using System.IO.Compression;
using System.Runtime.CompilerServices;
using Hercules.Config;
using Hercules.LLM;
using Hercules.LLM.JsonRepair;
using Hercules.Skills;
using Hercules.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Phase2Tests;

public class SkillPackagerTests : IDisposable
{
   private readonly SkillPackager _packager;
   private readonly FileSkillRepository _repo;
   private readonly SkillManager _skillManager;
   private readonly string _tempDir;

   public SkillPackagerTests()
   {
      _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-pkg-{Guid.NewGuid():N}");
      Directory.CreateDirectory(_tempDir);
      var storageCfg = new StorageConfig { DataRoot = _tempDir };
      _repo = new FileSkillRepository(storageCfg, NullLogger<FileSkillRepository>.Instance);
      _packager = new SkillPackager(_repo);
      _skillManager = new SkillManager(_repo, new StubLlm("test"), new AgentConfig(), new JsonRepairService());
   }

   public void Dispose()
   {
      try
      {
         if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
      }
      catch
      {
         /* best effort */
      }
   }

   [Fact]
   public void Export_Creates_Valid_Skillpkg_File()
   {
      var skill = _skillManager.CreateManual("test-skill", ["привет"], "Ты — тестовый ассистент.");
      var path = _packager.Export(skill.Meta.Id);
      Assert.True(File.Exists(path));
      Assert.EndsWith(".skillpkg", path);
   }

   [Fact]
   public void Export_Throws_For_Nonexistent_Skill()
   {
      Assert.Throws<InvalidOperationException>(() => _packager.Export("nonexistent"));
   }

   [Fact]
   public void Import_From_Exported_Package_Restores_Skill()
   {
      var skill = _skillManager.CreateManual("оригинал", ["погода"], "Ты — погодный ассистент.", "Прогноз погоды");
      var path = _packager.Export(skill.Meta.Id);

      // Удаляем оригинал
      // (в реальном сценарии импорт происходит в другой агент, но для теста — в тот же)

      // Импортируем с Rename (чтобы не конфликтовать)
      var imported = _packager.Import(path);

      Assert.NotNull(imported);
      Assert.Equal("оригинал", imported.Meta.Name);
      Assert.Equal("Ты — погодный ассистент.", imported.Prompt);
      Assert.Equal(["погода"], imported.Meta.PhraseReceivers);
      Assert.NotEqual(skill.Meta.Id, imported.Meta.Id); // ID должен быть другим (Rename)
   }

   [Fact]
   public void Import_Replace_Strategy_Overwrites_Existing_Skill()
   {
      var skill = _skillManager.CreateManual("замена", ["тест"], "Старый промпт");
      var path = _packager.Export(skill.Meta.Id);

      // Импортируем с Replace — должен перезаписать
      var imported = _packager.Import(path, ConflictResolution.Replace);

      Assert.Equal(skill.Meta.Id, imported.Meta.Id);
      Assert.Equal("Старый промпт", imported.Prompt);
   }

   [Fact]
   public void Import_Skip_Strategy_Returns_Existing_Skill()
   {
      var skill = _skillManager.CreateManual("skip-test", ["skip"], "Промпт");
      var path = _packager.Export(skill.Meta.Id);

      var imported = _packager.Import(path, ConflictResolution.Skip);

      // Skip возвращает существующий навык (по ID, не по ссылке — repo.Load создаёт новый объект)
      Assert.Equal(skill.Meta.Id, imported.Meta.Id);
      Assert.Equal(skill.Prompt, imported.Prompt);
   }

   [Fact]
   public void Validate_Returns_No_Errors_For_Valid_Package()
   {
      var skill = _skillManager.CreateManual("валидный", ["проверка"], "Промпт", "Описание");
      var path = _packager.Export(skill.Meta.Id);

      var errors = _packager.Validate(path);
      Assert.Empty(errors);
   }

   [Fact]
   public void Validate_Returns_Errors_For_Nonexistent_Package()
   {
      var errors = _packager.Validate("/nonexistent/path.skillpkg");
      Assert.NotEmpty(errors);
   }

   [Fact]
   public void Export_ProducesFileWithSkillpkgExtension()
   {
      var skill = _skillManager.CreateManual("ext-test", ["расширение"], "Промпт.");

      var path = _packager.Export(skill.Meta.Id);

      Assert.EndsWith(".skillpkg", path);
      Assert.True(File.Exists(path));
   }

   [Fact]
   public void Validate_ReturnsNoErrors_ForCorruptPackage()
   {
      // Проверяем, что Validate обнаруживает невалидный zip
      var tempZip = Path.Combine(_tempDir, "corrupt.skillpkg");
      File.WriteAllBytes(tempZip, new byte[] { 0x00, 0x01, 0x02 });

      var errors = _packager.Validate(tempZip);

      Assert.NotEmpty(errors);
   }

   [Fact]
   public void Import_SamePackageTwice_RenamesSecond()
   {
      var skill = _skillManager.CreateManual("duplicate", ["дубликат"], "Промпт.");
      var path = _packager.Export(skill.Meta.Id);

      var first = _packager.Import(path);
      var second = _packager.Import(path);

      Assert.NotEqual(first.Meta.Id, second.Meta.Id);
   }

   // ─── Folder export / import ─────────────────────────────────────────────────

   [Fact]
   public void ExportToFolder_Creates_SkillFolder()
   {
      var skill = _skillManager.CreateManual("folder-test", ["фолдер"], "System prompt.", "Описание.");
      var folderPath = _packager.ExportToFolder(skill.Meta.Id, _tempDir);

      Assert.True(Directory.Exists(folderPath));
      Assert.EndsWith($"skill.{skill.Meta.Id}", folderPath);
   }

   [Fact]
   public void ExportToFolder_Creates_AllSpecFiles()
   {
      var skill = _skillManager.CreateManual("spec-files", ["spec"], "Промпт.", "Описание.");
      var folderPath = _packager.ExportToFolder(skill.Meta.Id, _tempDir);

      Assert.True(File.Exists(Path.Combine(folderPath, "skill.meta.json")));
      Assert.True(File.Exists(Path.Combine(folderPath, "skill.description.md")));
      Assert.True(File.Exists(Path.Combine(folderPath, "skill.prompt.md")));
   }

   [Fact]
   public void ExportToFolder_Omits_OptionalFiles_When_Null()
   {
      var skill = _skillManager.CreateManual("no-optionals", ["noop"], "Промпт.");
      _packager.ExportToFolder(skill.Meta.Id, _tempDir,
          examples: null, changelog: null, includeUsage: false);

      var folderPath = Path.Combine(_tempDir, $"skill.{skill.Meta.Id}");
      Assert.False(File.Exists(Path.Combine(folderPath, "skill.examples.json")));
      Assert.False(File.Exists(Path.Combine(folderPath, "skill.changelog.md")));
      Assert.False(File.Exists(Path.Combine(folderPath, "skill.usage.json")));
   }

   [Fact]
   public void ExportToFolder_Includes_Examples_WhenProvided()
   {
      var skill = _skillManager.CreateManual("examples-test", ["ex"], "Промпт.");
      var examples = new SkillExamples
      {
         Examples = new List<SkillExample>
         {
            new() { Input = "Привет", ExpectedBehavior = "Отвечает приветствием" }
         }
      };
      _packager.ExportToFolder(skill.Meta.Id, _tempDir, examples: examples);

      var folderPath = Path.Combine(_tempDir, $"skill.{skill.Meta.Id}");
      var examplesFile = Path.Combine(folderPath, "skill.examples.json");
      Assert.True(File.Exists(examplesFile));
      Assert.Contains("Привет", File.ReadAllText(examplesFile));
   }

   [Fact]
   public void ExportToFolder_Includes_Changelog_WhenProvided()
   {
      var skill = _skillManager.CreateManual("changelog-test", ["log"], "Промпт.");
      const string changelog = "# Changelog\n\n## v2 — 2025-06-01\n- Fixed bug";
      _packager.ExportToFolder(skill.Meta.Id, _tempDir, changelog: changelog);

      var folderPath = Path.Combine(_tempDir, $"skill.{skill.Meta.Id}");
      var changelogFile = Path.Combine(folderPath, "skill.changelog.md");
      Assert.True(File.Exists(changelogFile));
      Assert.Contains("Changelog", File.ReadAllText(changelogFile));
   }

   [Fact]
   public void ExportToFolder_Includes_ToolSchema_WhenSkillHasTools()
   {
      var skill = _skillManager.CreateManual("tools-test", ["tools"], "Промпт.");
      skill.Meta.Tools = new List<ToolDeclaration>
      {
         new() { Name = "http", Description = "HTTP requests", Required = true }
      };
      _repo.Save(skill);

      _packager.ExportToFolder(skill.Meta.Id, _tempDir);

      var folderPath = Path.Combine(_tempDir, $"skill.{skill.Meta.Id}");
      var toolSchema = Path.Combine(folderPath, "tool.schema.json");
      Assert.True(File.Exists(toolSchema));
      Assert.Contains("http", File.ReadAllText(toolSchema));
   }

   [Fact]
   public void Import_FromFolder_Restores_Skill()
   {
      var skill = _skillManager.CreateManual("folder-import", ["импорт"], "Промпт.", "Описание.");
      _packager.ExportToFolder(skill.Meta.Id, _tempDir);

      var folderPath = Path.Combine(_tempDir, $"skill.{skill.Meta.Id}");

      // Удаляем из репозитория
      // (folder import — в другой агент, для теста — в тот же с Rename)
      var imported = _packager.Import(folderPath);

      Assert.NotNull(imported);
      Assert.Equal("folder-import", imported.Meta.Name);
      Assert.Equal("Промпт.", imported.Prompt);
      Assert.Equal(["импорт"], imported.Meta.PhraseReceivers);
      Assert.NotEqual(skill.Meta.Id, imported.Meta.Id); // Rename
   }

   [Fact]
   public void Validate_Folder_WithMissingMeta_ReturnsError()
   {
      var emptyDir = Path.Combine(_tempDir, $"skill.empty-{Guid.NewGuid():N}");
      Directory.CreateDirectory(emptyDir);

      var errors = _packager.Validate(emptyDir);

      Assert.NotEmpty(errors);
      Assert.Contains(errors, e => e.Contains("skill.meta.json"));
   }

   [Fact]
   public void Validate_Folder_WithMissingPrompt_ReturnsError()
   {
      var dir = Path.Combine(_tempDir, $"skill.partial-{Guid.NewGuid():N}");
      Directory.CreateDirectory(dir);
      File.WriteAllText(Path.Combine(dir, "skill.meta.json"),
          "{\"id\":\"test\",\"name\":\"Test\",\"phrase_receivers\":[\"x\"]}");

      var errors = _packager.Validate(dir);

      Assert.NotEmpty(errors);
      Assert.Contains(errors, e => e.Contains("skill.prompt.md"));
   }

   [Fact]
   public void Validate_Folder_Valid_ReturnsNoErrors()
   {
      var skill = _skillManager.CreateManual("valid-folder", ["vf"], "Промпт.", "Описание.");
      _packager.ExportToFolder(skill.Meta.Id, _tempDir);

      var folderPath = Path.Combine(_tempDir, $"skill.{skill.Meta.Id}");
      var errors = _packager.Validate(folderPath);

      Assert.Empty(errors);
   }

   [Fact]
   public void Export_zip_Includes_SkillFolder_Structure()
   {
      var skill = _skillManager.CreateManual("zip-structure", ["zip"], "Промпт.", "Описание.");
      var zipPath = _packager.Export(skill.Meta.Id, _tempDir);

      using var archive = ZipFile.OpenRead(zipPath);
      var skillFolder = $"skill.{skill.Meta.Id}/";

      Assert.NotNull(archive.GetEntry($"{skillFolder}skill.meta.json"));
      Assert.NotNull(archive.GetEntry($"{skillFolder}skill.description.md"));
      Assert.NotNull(archive.GetEntry($"{skillFolder}skill.prompt.md"));
      Assert.NotNull(archive.GetEntry("skill.package.json"));
   }

   [Fact]
   public void Manifest_Includes_PackageFormat_AndSpecVersion()
   {
      var skill = _skillManager.CreateManual("manifest-check", ["mf"], "Промпт.");
      _packager.ExportToFolder(skill.Meta.Id, _tempDir);

      // Проверяем, что package.json в ZIP содержит новые поля
      var zipPath = _packager.Export(skill.Meta.Id, _tempDir);
      using var archive = ZipFile.OpenRead(zipPath);
      var manifestEntry = archive.GetEntry("skill.package.json");
      Assert.NotNull(manifestEntry);

      using var reader = new StreamReader(manifestEntry.Open());
      var manifestJson = reader.ReadToEnd();

      Assert.Contains("\"package_format\"", manifestJson);
      Assert.Contains("\"package_spec_version\"", manifestJson);
   }
}

/// <summary>
///    Stub LLM для тестов SkillManager (не обращается к реальному провайдеру).
/// </summary>
internal sealed class StubLlm : ILLMClient
{
   private readonly string _response;

   public StubLlm(string response)
   {
      _response = response;
   }

   public string ProviderName => "stub";
   public string ModelName => "stub";

   public Task<LlmResponse> CompleteAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
   {
      return Task.FromResult(new LlmResponse(_response, ProviderName, ModelName));
   }

   public Task<LlmResponse> CompleteAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
   {
      return CompleteAsync(Roles.Main, messages, ct);
   }

   public IAsyncEnumerable<string> StreamAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
   {
      return StreamAsync(messages, ct);
   }

   public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatTurn> messages,
      [EnumeratorCancellation] CancellationToken ct = default)
   {
      await Task.Yield();
      yield return _response;
   }
}