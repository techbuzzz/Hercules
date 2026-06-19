using System.Text.Encodings.Web;
using System.Text.Json;
using Hercules.Config;

namespace Hercules.Storage;

/// <summary>
///     Файловое хранилище навыков в папке Skills/.
///     Раскладка файлов на навык {id}:
///     skill.{id}.meta.json   — метаданные
///     skill.{id}.md          — описание (текущая версия)
///     skill.{id}.prompt.md   — system prompt
///     skill.{id}.usage.json  — лог использования (массив)
///     skill.{id}.v{N}.md     — исторические версии описания
/// </summary>
public sealed class FileSkillRepository
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        // Не экранировать кириллицу — файлы метаданных читаются человеком
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _dir;

    public FileSkillRepository(StorageConfig cfg)
    {
        _dir = Path.Combine(cfg.DataRoot, cfg.SkillsDir);
        Directory.CreateDirectory(_dir);
    }

    private string MetaPath(string id)
    {
        return Path.Combine(_dir, $"skill.{id}.meta.json");
    }

    private string DescPath(string id)
    {
        return Path.Combine(_dir, $"skill.{id}.md");
    }

    private string PromptPath(string id)
    {
        return Path.Combine(_dir, $"skill.{id}.prompt.md");
    }

    private string UsagePath(string id)
    {
        return Path.Combine(_dir, $"skill.{id}.usage.json");
    }

    private string VersionPath(string id, int v)
    {
        return Path.Combine(_dir, $"skill.{id}.v{v}.md");
    }

    /// <summary>Загрузить все навыки из папки.</summary>
    public List<Skill> LoadAll()
    {
        var skills = new List<Skill>();
        if (!Directory.Exists(_dir))
        {
            return skills;
        }

        foreach (var metaFile in Directory.EnumerateFiles(_dir, "skill.*.meta.json"))
        {
            try
            {
                SkillMeta? meta = JsonSerializer.Deserialize<SkillMeta>(File.ReadAllText(metaFile), JsonOpts);
                if (meta is null)
                {
                    continue;
                }

                skills.Add(new Skill
                {
                    Meta = meta,
                    Description = ReadIfExists(DescPath(meta.Id)),
                    Prompt = ReadIfExists(PromptPath(meta.Id))
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Skills] Не удалось загрузить {metaFile}: {ex.Message}");
            }
        }

        return skills;
    }

    public Skill? Load(string id)
    {
        var path = MetaPath(id);
        if (!File.Exists(path))
        {
            return null;
        }

        SkillMeta? meta = JsonSerializer.Deserialize<SkillMeta>(File.ReadAllText(path), JsonOpts);
        if (meta is null)
        {
            return null;
        }

        return new Skill
        {
            Meta = meta,
            Description = ReadIfExists(DescPath(id)),
            Prompt = ReadIfExists(PromptPath(id))
        };
    }

    /// <summary>Сохранить новый навык (версия 1).</summary>
    public void Save(Skill skill)
    {
        File.WriteAllText(MetaPath(skill.Meta.Id), JsonSerializer.Serialize(skill.Meta, JsonOpts));
        File.WriteAllText(DescPath(skill.Meta.Id), skill.Description);
        File.WriteAllText(PromptPath(skill.Meta.Id), skill.Prompt);
        File.WriteAllText(VersionPath(skill.Meta.Id, skill.Meta.Version), skill.Description);
    }

    /// <summary>
    ///     Сохранить улучшенную версию навыка. Старые версии НЕ удаляются
    ///     (создаётся skill.{id}.v{N+1}.md), увеличивается номер версии.
    /// </summary>
    public void SaveNewVersion(Skill skill, string newDescription, string newPrompt)
    {
        skill.Meta.Version += 1;
        skill.Description = newDescription;
        skill.Prompt = newPrompt;
        File.WriteAllText(MetaPath(skill.Meta.Id), JsonSerializer.Serialize(skill.Meta, JsonOpts));
        File.WriteAllText(DescPath(skill.Meta.Id), newDescription);
        File.WriteAllText(PromptPath(skill.Meta.Id), newPrompt);
        File.WriteAllText(VersionPath(skill.Meta.Id, skill.Meta.Version), newDescription);
    }

    /// <summary>Добавить запись об использовании и пересчитать success_rate.</summary>
    public void AppendUsage(string id, SkillUsage usage, int window)
    {
        List<SkillUsage> usages = LoadUsages(id);
        usages.Add(usage);
        File.WriteAllText(UsagePath(id), JsonSerializer.Serialize(usages, JsonOpts));

        Skill? skill = Load(id);
        if (skill is null)
        {
            return;
        }

        var recent = usages.TakeLast(window).ToList();
        skill.Meta.TotalUses = usages.Count;
        skill.Meta.SuccessRate = recent.Count == 0
            ? 1.0
            : Math.Round(recent.Count(u => u.Success) / (double)recent.Count, 2);
        File.WriteAllText(MetaPath(id), JsonSerializer.Serialize(skill.Meta, JsonOpts));
    }

    public List<SkillUsage> LoadUsages(string id)
    {
        var path = UsagePath(id);
        if (!File.Exists(path))
        {
            return new List<SkillUsage>();
        }

        return JsonSerializer.Deserialize<List<SkillUsage>>(File.ReadAllText(path), JsonOpts) ?? new List<SkillUsage>();
    }

    /// <summary>Сохранить произвольный markdown-файл в папку Skills (например, отчёт рефлексии).</summary>
    public void SaveRawMarkdown(string fileName, string content)
    {
        File.WriteAllText(Path.Combine(_dir, fileName), content);
    }

    private static string ReadIfExists(string path)
    {
        return File.Exists(path)
            ? File.ReadAllText(path)
            : "";
    }
}
