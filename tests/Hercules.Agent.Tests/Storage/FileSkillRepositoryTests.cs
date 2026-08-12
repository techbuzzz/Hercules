using Hercules.Config;
using Hercules.Skills;
using Hercules.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Storage;

/// <summary>
///     Тесты FileSkillRepository: LoadAll, Load, Save, SaveNewVersion,
///     AppendUsage, LoadUsages, SaveRawMarkdown.
/// </summary>
public class FileSkillRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSkillRepository _repo;

    public FileSkillRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-fsr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var cfg = new StorageConfig { DataRoot = _tempDir };
        _repo = new FileSkillRepository(cfg, NullLogger<FileSkillRepository>.Instance);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    #region LoadAll

    [Fact]
    public void LoadAll_EmptyDirectory_ReturnsEmptyList()
    {
        var skills = _repo.LoadAll();

        Assert.Empty(skills);
    }

    [Fact]
    public void LoadAll_WithOneSkill_ReturnsOneSkill()
    {
        var skill = CreateSkill("test-skill", ["привет", "здравствуй"], "Ты — тестовый.");
        _repo.Save(skill);

        var skills = _repo.LoadAll();

        Assert.Single(skills);
        Assert.Equal("test-skill", skills[0].Meta.Id);
        Assert.Equal("Привет", skills[0].Meta.Name); // Name derived from "привет"
    }

    [Fact]
    public void LoadAll_SkipsCorruptMetaFile()
    {
        var badPath = Path.Combine(_repo.SkillsDirectory, "skill.bad.meta.json");
        File.WriteAllText(badPath, "not valid json {{{");
        var goodSkill = CreateSkill("good-skill", ["фраза"], "Промпт.");
        _repo.Save(goodSkill);

        var skills = _repo.LoadAll();

        Assert.Single(skills);
        Assert.Equal("good-skill", skills[0].Meta.Id);
    }

    #endregion

    #region Load

    [Fact]
    public void Load_ExistingSkill_ReturnsSkill()
    {
        var skill = CreateSkill("load-test", ["поиск"], "Ты — поисковик.");
        _repo.Save(skill);

        var loaded = _repo.Load("load-test");

        Assert.NotNull(loaded);
        Assert.Equal("load-test", loaded.Meta.Id);
        Assert.Equal("Поиск", loaded.Meta.Name); // Name derived from "поиск" → "Поиск"
        Assert.Equal("Ты — поисковик.", loaded.Prompt);
    }

    [Fact]
    public void Load_NonexistentSkill_ReturnsNull()
    {
        var result = _repo.Load("nonexistent-id");

        Assert.Null(result);
    }

    #endregion

    #region Save

    [Fact]
    public void Save_CreatesAllRequiredFiles()
    {
        var skill = CreateSkill("save-test", ["тест"], "Промпт теста.");

        _repo.Save(skill);

        var skillsDir = _repo.SkillsDirectory;
        Assert.True(File.Exists(Path.Combine(skillsDir, "skill.save-test.meta.json")));
        Assert.True(File.Exists(Path.Combine(skillsDir, "skill.save-test.md")));
        Assert.True(File.Exists(Path.Combine(skillsDir, "skill.save-test.prompt.md")));
        Assert.True(File.Exists(Path.Combine(skillsDir, "skill.save-test.v1.md")));
    }

    [Fact]
    public void Save_RoundTrip_PreservesAllFields()
    {
        var skill = CreateSkill("round-trip", ["круговой"], "Промпт круговой.");
        skill.Meta.SuccessRate = 0.85;

        _repo.Save(skill);
        var loaded = _repo.Load("round-trip");

        Assert.NotNull(loaded);
        Assert.Equal("round-trip", loaded.Meta.Id);
        Assert.Equal("Круговой", loaded.Meta.Name);
        Assert.Equal(0.85, loaded.Meta.SuccessRate);
        Assert.Equal("Промпт круговой.", loaded.Prompt);
    }

    #endregion

    #region SaveNewVersion

    [Fact]
    public void SaveNewVersion_IncrementsVersionAndSavesNewFiles()
    {
        var skill = CreateSkill("version-test", ["версия"], "Промпт v1.");
        _repo.Save(skill);
        Assert.Equal(1, skill.Meta.Version);

        _repo.SaveNewVersion(skill, "Новое описание.", "Промпт v2.");

        Assert.Equal(2, skill.Meta.Version);
        Assert.Equal("Новое описание.", skill.Description);
        Assert.Equal("Промпт v2.", skill.Prompt);

        // Version files: v1.md contains the description at time of v1 save ("# Навык\n\nОписание")
        var v1Path = Path.Combine(_repo.SkillsDirectory, "skill.version-test.v1.md");
        var v2Path = Path.Combine(_repo.SkillsDirectory, "skill.version-test.v2.md");
        Assert.True(File.Exists(v1Path), "v1 should be preserved");
        Assert.True(File.Exists(v2Path), "v2 should exist");
        Assert.Equal("# Навык\n\nОписание", File.ReadAllText(v1Path));
    }

    #endregion

    #region AppendUsage / LoadUsages

    [Fact]
    public void LoadUsages_EmptyFile_ReturnsEmptyList()
    {
        var usages = _repo.LoadUsages("any-id");

        Assert.Empty(usages);
    }

    [Fact]
    public void AppendUsage_AddsUsageAndUpdatesSuccessRate()
    {
        var skill = CreateSkill("usage-test", ["использование"], "Промпт.");
        _repo.Save(skill);

        _repo.AppendUsage("usage-test", new SkillUsage { Success = true, Confidence = "high" }, window: 10);
        _repo.AppendUsage("usage-test", new SkillUsage { Success = false, Confidence = "low" }, window: 10);
        _repo.AppendUsage("usage-test", new SkillUsage { Success = true, Confidence = "medium" }, window: 10);

        var usages = _repo.LoadUsages("usage-test");
        var updatedSkill = _repo.Load("usage-test");

        Assert.Equal(3, usages.Count);
        Assert.NotNull(updatedSkill);
        // 2/3 = 0.67, rounded to 2 decimal places
        Assert.Equal(0.67, updatedSkill.Meta.SuccessRate);
        Assert.Equal(3, updatedSkill.Meta.TotalUses);
    }

    [Fact]
    public void AppendUsage_EmptyWindow_ReturnsSuccessRate1()
    {
        var skill = CreateSkill("empty-window", ["окно"], "Промпт.");
        _repo.Save(skill);

        _repo.AppendUsage("empty-window", new SkillUsage { Success = false }, window: 0);

        var updatedSkill = _repo.Load("empty-window");
        Assert.NotNull(updatedSkill);
        Assert.Equal(1.0, updatedSkill.Meta.SuccessRate); // При window=0 возвращаем 1.0
    }

    #endregion

    #region SaveRawMarkdown

    [Fact]
    public void SaveRawMarkdown_CreatesFileInSkillsDirectory()
    {
        var fileName = "reflection-report-2026-08-12.md";
        var content = "# Рефлексия\n\nАгент хорошо справился.";

        _repo.SaveRawMarkdown(fileName, content);

        var path = Path.Combine(_repo.SkillsDirectory, fileName);
        Assert.True(File.Exists(path));
        Assert.Contains("Рефлексия", File.ReadAllText(path));
    }

    #endregion

    #region Helpers

    private static Skill CreateSkill(string id, IEnumerable<string> receivers, string prompt)
    {
        return new Skill
        {
            Meta = new SkillMeta
            {
                Id = id,
                Name = receivers.First().First().ToString().ToUpper() + receivers.First()[1..],
                Description = $"Навык по фразам: {string.Join(", ", receivers)}",
                PhraseReceivers = receivers.ToList(),
                Version = 1
            },
            Description = $"# Навык\n\nОписание",
            Prompt = prompt
        };
    }

    #endregion
}
