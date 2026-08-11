using System.IO.Compression;
using System.Text.Json;
using Hercules.Config;
using Hercules.Storage;

namespace Hercules.Skills;

/// <summary>
///     Маркетплейс навыков: каталог доступных пакетов в data/Skills/marketplace/.
///     Позволяет листать, искать, устанавливать и публиковать пакеты навыков.
///     Публичный репозиторий шаблонов может быть синхронизирован через git clone или HTTP.
/// </summary>
public sealed class SkillMarketplace
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly SkillPackager _packager;

    public SkillMarketplace(StorageConfig cfg, SkillPackager packager)
    {
        DirectoryPath = Path.Combine(cfg.DataRoot, cfg.SkillsDir, cfg.Phase2?.MarketplaceDir ?? "marketplace");
        Directory.CreateDirectory(DirectoryPath);
        _packager = packager ?? throw new ArgumentNullException(nameof(packager));
    }

    /// <summary>Каталог маркетплейса (data/Skills/marketplace/).</summary>
    public string DirectoryPath { get; }

    /// <summary>
    ///     Список пакетов в маркетплейсе. Сканирует .skillpkg-файлы и возвращает метаданные.
    /// </summary>
    public List<MarketplaceEntry> List()
    {
        var entries = new List<MarketplaceEntry>();
        foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*.skillpkg"))
        {
            try
            {
                SkillPackageManifest manifest = ReadManifestFromZip(file);
                entries.Add(new MarketplaceEntry(
                    Path.GetFileName(file),
                    manifest.Skill.Id,
                    manifest.Skill.Name,
                    manifest.Skill.Description,
                    manifest.Skill.Version,
                    manifest.Skill.PhraseReceivers,
                    file,
                    manifest.CreatedAt));
            }
            catch
            {
                // Пропускаем невалидные пакеты
            }
        }

        return entries;
    }

    /// <summary>
    ///     Установить пакет из маркетплейса в data/Skills/.
    ///     Эквивалент packager.Import, но с conflict-разрешением по умолчанию = Rename.
    /// </summary>
    public Skill Install(string fileName, ConflictResolution conflict = ConflictResolution.Rename)
    {
        var path = Path.Combine(DirectoryPath, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Пакет '{fileName}' не найден в маркетплейсе.");
        }

        return _packager.Import(path, conflict);
    }

    /// <summary>
    ///     Опубликовать пакет в маркетплейс: копирует .skillpkg в marketplace/.
    ///     Если пакет с таким именем уже существует — перезаписывает.
    /// </summary>
    public string Publish(string packagePath)
    {
        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException($"Пакет не найден: {packagePath}");
        }

        var fileName = Path.GetFileName(packagePath);
        var destPath = Path.Combine(DirectoryPath, fileName);
        File.Copy(packagePath, destPath, true);
        return destPath;
    }

    /// <summary>
    ///     Удалить пакет из маркетплейса.
    /// </summary>
    public bool Remove(string fileName)
    {
        var path = Path.Combine(DirectoryPath, fileName);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    /// <summary>
    ///     Поиск пакетов по имени или описанию (case-insensitive substring).
    /// </summary>
    public List<MarketplaceEntry> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return List();
        }

        var q = query.Trim().ToLowerInvariant();
        return List().Where(e =>
            e.Name.ToLowerInvariant().Contains(q) ||
            e.Description.ToLowerInvariant().Contains(q) ||
            e.PhraseReceivers.Any(r => r.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    private static SkillPackageManifest ReadManifestFromZip(string zipPath)
    {
        using FileStream stream = File.OpenRead(zipPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        ZipArchiveEntry entry = archive.GetEntry("skill.package.json") ?? throw new InvalidOperationException("skill.package.json не найден в пакете");
        using var reader = new StreamReader(entry.Open());
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<SkillPackageManifest>(json, JsonOpts) ?? throw new InvalidOperationException("Не удалось десериализовать манифест");
    }
}

/// <summary>Запись в каталоге маркетплейса.</summary>
public sealed record MarketplaceEntry(
    string FileName,
    string SkillId,
    string Name,
    string Description,
    int Version,
    IReadOnlyList<string> PhraseReceivers,
    string FilePath,
    string CreatedAt);
