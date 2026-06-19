using Hercules.Config;

namespace Hercules.Storage;

/// <summary>
///     Файловое хранилище долговременной памяти (папка Memory/).
///     Все данные хранятся в Markdown:
///     user_profile.md     — модель пользователя
///     preferences.md      — предпочтения
///     entities.md         — известные сущности
///     context_{date}.md   — краткий контекст по сессиям за день
/// </summary>
public sealed class MemoryStore
{
    private const string ProfileFile = "user_profile.md";
    private const string PreferencesFile = "preferences.md";
    private const string EntitiesFile = "entities.md";

    private readonly string _dir;

    public MemoryStore(StorageConfig cfg)
    {
        _dir = Path.Combine(cfg.DataRoot, cfg.MemoryDir);
        Directory.CreateDirectory(_dir);
    }


    public string ProfilePath => Path_(ProfileFile);
    public string PreferencesPath => Path_(PreferencesFile);
    public string EntitiesPath => Path_(EntitiesFile);

    private string Path_(string file)
    {
        return Path.Combine(_dir, file);
    }

    private string ContextPath(DateOnly date)
    {
        return Path_($"context_{date:yyyy-MM-dd}.md");
    }

    public string ReadProfile()
    {
        return ReadOrDefault(ProfilePath,
            "# Профиль пользователя\n\n_Пока ничего не известно. Профиль наполняется по мере общения._\n");
    }

    public string ReadPreferences()
    {
        return ReadOrDefault(PreferencesPath,
            "# Предпочтения\n\n- Язык: русский\n- Тон: дружелюбный, по делу\n");
    }

    public string ReadEntities()
    {
        return ReadOrDefault(EntitiesPath,
            "# Известные сущности\n\n_Проекты, люди, компании появятся здесь._\n");
    }

    public void WriteProfile(string content)
    {
        File.WriteAllText(ProfilePath, content);
    }

    public void WritePreferences(string content)
    {
        File.WriteAllText(PreferencesPath, content);
    }

    public void WriteEntities(string content)
    {
        File.WriteAllText(EntitiesPath, content);
    }

    /// <summary>Добавить блок к файлу профиля/сущностей/предпочтений.</summary>
    public void Append(string path, string markdownBlock)
    {
        var prefix = File.Exists(path)
            ? "\n"
            : "";
        File.AppendAllText(path, prefix + markdownBlock.TrimEnd() + "\n");
    }

    /// <summary>Добавить запись контекста за текущий день.</summary>
    public void AppendContext(string summary, DateOnly date)
    {
        var path = ContextPath(date);
        var header = File.Exists(path)
            ? ""
            : $"# Контекст за {date:yyyy-MM-dd}\n\n";
        var entry = $"## Сессия {DateTime.Now:HH:mm:ss}\n\n{summary.TrimEnd()}\n\n";
        File.AppendAllText(path, header + entry);
    }

    /// <summary>Прочитать последний по дате файл контекста (для переноса между сессиями).</summary>
    public string ReadLastContext()
    {
        if (!Directory.Exists(_dir))
        {
            return "";
        }

        var files = Directory.EnumerateFiles(_dir, "context_*.md")
            .OrderByDescending(f => f)
            .ToList();
        return files.Count == 0
            ? ""
            : File.ReadAllText(files[0]);
    }

    /// <summary>Полностью очистить память.</summary>
    public void Reset()
    {
        if (!Directory.Exists(_dir))
        {
            return;
        }

        foreach (var f in Directory.EnumerateFiles(_dir, "*.md"))
        {
            File.Delete(f);
        }
    }

    private static string ReadOrDefault(string path, string fallback)
    {
        return File.Exists(path)
            ? File.ReadAllText(path)
            : fallback;
    }
}
