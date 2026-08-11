using System.IO.Compression;
using System.Text.Json;
using Hercules.Config;
using Hercules.Storage;

namespace Hercules.Skills;

/// <summary>
///     Шаблон агента: bundle из нескольких навыков + начальной памяти + конфигурации инструментов.
///     Позволяет развернуть агента для вертикального сценария (теплица, холодовая цепь, серверная, вендинг)
///     одной командой вместо ручного создания каждого навыка.
///     Формат — ZIP-архив .agenttemplate со структурой:
///     <list type="bullet">
///         <item>template.json — манифест шаблона (обязательный)</item>
///         <item>skills/*.skillpkg — пакеты навыков, входящие в шаблон</item>
///         <item>memory/*.md — начальные файлы памяти (user_profile.md, preferences.md, entities.md)</item>
///         <item>tools/*.json — декларации инструментов</item>
///     </list>
/// </summary>
public sealed class AgentTemplateManager
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly SkillPackager _packager;
    private readonly StorageConfig _storageCfg;

    public AgentTemplateManager(StorageConfig cfg, SkillPackager packager)
    {
        _storageCfg = cfg;
        var templatesSubdir = cfg.Phase2?.TemplatesDir ?? "Templates";
        DirectoryPath = Path.Combine(cfg.DataRoot, templatesSubdir);
        Directory.CreateDirectory(DirectoryPath);
        _packager = packager ?? throw new ArgumentNullException(nameof(packager));
    }

    /// <summary>Каталог шаблонов (data/Templates/).</summary>
    public string DirectoryPath { get; }

    /// <summary>Список доступных шаблонов.</summary>
    public List<TemplateEntry> List()
    {
        var entries = new List<TemplateEntry>();
        foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*.agenttemplate"))
        {
            try
            {
                TemplateManifest manifest = ReadTemplateManifest(file);
                entries.Add(new TemplateEntry(
                    Path.GetFileName(file),
                    manifest.Name,
                    manifest.Description,
                    manifest.Version,
                    manifest.Skills.Count,
                    file));
            }
            catch
            {
                /* skip invalid */
            }
        }

        return entries;
    }

    /// <summary>
    ///     Применить шаблон: импортировать все навыки, скопировать начальную память, инструменты.
    /// </summary>
    public ApplyTemplateResult Apply(string fileName, ConflictResolution conflict = ConflictResolution.Rename)
    {
        var path = Path.Combine(DirectoryPath, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Шаблон '{fileName}' не найден.");
        }

        TemplateManifest manifest = ReadTemplateManifest(path);
        var result = new ApplyTemplateResult { TemplateName = manifest.Name };

        using FileStream archiveStream = File.OpenRead(path);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);

        // 1. Импорт навыков
        foreach (var skillFile in manifest.Skills)
        {
            ZipArchiveEntry? entry = archive.GetEntry($"skills/{skillFile}");
            if (entry is null)
            {
                result.Errors.Add($"Навык '{skillFile}' не найден в архиве шаблона.");
                continue;
            }

            var tempPath = Path.Combine(Path.GetTempPath(), $"hercules-template-{Guid.NewGuid():N}.skillpkg");
            try
            {
                using Stream entryStream = entry.Open();
                using FileStream fileStream = File.Create(tempPath);
                entryStream.CopyTo(fileStream);

                Skill skill = _packager.Import(tempPath, conflict);
                result.InstalledSkills.Add(skill.Meta.Name);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Ошибка импорта навыка '{skillFile}': {ex.Message}");
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    /* best effort */
                }
            }
        }

        // 2. Копирование начальной памяти
        var memoryDir = Path.Combine(_storageCfg.DataRoot, _storageCfg.MemoryDir);
        Directory.CreateDirectory(memoryDir);
        foreach (var memoryFile in manifest.MemoryFiles)
        {
            ZipArchiveEntry? entry = archive.GetEntry($"memory/{memoryFile}");
            if (entry is null)
            {
                continue;
            }

            try
            {
                var destPath = Path.Combine(memoryDir, memoryFile);
                using Stream entryStream = entry.Open();
                using FileStream fileStream = File.Create(destPath);
                entryStream.CopyTo(fileStream);
                result.InstalledMemoryFiles.Add(memoryFile);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Ошибка копирования памяти '{memoryFile}': {ex.Message}");
            }
        }

        // 3. Копирование деклараций инструментов
        var toolsSubdir = _storageCfg.Phase2?.ToolsDir ?? "Tools";
        var toolsDir = Path.Combine(_storageCfg.DataRoot, toolsSubdir);
        Directory.CreateDirectory(toolsDir);
        foreach (var toolFile in manifest.ToolFiles)
        {
            ZipArchiveEntry? entry = archive.GetEntry($"tools/{toolFile}");
            if (entry is null)
            {
                continue;
            }

            try
            {
                var destPath = Path.Combine(toolsDir, toolFile);
                using Stream entryStream = entry.Open();
                using FileStream fileStream = File.Create(destPath);
                entryStream.CopyTo(fileStream);
                result.InstalledToolFiles.Add(toolFile);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Ошибка копирования инструмента '{toolFile}': {ex.Message}");
            }
        }

        return result;
    }

    private static TemplateManifest ReadTemplateManifest(string zipPath)
    {
        using FileStream stream = File.OpenRead(zipPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        ZipArchiveEntry entry = archive.GetEntry("template.json") ?? throw new InvalidOperationException("template.json не найден в архиве шаблона");
        using var reader = new StreamReader(entry.Open());
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<TemplateManifest>(json, JsonOpts) ?? throw new InvalidOperationException("Не удалось десериализовать манифест шаблона");
    }
}

/// <summary>Манифест шаблона агента (template.json внутри .agenttemplate).</summary>
public sealed class TemplateManifest
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Version { get; set; } = 1;

    /// <summary>Список .skillpkg-файлов в skills/ внутри архива.</summary>
    public List<string> Skills { get; set; } = new();

    /// <summary>Список .md-файлов в memory/ внутри архива.</summary>
    public List<string> MemoryFiles { get; set; } = new();

    /// <summary>Список .json-файлов в tools/ внутри архива.</summary>
    public List<string> ToolFiles { get; set; } = new();

    /// <summary>Вертикальный сценарий (greenhouse, coldchain, server-room, vending).</summary>
    public string? Scenario { get; set; }
}

/// <summary>Запись в каталоге шаблонов.</summary>
public sealed record TemplateEntry(
    string FileName,
    string Name,
    string Description,
    int Version,
    int SkillCount,
    string FilePath);

/// <summary>Результат применения шаблона агента.</summary>
public sealed class ApplyTemplateResult
{
    public string TemplateName { get; set; } = "";
    public List<string> InstalledSkills { get; set; } = new();
    public List<string> InstalledMemoryFiles { get; set; } = new();
    public List<string> InstalledToolFiles { get; set; } = new();
    public List<string> Errors { get; set; } = new();

    public bool HasErrors => Errors.Count > 0;
}
