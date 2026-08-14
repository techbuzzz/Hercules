using System.IO.Compression;
using System.Text.Json;
using Hercules.Config;
using Hercules.Skills.Marketplace;
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
    private readonly IMarketplaceSigningService? _signing;
    private readonly DependencyResolver _resolver;

    public SkillMarketplace(StorageConfig cfg, SkillPackager packager)
    {
        DirectoryPath = Path.Combine(cfg.DataRoot, cfg.SkillsDir, cfg.Phase2?.MarketplaceDir ?? "marketplace");
        Directory.CreateDirectory(DirectoryPath);
        _packager = packager ?? throw new ArgumentNullException(nameof(packager));
        _resolver = new DependencyResolver();
    }

    public SkillMarketplace(StorageConfig cfg, SkillPackager packager, IMarketplaceSigningService signing)
    {
        DirectoryPath = Path.Combine(cfg.DataRoot, cfg.SkillsDir, cfg.Phase2?.MarketplaceDir ?? "marketplace");
        Directory.CreateDirectory(DirectoryPath);
        _packager = packager ?? throw new ArgumentNullException(nameof(packager));
        _signing = signing;
        _resolver = new DependencyResolver();
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
        // Удаляем sidecar-файлы
        var hashFile = Path.ChangeExtension(path, ".hash_sha256.txt");
        var sigFile = Path.ChangeExtension(path, ".signature.txt");
        if (File.Exists(hashFile)) File.Delete(hashFile);
        if (File.Exists(sigFile)) File.Delete(sigFile);
        return true;
    }

    /// <summary>
    ///     Проверить integrity (hash + signature) пакета.
    /// </summary>
    public PackageVerificationResult VerifyPackage(string fileName)
    {
        var path = Path.Combine(DirectoryPath, fileName);
        if (!File.Exists(path))
        {
            return new PackageVerificationResult(false, null, false, false, "Package not found.");
        }

        var hashFile = Path.ChangeExtension(path, ".hash_sha256.txt");
        var sigFile = Path.ChangeExtension(path, ".signature.txt");

        string? hash = null;
        bool hashValid = false;
        bool sigValid = false;

        if (File.Exists(hashFile))
        {
            hash = File.ReadAllText(hashFile).Trim();
            var bytes = File.ReadAllBytes(path);
            if (_signing is not null)
            {
                hashValid = _signing.VerifyHash(bytes, hash);
            }
            else
            {
                var actualHash = ComputeHashFallback(bytes);
                hashValid = string.Equals(actualHash, hash, StringComparison.OrdinalIgnoreCase);
            }

            if (!hashValid)
            {
                return new PackageVerificationResult(false, hash, false, false, "SHA256 hash mismatch.");
            }
        }

        if (File.Exists(sigFile) && _signing is not null && hash is not null)
        {
            var sig = File.ReadAllText(sigFile).Trim();
            sigValid = _signing.VerifySignature(hash, sig);
            if (!sigValid)
            {
                return new PackageVerificationResult(false, hash, true, false, "HMAC signature mismatch.");
            }
        }

        return new PackageVerificationResult(true, hash, hashValid, sigValid, null);
    }

    /// <summary>
    ///     Получить список зависимостей из манифеста пакета.
    /// </summary>
    public List<DependencyInfo> GetDependencies(string fileName)
    {
        var path = Path.Combine(DirectoryPath, fileName);
        if (!File.Exists(path))
        {
            return new List<DependencyInfo>();
        }

        try
        {
            var manifest = ReadManifestFromZip(path);
            return manifest.Dependencies
                .Select(d => new DependencyInfo(
                    d.Id,
                    d.VersionConstraint,
                    d.IsRequired,
                    IsInstalled(d.Id),
                    GetInstalledVersion(d.Id),
                    null))
                .ToList();
        }
        catch
        {
            return new List<DependencyInfo>();
        }
    }

    /// <summary>
    ///     Установить пакет из маркетплейса со всеми зависимостями.
    ///     Возвращает список установленных навыков.
    /// </summary>
    public List<Skill> InstallWithDeps(string fileName)
    {
        var path = Path.Combine(DirectoryPath, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Пакет '{fileName}' не найден в маркетплейсе.");
        }

        var manifest = ReadManifestFromZip(path);
        var rootId = manifest.Skill.Id;
        var installed = new List<Skill>();

        // Сначала устанавливаем зависимости
        foreach (var dep in manifest.Dependencies.Where(d => d.IsRequired))
        {
            var depFile = FindPackageInMarketplace(dep.Id);
            if (depFile is not null && !IsInstalled(dep.Id))
            {
                installed.Add(_packager.Import(Path.Combine(DirectoryPath, depFile), ConflictResolution.Rename));
            }
        }

        // Затем корневой навык
        installed.Add(_packager.Import(path, ConflictResolution.Rename));
        return installed;
    }

    /// <summary>
    ///     Импортировать пакет по HTTP URL (если AllowHttpImport = true).
    /// </summary>
    public async Task<Skill> ImportFromUrlAsync(string url, HttpClient? httpClient = null, CancellationToken ct = default)
    {
        var client = httpClient ?? new HttpClient();
        var bytes = await client.GetByteArrayAsync(url, ct);

        // Проверяем размер
        var maxBytes = 50 * 1024 * 1024; // 50 MB
        if (bytes.Length > maxBytes)
        {
            throw new InvalidOperationException($"Package exceeds maximum size of 50 MB.");
        }

        // Сохраняем во временный файл
        var tempPath = Path.Combine(Path.GetTempPath(), $"hercules-import-{Guid.NewGuid():N}.skillpkg");
        await File.WriteAllBytesAsync(tempPath, bytes, ct);

        try
        {
            // Валидируем
            var errors = _packager.Validate(tempPath);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException($"Invalid package from URL: {string.Join("; ", errors)}");
            }

            // Импортируем
            return _packager.Import(tempPath, ConflictResolution.Rename);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
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

    private static string ComputeHashFallback(byte[] bytes)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private bool IsInstalled(string skillId)
    {
        var marketplaceDir = Path.GetDirectoryName(DirectoryPath) ?? "";
        var skillsDir = Path.Combine(marketplaceDir, "Skills");
        if (!Directory.Exists(skillsDir)) return false;
        return Directory.EnumerateDirectories(skillsDir, $"skill.{skillId}*").Any();
    }

    private static string? GetInstalledVersion(string skillId)
    {
        // Not easily accessible without repo — return null for now
        return null;
    }

    private string? FindPackageInMarketplace(string skillId)
    {
        foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*.skillpkg"))
        {
            try
            {
                var manifest = ReadManifestFromZip(file);
                if (manifest.Skill.Id == skillId)
                {
                    return Path.GetFileName(file);
                }
            }
            catch
            {
                // Пропускаем невалидные
            }
        }
        return null;
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
