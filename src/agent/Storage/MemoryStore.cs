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

    public async Task<string> ReadProfileAsync(CancellationToken ct = default)
    {
        return await ReadOrDefaultAsync(ProfilePath,
            "# Профиль пользователя\n\n_Пока ничего не известно. Профиль наполняется по мере общения._\n", ct);
    }

    public string ReadProfile()
    {
        return ReadProfileAsync().GetAwaiter().GetResult();
    }

    public async Task<string> ReadPreferencesAsync(CancellationToken ct = default)
    {
        return await ReadOrDefaultAsync(PreferencesPath,
            "# Предпочтения\n\n- Язык: русский\n- Тон: дружелюбный, по делу\n", ct);
    }

    public string ReadPreferences()
    {
        return ReadPreferencesAsync().GetAwaiter().GetResult();
    }

    public async Task<string> ReadEntitiesAsync(CancellationToken ct = default)
    {
        return await ReadOrDefaultAsync(EntitiesPath,
            "# Известные сущности\n\n_Проекты, люди, компании появятся здесь._\n", ct);
    }

    public string ReadEntities()
    {
        return ReadEntitiesAsync().GetAwaiter().GetResult();
    }

    public async Task WriteProfileAsync(string content, CancellationToken ct = default)
    {
        await File.WriteAllTextAsync(ProfilePath, content, ct);
    }

    public void WriteProfile(string content)
    {
        WriteProfileAsync(content).GetAwaiter().GetResult();
    }

    public async Task WritePreferencesAsync(string content, CancellationToken ct = default)
    {
        await File.WriteAllTextAsync(PreferencesPath, content, ct);
    }

    public void WritePreferences(string content)
    {
        WritePreferencesAsync(content).GetAwaiter().GetResult();
    }

    public async Task WriteEntitiesAsync(string content, CancellationToken ct = default)
    {
        await File.WriteAllTextAsync(EntitiesPath, content, ct);
    }

    public void WriteEntities(string content)
    {
        WriteEntitiesAsync(content).GetAwaiter().GetResult();
    }

    /// <summary>Добавить блок к файлу профиля/сущностей/предпочтений.</summary>
    public async Task AppendAsync(string path, string markdownBlock, CancellationToken ct = default)
    {
        var prefix = File.Exists(path)
            ? "\n"
            : "";
        var content = prefix + markdownBlock.TrimEnd() + "\n";
        await File.AppendAllTextAsync(path, content, ct);
    }

    public void Append(string path, string markdownBlock)
    {
        AppendAsync(path, markdownBlock).GetAwaiter().GetResult();
    }

    /// <summary>Добавить запись контекста за текущий день.</summary>
    public async Task AppendContextAsync(string summary, DateOnly date, CancellationToken ct = default)
    {
        var path = ContextPath(date);
        var header = File.Exists(path)
            ? ""
            : $"# Контекст за {date:yyyy-MM-dd}\n\n";
        var entry = $"## Сессия {DateTime.Now:HH:mm:ss}\n\n{summary.TrimEnd()}\n\n";
        await File.AppendAllTextAsync(path, header + entry, ct);
    }

    public void AppendContext(string summary, DateOnly date)
    {
        AppendContextAsync(summary, date).GetAwaiter().GetResult();
    }

    /// <summary>Прочитать последний по дате файл контекста (для переноса между сессиями).</summary>
    public async Task<string> ReadLastContextAsync(CancellationToken ct = default)
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
            : await File.ReadAllTextAsync(files[0], ct);
    }

    public string ReadLastContext()
    {
        return ReadLastContextAsync().GetAwaiter().GetResult();
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

    private static async Task<string> ReadOrDefaultAsync(string path, string fallback, CancellationToken ct = default)
    {
        return File.Exists(path)
            ? await File.ReadAllTextAsync(path, ct)
            : fallback;
    }
}
