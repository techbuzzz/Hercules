using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using Hercules.Storage;

namespace Hercules.Skills;

/// <summary>
///     Экспорт/импорт пакетов навыков. Пакет — это ZIP-архив (.skillpkg) или папка
///     с фиксированным набором файлов:
///     <list type="bullet">
///         <item>skill.package.json — манифест пакета (обязательный)</item>
///         <item>skill.description.md — описание навыка</item>
///         <item>skill.prompt.md — system prompt</item>
///         <item>skill.tests.json — тесты (опционально)</item>
///         <item>skill.usage.json — история использования (опционально, для анализа)</item>
///         <item>tool.schema.json — декларация инструментов (опционально)</item>
///     </list>
///     Экспорт сохраняет навык в переносимый формат; импорт разворачивает пакет
///     в data/Skills/ с возможностью conflict-разрешения (replace | skip | rename).
/// </summary>
public sealed class SkillPackager
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly FileSkillRepository _repo;

    public SkillPackager(FileSkillRepository repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }

    /// <summary>Имя файла-пакета по умолчанию: {id}-v{version}.skillpkg.</summary>
    public static string PackageFileName(string skillId, int version)
    {
        return $"{skillId}-v{version}.skillpkg";
    }

    /// <summary>
    ///     Экспортировать навык в ZIP-пакет (.skillpkg).
    ///     Возвращает путь к созданному файлу.
    /// </summary>
    /// <param name="skillId">ID навыка для экспорта.</param>
    /// <param name="outputDir">Директория для сохранения пакета. Если null — используется data/Skills/exports/.</param>
    /// <param name="includeUsage">
    ///     Включить историю использования (для анализа). По умолчанию false — пакеты для
    ///     распространения не содержат usage.
    /// </param>
    public string Export(string skillId, string? outputDir = null, bool includeUsage = false)
    {
        Skill? skill = _repo.Load(skillId);
        if (skill is null)
        {
            throw new InvalidOperationException($"Навык '{skillId}' не найден.");
        }

        var outDir = outputDir ?? Path.Combine(_repo.SkillsDirectory, "exports");
        Directory.CreateDirectory(outDir);

        var fileName = PackageFileName(skill.Meta.Id, skill.Meta.Version);
        var packagePath = Path.Combine(outDir, fileName);

        using FileStream archiveStream = File.Create(packagePath);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create);

        // 1. Манифест пакета
        var manifest = new SkillPackageManifest
        {
            PackageVersion = 1,
            Skill = new SkillPackageSkillMeta
            {
                Id = skill.Meta.Id,
                Name = skill.Meta.Name,
                Description = skill.Meta.Description,
                PhraseReceivers = skill.Meta.PhraseReceivers,
                Version = skill.Meta.Version,
                CreatedAt = skill.Meta.CreatedAt
            },
            CreatedAt = DateTime.UtcNow.ToString("o")
        };
        WriteEntry(archive, "skill.package.json", JsonSerializer.Serialize(manifest, JsonOpts));

        // 2. Описание (markdown)
        WriteEntry(archive, "skill.description.md", skill.Description);

        // 3. System prompt
        WriteEntry(archive, "skill.prompt.md", skill.Prompt);

        // 4. История использования (опционально)
        if (includeUsage)
        {
            List<SkillUsage> usages = _repo.LoadUsages(skillId);
            if (usages.Count > 0)
            {
                WriteEntry(archive, "skill.usage.json", JsonSerializer.Serialize(usages, JsonOpts));
            }
        }

        return packagePath;
    }

    /// <summary>
    ///     Импортировать навык из ZIP-пакета (.skillpkg) или из папки.
    ///     Если навык с таким ID уже существует — применяется стратегия conflictResolution.
    /// </summary>
    /// <param name="packagePath">Путь к .skillpkg-файлу или папке с распакованным пакетом.</param>
    /// <param name="conflictResolution">replace | skip | rename. По умолчанию — rename (добавляет суффикс).</param>
    /// <returns>Импортированный навык.</returns>
    public Skill Import(string packagePath, ConflictResolution conflictResolution = ConflictResolution.Rename)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            throw new ArgumentException("Путь к пакету не указан.", nameof(packagePath));
        }

        SkillPackageManifest manifest;
        string description;
        string prompt;
        SkillTestSuite? tests = null;
        List<ToolDeclaration>? tools = null;

        if (Directory.Exists(packagePath))
        {
            // Импорт из папки
            (manifest, description, prompt, tests, tools) = ReadFromDirectory(packagePath);
        }
        else if (File.Exists(packagePath))
        {
            // Импорт из ZIP-архива
            (manifest, description, prompt, tests, tools) = ReadFromZip(packagePath);
        }
        else
        {
            throw new FileNotFoundException($"Пакет не найден: {packagePath}");
        }

        // Разрешение конфликта ID
        var skillId = manifest.Skill.Id;
        Skill? existing = _repo.Load(skillId);
        if (existing is not null)
        {
            switch (conflictResolution)
            {
                case ConflictResolution.Skip:
                    return existing;
                case ConflictResolution.Rename:
                    skillId = GenerateUniqueId(skillId);
                    break;
                case ConflictResolution.Replace:
                    // Удаляем существующие файлы (будут перезаписаны Save)
                    break;
            }
        }

        var skill = new Skill
        {
            Meta = new SkillMeta
            {
                Id = skillId,
                Name = manifest.Skill.Name,
                Description = manifest.Skill.Description,
                PhraseReceivers = manifest.Skill.PhraseReceivers,
                Version = manifest.Skill.Version,
                CreatedAt = manifest.Skill.CreatedAt
            },
            Description = description,
            Prompt = prompt
        };
        _repo.Save(skill);
        return skill;
    }

    /// <summary>
    ///     Проверить валидность пакета без импорта. Возвращает список ошибок (пустой = OK).
    /// </summary>
    public List<string> Validate(string packagePath)
    {
        var errors = new List<string>();

        try
        {
            SkillPackageManifest manifest;
            string description;
            string prompt;

            if (Directory.Exists(packagePath))
            {
                (manifest, description, prompt, _, _) = ReadFromDirectory(packagePath);
            }
            else if (File.Exists(packagePath))
            {
                (manifest, description, prompt, _, _) = ReadFromZip(packagePath);
            }
            else
            {
                errors.Add($"Пакет не найден: {packagePath}");
                return errors;
            }

            if (string.IsNullOrWhiteSpace(manifest.Skill.Id))
            {
                errors.Add("Манифест: skill.id пуст.");
            }

            if (string.IsNullOrWhiteSpace(manifest.Skill.Name))
            {
                errors.Add("Манифест: skill.name пуст.");
            }

            if (manifest.Skill.PhraseReceivers.Count == 0)
            {
                errors.Add("Манифест: phrase_receivers пуст — навык не сможет маршрутизироваться.");
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                errors.Add("skill.description.md пуст или отсутствует.");
            }

            if (string.IsNullOrWhiteSpace(prompt))
            {
                errors.Add("skill.prompt.md пуст или отсутствует.");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Ошибка чтения пакета: {ex.Message}");
        }

        return errors;
    }

    private static (SkillPackageManifest, string, string, SkillTestSuite?, List<ToolDeclaration>?) ReadFromZip(string zipPath)
    {
        using FileStream archiveStream = File.OpenRead(zipPath);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
        return ReadEntries(
            ReadEntry(archive, "skill.package.json"),
            ReadEntry(archive, "skill.description.md"),
            ReadEntry(archive, "skill.prompt.md"),
            ReadEntryOptional(archive, "skill.tests.json"),
            ReadEntryOptional(archive, "tool.schema.json"));
    }

    private static (SkillPackageManifest, string, string, SkillTestSuite?, List<ToolDeclaration>?) ReadFromDirectory(string dir)
    {
        return ReadEntries(
            File.ReadAllText(Path.Combine(dir, "skill.package.json")),
            File.ReadAllText(Path.Combine(dir, "skill.description.md")),
            File.ReadAllText(Path.Combine(dir, "skill.prompt.md")),
            File.Exists(Path.Combine(dir, "skill.tests.json"))
                ? File.ReadAllText(Path.Combine(dir, "skill.tests.json"))
                : null,
            File.Exists(Path.Combine(dir, "tool.schema.json"))
                ? File.ReadAllText(Path.Combine(dir, "tool.schema.json"))
                : null);
    }

    private static (SkillPackageManifest, string, string, SkillTestSuite?, List<ToolDeclaration>?) ReadEntries(
        string manifestJson, string description, string prompt,
        string? testsJson, string? toolsJson)
    {
        SkillPackageManifest manifest = JsonSerializer.Deserialize<SkillPackageManifest>(manifestJson, JsonOpts) ?? throw new InvalidOperationException("Не удалось десериализовать skill.package.json");
        SkillTestSuite? tests = testsJson is not null
            ? JsonSerializer.Deserialize<SkillTestSuite>(testsJson, JsonOpts)
            : null;
        List<ToolDeclaration>? tools = toolsJson is not null
            ? JsonSerializer.Deserialize<List<ToolDeclaration>>(toolsJson, JsonOpts)
            : null;
        return (manifest, description, prompt, tests, tools);
    }

    private static string ReadEntry(ZipArchive archive, string entryName)
    {
        ZipArchiveEntry entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException($"Обязательный файл отсутствует в пакете: {entryName}");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static string? ReadEntryOptional(ZipArchive archive, string entryName)
    {
        ZipArchiveEntry? entry = archive.GetEntry(entryName);
        if (entry is null)
        {
            return null;
        }

        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private string GenerateUniqueId(string baseId)
    {
        var suffix = Random.Shared.Next(1000, 9999).ToString();
        var newId = $"{baseId}-{suffix}";
        while (_repo.Load(newId) is not null)
        {
            suffix = Random.Shared.Next(1000, 9999).ToString();
            newId = $"{baseId}-{suffix}";
        }

        return newId;
    }
}

/// <summary>Стратегия разрешения конфликта ID при импорте навыка.</summary>
public enum ConflictResolution
{
    /// <summary>Перезаписать существующий навык (старые версии сохраняются как .v{N}.md).</summary>
    Replace,

    /// <summary>Пропустить импорт, вернуть существующий навык.</summary>
    Skip,

    /// <summary>Сгенерировать новый ID (добавить суффикс) и импортировать как новый навык.</summary>
    Rename
}
