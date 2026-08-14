using System.IO.Compression;
using System.Text.Json;
using Hercules.Config;
using Hercules.LLM.JsonRepair;
using Hercules.Skills;
using Hercules.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Phase2Tests;

/// <summary>
///     Tests for AgentTemplateManager (task_030).
///     Covers: List (empty + with files), Apply (imports, memory, tools), manifest parsing,
///     conflict resolution, ApplyTemplateResult structure.
/// </summary>
public class AgentTemplateManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _templatesDir;
    private readonly StorageConfig _storageCfg;
    private readonly FileSkillRepository _repo;
    private readonly SkillPackager _packager;
    private readonly AgentTemplateManager _manager;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public AgentTemplateManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hctpl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _templatesDir = Path.Combine(_tempDir, "Templates");
        Directory.CreateDirectory(_templatesDir);
        _storageCfg = new StorageConfig { DataRoot = _tempDir };
        _repo = new FileSkillRepository(_storageCfg, NullLogger<FileSkillRepository>.Instance);
        _packager = new SkillPackager(_repo);
        _manager = new AgentTemplateManager(_storageCfg, _packager);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }
        catch { /* best effort */ }
    }

    // ---- List ----

    [Fact]
    public void List_Returns_Empty_When_No_Templates()
    {
        var entries = _manager.List();
        Assert.Empty(entries);
    }

    [Fact]
    public void List_Returns_TemplateEntry_For_Valid_Zip()
    {
        // Arrange: create a valid .agenttemplate ZIP
        string zipPath = Path.Combine(_templatesDir, "test-template.agenttemplate");
        CreateValidTemplateZip(zipPath, "Test Template", "A test description", "test");

        // Act
        var entries = _manager.List();

        // Assert
        Assert.Single(entries);
        Assert.Equal("test-template.agenttemplate", entries[0].FileName);
        Assert.Equal("Test Template", entries[0].Name);
        Assert.Equal("A test description", entries[0].Description);
        Assert.Equal(1, entries[0].Version);
    }

    [Fact]
    public void List_Skips_Invalid_Zip_Files()
    {
        string validZip = Path.Combine(_templatesDir, "valid.agenttemplate");
        CreateValidTemplateZip(validZip, "Valid", "desc", "valid");
        string invalidZip = Path.Combine(_templatesDir, "invalid.agenttemplate");
        System.IO.File.WriteAllText(invalidZip, "not a zip file");

        var entries = _manager.List();

        Assert.Single(entries);
        Assert.Equal("valid.agenttemplate", entries[0].FileName);
    }

    [Fact]
    public void List_Returns_Multiple_Entries()
    {
        CreateValidTemplateZip(Path.Combine(_templatesDir, "a.agenttemplate"), "A", "desc a", "a");
        CreateValidTemplateZip(Path.Combine(_templatesDir, "b.agenttemplate"), "B", "desc b", "b");

        var entries = _manager.List();

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Name == "A");
        Assert.Contains(entries, e => e.Name == "B");
    }

    // ---- Apply ----

    [Fact]
    public void Apply_Throws_When_Template_Not_Found()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => _manager.Apply("nonexistent.agenttemplate"));
        Assert.Contains("nonexistent", ex.Message);
    }

    [Fact]
    public void Apply_Copies_Memory_Files()
    {
        // Arrange
        string zipPath = Path.Combine(_templatesDir, "mem-test.agenttemplate");
        CreateTemplateZipWithMemory(zipPath, "mem-template", "Test memory");

        // Act
        var result = _manager.Apply("mem-test.agenttemplate");

        // Assert
        Assert.Equal("mem-template", result.TemplateName);
        Assert.Contains("user_profile.md", result.InstalledMemoryFiles);
        string expectedPath = Path.Combine(_storageCfg.DataRoot, _storageCfg.MemoryDir, "user_profile.md");
        Assert.True(System.IO.File.Exists(expectedPath));
        string content = System.IO.File.ReadAllText(expectedPath);
        Assert.Contains("Test memory", content);
    }

    [Fact]
    public void Apply_Sets_HasErrors_False_When_Successful()
    {
        string zipPath = Path.Combine(_templatesDir, "ok.agenttemplate");
        CreateTemplateZipWithMemory(zipPath, "ok-template", "Content");

        var result = _manager.Apply("ok.agenttemplate");

        Assert.False(result.HasErrors);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Apply_Adds_Error_When_Skill_Not_Found_In_Archive()
    {
        // Create a ZIP with a missing skill
        string zipPath = Path.Combine(_templatesDir, "bad.agenttemplate");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var manifest = new { name = "Bad", description = "bad", version = 1, skills = new[] { "missing.skillpkg" }, memory_files = new[] { "user_profile.md" }, tool_files = Array.Empty<string>() };
            var jsonEntry = zip.CreateEntry("template.json");
            using (var w = new StreamWriter(jsonEntry.Open())) w.Write(JsonSerializer.Serialize(manifest));
            var memEntry = zip.CreateEntry("memory/user_profile.md");
            using (var mw = new StreamWriter(memEntry.Open())) mw.Write("# Memory");
        }

        var result = _manager.Apply("bad.agenttemplate");

        Assert.True(result.HasErrors);
        Assert.Single(result.Errors);
        Assert.Contains("missing.skillpkg", result.Errors[0]);
    }

    // ---- ApplyTemplateResult structure ----

    [Fact]
    public void ApplyTemplateResult_Default_HasEmptyLists()
    {
        var result = new ApplyTemplateResult();

        Assert.Empty(result.InstalledSkills);
        Assert.Empty(result.InstalledMemoryFiles);
        Assert.Empty(result.InstalledToolFiles);
        Assert.Empty(result.Errors);
        Assert.False(result.HasErrors);
    }

    // ---- TemplateEntry record ----

    [Fact]
    public void TemplateEntry_Contains_All_Fields()
    {
        var entry = new TemplateEntry(
            "file.agenttemplate",
            "Name",
            "Desc",
            2,
            5,
            "C:\\path\\file.agenttemplate");

        Assert.Equal("file.agenttemplate", entry.FileName);
        Assert.Equal("Name", entry.Name);
        Assert.Equal("Desc", entry.Description);
        Assert.Equal(2, entry.Version);
        Assert.Equal(5, entry.SkillCount);
        Assert.Equal("C:\\path\\file.agenttemplate", entry.FilePath);
    }

    // ---- TemplateManifest record ----

    [Fact]
    public void TemplateManifest_Default_Values()
    {
        var manifest = new TemplateManifest();

        Assert.Equal("", manifest.Name);
        Assert.Equal("", manifest.Description);
        Assert.Equal(1, manifest.Version);
        Assert.Empty(manifest.Skills);
        Assert.Empty(manifest.MemoryFiles);
        Assert.Empty(manifest.ToolFiles);
        Assert.Null(manifest.Scenario);
    }

    // ---- DirectoryPath property ----

    [Fact]
    public void DirectoryPath_Is_Set_To_Templates_Subdirectory()
    {
        Assert.Equal(_templatesDir, _manager.DirectoryPath);
    }

    // ---- ConflictResolution enum ----

    [Fact]
    public void ConflictResolution_Has_All_Values()
    {
        Assert.Equal(ConflictResolution.Rename, Enum.Parse<ConflictResolution>("Rename"));
        Assert.Equal(ConflictResolution.Skip, Enum.Parse<ConflictResolution>("Skip"));
        Assert.Equal(ConflictResolution.Replace, Enum.Parse<ConflictResolution>("Replace"));
    }

    // ---- Helper methods ----

    private static void CreateValidTemplateZip(string zipPath, string name, string description, string scenario)
    {
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var manifest = new
            {
                name,
                description,
                version = 1,
                scenario,
                skills = Array.Empty<string>(),
                memory_files = new[] { "user_profile.md" },
                tool_files = Array.Empty<string>()
            };
            var jsonEntry = zip.CreateEntry("template.json");
            using (var w = new StreamWriter(jsonEntry.Open())) w.Write(JsonSerializer.Serialize(manifest, JsonOpts));
            var memEntry = zip.CreateEntry("memory/user_profile.md");
            using (var mw = new StreamWriter(memEntry.Open()))
            {
                mw.WriteLine($"# {name} Profile");
                mw.WriteLine($"Description: {description}");
            }
        }
    }

    private static void CreateTemplateZipWithMemory(string zipPath, string name, string memoryContent)
    {
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var manifest = new
            {
                name,
                description = name + " template",
                version = 1,
                scenario = name.ToLowerInvariant(),
                skills = Array.Empty<string>(),
                memory_files = new[] { "user_profile.md" },
                tool_files = Array.Empty<string>()
            };
            var jsonEntry = zip.CreateEntry("template.json");
            using (var w = new StreamWriter(jsonEntry.Open())) w.Write(JsonSerializer.Serialize(manifest, JsonOpts));
            var memEntry = zip.CreateEntry("memory/user_profile.md");
            using (var mw = new StreamWriter(memEntry.Open())) mw.Write(memoryContent);
        }
    }
}
