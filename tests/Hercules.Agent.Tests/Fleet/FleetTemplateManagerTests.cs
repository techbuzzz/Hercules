using System.IO.Compression;
using Hercules.Config;
using Hercules.Fleet;
using Hercules.Skills;
using Hercules.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Fleet;

/// <summary>
///     Тесты FleetTemplateManager (task_062): list, get manifest, apply fleet templates.
/// </summary>
public sealed class FleetTemplateManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _templatesDir;
    private readonly string _fleetTemplatesDir;
    private readonly FleetTemplateManager _manager;

    public FleetTemplateManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-fleet-tests-{Guid.NewGuid():N}");
        _templatesDir = Path.Combine(_tempDir, "Templates");
        _fleetTemplatesDir = Path.Combine(_tempDir, "FleetTemplates");
        Directory.CreateDirectory(_templatesDir);
        Directory.CreateDirectory(_fleetTemplatesDir);

        var storageConfig = new StorageConfig { DataRoot = _tempDir };
        var phase2Config = new Phase2Config
        {
            TemplatesDir = "Templates",
            FleetTemplatesDir = "FleetTemplates"
        };
        var appConfig = new AppConfig { Phase2 = phase2Config };

        // Create a minimal agent template manager (needed for fleet template manager constructor)
        var fileSkillRepoLoggerMock = new Mock<ILogger<FileSkillRepository>>();
        var fileSkillRepo = new FileSkillRepository(storageConfig, fileSkillRepoLoggerMock.Object);
        var packager = new SkillPackager(fileSkillRepo);

        var agentTemplateManager = new AgentTemplateManager(storageConfig, packager);
        _manager = new FleetTemplateManager(storageConfig, agentTemplateManager);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    // ---- List tests ----

    [Fact]
    public void List_Returns_Empty_When_No_Templates()
    {
        var result = _manager.List();
        Assert.Empty(result);
    }

    [Fact]
    public void List_Returns_Entry_For_Valid_Zip()
    {
        CreateFleetTemplateZip("greenhouse.fleettemplate", "Greenhouse Assistant Fleet", "greenhouse");
        var result = _manager.List();
        Assert.Single(result);
        Assert.Equal("greenhouse.fleettemplate", result[0].FileName);
        Assert.Equal("Greenhouse Assistant Fleet", result[0].Name);
        Assert.Equal("greenhouse", result[0].Vertical);
    }

    [Fact]
    public void List_Skips_Invalid_Zip_Files()
    {
        CreateFleetTemplateZip("greenhouse.fleettemplate", "Greenhouse", "greenhouse");
        CreateInvalidZip("broken.fleettemplate");
        var result = _manager.List();
        Assert.Single(result);
        Assert.Equal("greenhouse.fleettemplate", result[0].FileName);
    }

    [Fact]
    public void List_Returns_Multiple_Entries()
    {
        CreateFleetTemplateZip("greenhouse.fleettemplate", "Greenhouse", "greenhouse");
        CreateFleetTemplateZip("cold-chain.fleettemplate", "Cold Chain", "cold-chain");
        CreateFleetTemplateZip("vending.fleettemplate", "Vending", "vending");
        var result = _manager.List();
        Assert.Equal(3, result.Count);
    }

    // ---- GetManifest tests ----

    [Fact]
    public void GetManifest_Throws_When_File_Not_Found()
    {
        Assert.Throws<FileNotFoundException>(() => _manager.GetManifest("nonexistent.fleettemplate"));
    }

    [Fact]
    public void GetManifest_Returns_Correct_Manifest()
    {
        CreateFleetTemplateZip("greenhouse.fleettemplate", "Greenhouse Fleet", "greenhouse");
        var manifest = _manager.GetManifest("greenhouse.fleettemplate");
        Assert.Equal("Greenhouse Fleet", manifest.Name);
        Assert.Equal("greenhouse", manifest.Vertical);
        Assert.Equal(1, manifest.Version);
    }

    // ---- Apply tests ----

    [Fact]
    public void Apply_Extracts_Fleet_Config_Files()
    {
        CreateFleetTemplateZip("greenhouse.fleettemplate", "Greenhouse", "greenhouse");
        CreateDummyAgentTemplate("greenhouse.agenttemplate");
        var result = _manager.Apply("greenhouse.fleettemplate");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors));
        Assert.Equal("Greenhouse", result.TemplateName);
        Assert.Contains(result.InstalledConfigFiles, f => f == "monitoring.json");
        Assert.Contains(result.InstalledConfigFiles, f => f == "policy.json");
        Assert.Contains(result.InstalledConfigFiles, f => f == "offline.json");
        Assert.Contains(result.InstalledConfigFiles, f => f == "hardware-bom.json");
    }

    [Fact]
    public void Apply_Sets_HasErrors_False_When_Successful()
    {
        CreateFleetTemplateZip("greenhouse.fleettemplate", "Greenhouse", "greenhouse");
        // Pre-create the agent template so the apply doesn't fail on missing reference
        CreateDummyAgentTemplate("greenhouse.agenttemplate");
        var result = _manager.Apply("greenhouse.fleettemplate");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors));
    }

    [Fact]
    public void Apply_Adds_Error_When_Template_Not_Found()
    {
        var result = _manager.Apply("nonexistent.fleettemplate");
        Assert.True(result.HasErrors);
        Assert.Single(result.Errors);
        Assert.Contains("not found", result.Errors[0]);
    }

    [Fact]
    public void Apply_Renames_Existing_Config_When_Conflict()
    {
        // Pre-create monitoring.json
        var existingPath = Path.Combine(_tempDir, "monitoring.json");
        File.WriteAllText(existingPath, "{}");

        CreateFleetTemplateZip("greenhouse.fleettemplate", "Greenhouse", "greenhouse");
        CreateDummyAgentTemplate("greenhouse.agenttemplate");
        var result = _manager.Apply("greenhouse.fleettemplate", FleetConflictResolution.Rename);

        Assert.False(result.HasErrors, string.Join("; ", result.Errors));
        Assert.True(File.Exists(existingPath + ".bak"), "Existing config should be backed up");
        Assert.True(File.Exists(existingPath), "New config should be written");
    }

    [Fact]
    public void Apply_Skips_Existing_Config_When_Skip()
    {
        var existingPath = Path.Combine(_tempDir, "monitoring.json");
        File.WriteAllText(existingPath, "{\"original\":true}");

        CreateFleetTemplateZip("greenhouse.fleettemplate", "Greenhouse", "greenhouse");
        CreateDummyAgentTemplate("greenhouse.agenttemplate");
        var result = _manager.Apply("greenhouse.fleettemplate", FleetConflictResolution.Skip);

        Assert.False(result.HasErrors, string.Join("; ", result.Errors));
        var content = File.ReadAllText(existingPath);
        Assert.Contains("original", content); // Should still be original
    }

    [Fact]
    public void Apply_Fails_When_Conflict_And_Fail_Strategy()
    {
        var existingPath = Path.Combine(_tempDir, "monitoring.json");
        File.WriteAllText(existingPath, "{}");

        CreateFleetTemplateZip("greenhouse.fleettemplate", "Greenhouse", "greenhouse");
        var result = _manager.Apply("greenhouse.fleettemplate", FleetConflictResolution.Fail);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Contains("monitoring.json"));
    }

    [Fact]
    public void GetFleetTemplatesDir_Returns_Configured_Directory()
    {
        Assert.Equal(_fleetTemplatesDir, _manager.GetFleetTemplatesDir());
    }

    // ---- FleetTemplateEntry record tests ----

    [Fact]
    public void FleetTemplateEntry_Contains_All_Fields()
    {
        CreateFleetTemplateZip("greenhouse.fleettemplate", "Greenhouse Assistant Fleet", "greenhouse");
        var entries = _manager.List();
        var entry = entries[0];
        Assert.Equal("greenhouse.fleettemplate", entry.FileName);
        Assert.Equal("Greenhouse Assistant Fleet", entry.Name);
        Assert.Equal("greenhouse", entry.Vertical);
        Assert.Equal(1, entry.Version);
        Assert.EndsWith("greenhouse.fleettemplate", entry.FilePath);
    }

    // ---- FleetManifest default values ----

    [Fact]
    public void FleetManifest_Default_Values()
    {
        var manifest = new FleetManifest();
        Assert.Equal("", manifest.Name);
        Assert.Equal(1, manifest.Version); // Class defines default = 1
        Assert.Equal("", manifest.Vertical);
        Assert.NotNull(manifest.HardwareBom);
        Assert.NotNull(manifest.Monitoring);
        Assert.NotNull(manifest.Policy);
        Assert.NotNull(manifest.Offline);
    }

    [Fact]
    public void ApplyFleetTemplateResult_Default_HasEmptyLists()
    {
        var result = new ApplyFleetTemplateResult();
        Assert.NotNull(result.Errors);
        Assert.NotNull(result.InstalledConfigFiles);
        Assert.False(result.HasErrors);
        Assert.False(result.AgentTemplateApplied);
    }

    // ---- Helper methods ----

    private void CreateFleetTemplateZip(string fileName, string name, string vertical)
    {
        var zipPath = Path.Combine(_fleetTemplatesDir, fileName);
        using var zipStream = File.Create(zipPath);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);

        var manifest = $@"{{
  ""name"": ""{name}"",
  ""description"": ""Test fleet template"",
  ""version"": 1,
  ""vertical"": ""{vertical}"",
  ""agent_template_file"": ""{vertical}.agenttemplate""
}}";
        AddEntry(archive, "fleet-manifest.json", manifest);

        AddEntry(archive, "monitoring.json", @"{""min_log_level"": ""Information"", ""metric_thresholds"": {}, ""alert_rules"": []}");
        AddEntry(archive, "policy.json", @"{""max_agents"": 10, ""max_delegation_hops"": 3}");
        AddEntry(archive, "offline.json", @"{""default_degradation_mode"": ""Degraded"", ""allowed_offline_skills"": []}");
        AddEntry(archive, "hardware-bom.json", @"{""cpu"": """", ""ram_gb"": 4, ""storage_gb"": 32}");
    }

    private void CreateInvalidZip(string fileName)
    {
        var path = Path.Combine(_fleetTemplatesDir, fileName);
        using var fs = File.Create(path);
        using var writer = new StreamWriter(fs);
        writer.Write("this is not a valid zip");
    }

    private void CreateDummyAgentTemplate(string fileName)
    {
        // Create a minimal valid .agenttemplate ZIP so Apply() finds it in Templates/
        var path = Path.Combine(_templatesDir, fileName);
        using var zipStream = File.Create(path);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("template.json", CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(@"{
  ""name"": ""Dummy"",
  ""description"": ""Test"",
  ""version"": 1,
  ""skills"": [],
  ""memory_files"": [],
  ""tool_files"": []
}");
    }

    private static void AddEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }
}
