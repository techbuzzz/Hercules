using Hercules.Config;
using Hercules.Skills;
using Hercules.Storage;
using Xunit;

namespace Hercules.Agent.Tests.Phase2Tests;

public class SkillMarketplaceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSkillRepository _repo;
    private readonly SkillPackager _packager;
    private readonly SkillManager _skillManager;
    private readonly SkillMarketplace _marketplace;

    public SkillMarketplaceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-mkt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var storageCfg = new StorageConfig { DataRoot = _tempDir };
        _repo = new FileSkillRepository(storageCfg);
        _packager = new SkillPackager(_repo);
        _skillManager = new SkillManager(_repo, new StubLlm("test"), new AgentConfig());
        _marketplace = new SkillMarketplace(storageCfg, _packager);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    [Fact]
    public void List_Returns_Empty_For_New_Marketplace()
    {
        var entries = _marketplace.List();
        Assert.Empty(entries);
    }

    [Fact]
    public void Publish_Adds_Package_To_Marketplace()
    {
        var skill = _skillManager.CreateManual("Маркетплейс-тест", ["тест"], "Промпт");
        var packagePath = _packager.Export(skill.Meta.Id);

        _marketplace.Publish(packagePath);

        var entries = _marketplace.List();
        Assert.Single(entries);
        Assert.Equal("Маркетплейс-тест", entries[0].Name);
    }

    [Fact]
    public void Install_From_Marketplace_Imports_Skill()
    {
        var skill = _skillManager.CreateManual("Устанавливаемый", ["установка"], "Промпт");
        var packagePath = _packager.Export(skill.Meta.Id);
        var fileName = Path.GetFileName(packagePath);
        _marketplace.Publish(packagePath);

        var installed = _marketplace.Install(fileName, ConflictResolution.Rename);

        Assert.NotNull(installed);
        Assert.Equal("Устанавливаемый", installed.Meta.Name);
        Assert.NotEqual(skill.Meta.Id, installed.Meta.Id); // Rename → новый ID
    }

    [Fact]
    public void Search_Finds_Skills_By_Name()
    {
        var skill = _skillManager.CreateManual("Погода", ["погода"], "Промпт");
        _marketplace.Publish(_packager.Export(skill.Meta.Id));

        var results = _marketplace.Search("погод");
        Assert.NotEmpty(results);
        Assert.Contains(results, e => e.Name == "Погода");
    }

    [Fact]
    public void Remove_Deletes_Package_From_Marketplace()
    {
        var skill = _skillManager.CreateManual("Удаляемый", ["удалить"], "Промпт");
        var packagePath = _packager.Export(skill.Meta.Id);
        var fileName = Path.GetFileName(packagePath);
        _marketplace.Publish(packagePath);

        var removed = _marketplace.Remove(fileName);
        Assert.True(removed);
        Assert.Empty(_marketplace.List());
    }

    [Fact]
    public void Install_Throws_For_Nonexistent_File()
    {
        Assert.Throws<FileNotFoundException>(() => _marketplace.Install("nonexistent.skillpkg"));
    }
}