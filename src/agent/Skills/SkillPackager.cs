using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using Hercules.Config;
using Hercules.Skills.Marketplace;
using Hercules.Storage;

namespace Hercules.Skills;

/// <summary>
///     Экспорт/импорт пакетов навыков.
///
///     **ZIP-пакет (.skillpkg)** — переносимый архив:
///     <list type="bullet">
///         <item>skill.folder/ — папка навыка (содержимое по spec)</item>
///         <item>skill.package.json — манифест (package-level metadata)</item>
///     </list>
///
///     **Folder-экспорт** — директория по спецификации:
///     <list type="bullet">
///         <item>skill.meta.json — метаданные навыка</item>
///         <item>skill.prompt.md — system prompt</item>
///         <item>skill.description.md — описание навыка</item>
///         <item>skill.tests.json — тесты (опционально)</item>
///         <item>skill.examples.json — примеры (опционально)</item>
///         <item>skill.changelog.md — история версий (опционально)</item>
///         <item>tool.schema.json — декларация инструментов (опционально)</item>
///         <item>skill.usage.json — лог использования (опционально, для анализа)</item>
///     </list>
///
///     Экспорт сохраняет навык в переносимый формат; импорт разворачивает пакет
///     в data/Skills/ с возможностью conflict-разрешения (replace | skip | rename).
///     Folder-структура — авторитетный формат хранения; ZIP — формат распространения.
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
    private readonly ISecretMaskingService? _masking;
    private readonly bool _redactInExports;
    private readonly IMarketplaceSigningService? _signing;

    public SkillPackager(FileSkillRepository repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }

    public SkillPackager(FileSkillRepository repo, SecretsConfig secretsConfig, ISecretMaskingService masking)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _masking = masking;
        _redactInExports = secretsConfig?.RedactInExports ?? true;
    }

    public SkillPackager(FileSkillRepository repo, IMarketplaceSigningService signing)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _signing = signing;
    }

    public SkillPackager(FileSkillRepository repo, SecretsConfig secretsConfig, ISecretMaskingService masking, IMarketplaceSigningService signing)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _masking = masking;
        _redactInExports = secretsConfig?.RedactInExports ?? true;
        _signing = signing;
    }

    /// <summary>Имя файла-пакета по умолчанию: {id}-v{version}.skillpkg.</summary>
    public static string PackageFileName(string skillId, int version)
    {
        return $"{skillId}-v{version}.skillpkg";
    }

    /// <summary>
    ///     Экспортировать навык в папку по спецификации skill-package-spec.md.
    ///     Создаёт skill.{id}/ и все файлы внутри.
    ///     Возвращает путь к созданной папке.
    /// </summary>
    /// <param name="skillId">ID навыка для экспорта.</param>
    /// <param name="outputDir">Родительская директория. Если null — используется data/Skills/exports/.</param>
    /// <param name="examples">Опциональные примеры (SkillExamples).</param>
    /// <param name="changelog">Опциональная история версий (Markdown).</param>
    /// <param name="includeUsage">Включить историю использования. По умолчанию false.</param>
    public string ExportToFolder(string skillId, string? outputDir = null,
        SkillExamples? examples = null, string? changelog = null, bool includeUsage = false)
    {
        Skill? skill = _repo.Load(skillId);
        if (skill is null)
        {
            throw new InvalidOperationException($"Навык '{skillId}' не найден.");
        }

        var outDir = outputDir ?? Path.Combine(_repo.SkillsDirectory, "exports");
        Directory.CreateDirectory(outDir);

        var skillDir = Path.Combine(outDir, $"skill.{skillId}");
        Directory.CreateDirectory(skillDir);

        // skill.meta.json
        File.WriteAllText(Path.Combine(skillDir, "skill.meta.json"),
            JsonSerializer.Serialize(skill.Meta, JsonOpts));

        // skill.description.md — redact secrets before export
        var description = _redactInExports && _masking is not null
            ? _masking.MaskSecrets(skill.Description)
            : skill.Description;
        File.WriteAllText(Path.Combine(skillDir, "skill.description.md"), description);

        // skill.prompt.md — redact secrets before export
        var prompt = _redactInExports && _masking is not null
            ? _masking.MaskSecrets(skill.Prompt)
            : skill.Prompt;
        File.WriteAllText(Path.Combine(skillDir, "skill.prompt.md"), prompt);

        // skill.examples.json (optional)
        if (examples is not null && examples.Examples.Count > 0)
        {
            File.WriteAllText(Path.Combine(skillDir, "skill.examples.json"),
                JsonSerializer.Serialize(examples, JsonOpts));
        }

        // skill.changelog.md (optional)
        if (!string.IsNullOrWhiteSpace(changelog))
        {
            File.WriteAllText(Path.Combine(skillDir, "skill.changelog.md"), changelog);
        }

        // tool.schema.json (optional)
        if (skill.Meta.Tools is { Count: > 0 })
        {
            File.WriteAllText(Path.Combine(skillDir, "tool.schema.json"),
                JsonSerializer.Serialize(skill.Meta.Tools, JsonOpts));
        }

        // skill.usage.json (optional — analytics, not exported by default)
        if (includeUsage)
        {
            List<SkillUsage> usages = _repo.LoadUsages(skillId);
            if (usages.Count > 0)
            {
                File.WriteAllText(Path.Combine(skillDir, "skill.usage.json"),
                    JsonSerializer.Serialize(usages, JsonOpts));
            }
        }

        return skillDir;
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

        // Пишем в temp-файл, чтобы потом подписать
        var tempPath = packagePath + ".tmp";
        using (FileStream archiveStream = File.Create(tempPath))
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create))
        {

        // skill.folder/ — зеркало folder-структуры внутри ZIP
        string skillFolder = $"skill.{skill.Meta.Id}/";

        // 1. skill.meta.json
        WriteEntry(archive, $"{skillFolder}skill.meta.json",
            JsonSerializer.Serialize(skill.Meta, JsonOpts));

        // 2. skill.description.md — redact secrets before export
        var description = _redactInExports && _masking is not null
            ? _masking.MaskSecrets(skill.Description)
            : skill.Description;
        WriteEntry(archive, $"{skillFolder}skill.description.md", description);

        // 3. skill.prompt.md — redact secrets before export
        var prompt = _redactInExports && _masking is not null
            ? _masking.MaskSecrets(skill.Prompt)
            : skill.Prompt;
        WriteEntry(archive, $"{skillFolder}skill.prompt.md", prompt);

        // 4. tool.schema.json (optional)
        if (skill.Meta.Tools is { Count: > 0 })
        {
            WriteEntry(archive, $"{skillFolder}tool.schema.json",
                JsonSerializer.Serialize(skill.Meta.Tools, JsonOpts));
        }

        // 5. skill.usage.json (optional)
        if (includeUsage)
        {
            List<SkillUsage> usages = _repo.LoadUsages(skillId);
            if (usages.Count > 0)
            {
                WriteEntry(archive, $"{skillFolder}skill.usage.json",
                    JsonSerializer.Serialize(usages, JsonOpts));
            }
        }

        // 6. skill.package.json — package-level manifest (ZIP convenience)
        var manifest = new SkillPackageManifest
        {
            PackageVersion = 1,
            PackageFormat = "zip",
            PackageSpecVersion = "1.0.0",
            Skill = new SkillPackageSkillMeta
            {
                Id = skill.Meta.Id,
                Name = skill.Meta.Name,
                Description = skill.Meta.Description,
                PhraseReceivers = skill.Meta.PhraseReceivers,
                Version = skill.Meta.Version,
                CreatedAt = skill.Meta.CreatedAt,
                // Task 020: manifest fields
                Owner = skill.Meta.Owner,
                MinHerculesVersion = skill.Meta.MinHerculesVersion,
                MaxHerculesVersion = skill.Meta.MaxHerculesVersion,
                InputSchemaVersion = skill.Meta.InputSchemaVersion,
                OutputSchemaVersion = skill.Meta.OutputSchemaVersion,
                Permissions = skill.Meta.Permissions,
                ModelRequirements = skill.Meta.ModelRequirements,
                RiskLevel = skill.Meta.RiskLevel,
                Budget = skill.Meta.Budget is not null
                    ? new SkillPackageBudget
                    {
                        MaxTokensPerCall = skill.Meta.Budget.MaxTokensPerCall,
                        MaxCallsPerMinute = skill.Meta.Budget.MaxCallsPerMinute,
                        MaxCostPerCallUsd = skill.Meta.Budget.MaxCostPerCallUsd
                    }
                    : null
            },
            Tools = skill.Meta.Tools,
            CreatedAt = DateTime.UtcNow.ToString("o")
        };
        WriteEntry(archive, "skill.package.json", JsonSerializer.Serialize(manifest, JsonOpts));
        }

        // Подписываем и пишем sidecar-файлы
        WriteIntegritySidecars(tempPath, packagePath);
        return packagePath;
    }

    private void WriteIntegritySidecars(string tempPath, string finalPath)
    {
        byte[] bytes = File.ReadAllBytes(tempPath);
        var hash = _signing?.ComputeHash(bytes) ?? ComputeHashFallback(bytes);

        // signature.txt (HMAC-SHA256)
        string? signature = null;
        if (_signing is not null)
        {
            try
            {
                signature = _signing.ComputeSignature(hash);
            }
            catch (InvalidOperationException)
            {
                // Ключ не сконфигурирован — пропускаем подпись
            }
        }

        if (signature is not null)
        {
            File.WriteAllText(Path.ChangeExtension(finalPath, ".signature.txt"), signature);
        }

        // hash_sha256.txt
        File.WriteAllText(Path.ChangeExtension(finalPath, ".hash_sha256.txt"), hash);

        // Перемещаем из temp в final
        if (File.Exists(finalPath)) File.Delete(finalPath);
        File.Move(tempPath, finalPath);
    }

    private static string ComputeHashFallback(byte[] bytes)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    ///     Импортировать навык из ZIP-пакета (.skillpkg) или из папки по спецификации.
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
        SkillMeta meta;
        string description;
        string prompt;

        if (Directory.Exists(packagePath))
        {
            // Импорт из папки по спецификации skill-package-spec.md
            (meta, description, prompt) = ReadFromFolder(packagePath);

            // Восстанавливаем manifest из meta для обратной совместимости с остальной логикой
            manifest = new SkillPackageManifest
            {
                PackageVersion = 1,
                PackageFormat = "folder",
                PackageSpecVersion = "1.0.0",
                Skill = new SkillPackageSkillMeta
                {
                    Id = meta.Id,
                    Name = meta.Name,
                    Description = meta.Description,
                    PhraseReceivers = meta.PhraseReceivers,
                    Version = meta.Version,
                    CreatedAt = meta.CreatedAt
                },
                Tools = meta.Tools,
                CreatedAt = DateTime.UtcNow.ToString("o")
            };
        }
        else if (File.Exists(packagePath))
        {
            // Импорт из ZIP-архива
            (manifest, meta, description, prompt) = ReadFromZip(packagePath);
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
                    // Существующие файлы будут перезаписаны через Save
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
                CreatedAt = manifest.Skill.CreatedAt,
                Tools = manifest.Tools,
                // Task 020: manifest fields
                Owner = manifest.Skill.Owner,
                MinHerculesVersion = manifest.Skill.MinHerculesVersion,
                MaxHerculesVersion = manifest.Skill.MaxHerculesVersion,
                InputSchemaVersion = manifest.Skill.InputSchemaVersion,
                OutputSchemaVersion = manifest.Skill.OutputSchemaVersion,
                Permissions = manifest.Skill.Permissions,
                ModelRequirements = manifest.Skill.ModelRequirements,
                RiskLevel = manifest.Skill.RiskLevel,
                Budget = manifest.Skill.Budget is not null
                    ? new SkillMetaBudget
                    {
                        MaxTokensPerCall = manifest.Skill.Budget.MaxTokensPerCall,
                        MaxCallsPerMinute = manifest.Skill.Budget.MaxCallsPerMinute,
                        MaxCostPerCallUsd = manifest.Skill.Budget.MaxCostPerCallUsd
                    }
                    : null
            },
            Description = description,
            Prompt = prompt
        };
        _repo.Save(skill);
        return skill;
    }

    /// <summary>
    ///     Проверить валидность пакета без импорта. Возвращает список ошибок (пустой = OK).
    ///     Также проверяет integrity hash и подпись (task_021).
    /// </summary>
    public List<string> Validate(string packagePath)
    {
        var errors = new List<string>();

        try
        {
            SkillPackageManifest? manifest = null;
            SkillMeta? meta = null;
            string description = "";
            string prompt = "";

            if (Directory.Exists(packagePath))
            {
                (meta, description, prompt) = ReadFromFolder(packagePath);
            }
            else if (File.Exists(packagePath))
            {
                (manifest, meta, description, prompt) = ReadFromZip(packagePath);
            }
            else
            {
                errors.Add($"Пакет не найден: {packagePath}");
                return errors;
            }

            // Integrity check (task_021): hash + signature verification
            if (File.Exists(packagePath))
            {
                ValidateIntegrity(packagePath, errors);
            }

            var id = meta?.Id ?? manifest?.Skill.Id ?? "";
            var name = meta?.Name ?? manifest?.Skill.Name ?? "";
            var receivers = meta?.PhraseReceivers ?? manifest?.Skill.PhraseReceivers ?? new List<string>();

            if (string.IsNullOrWhiteSpace(id))
            {
                errors.Add("ID навыка пуст (skill.meta.json / skill.package.json).");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add("Имя навыка пусто.");
            }

            if (receivers.Count == 0)
            {
                errors.Add("phrase_receivers пуст — навык не сможет маршрутизироваться.");
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

    private (SkillMeta Meta, string Description, string Prompt) ReadFromFolder(string dir)
    {
        // skill.meta.json (required)
        var metaPath = Path.Combine(dir, "skill.meta.json");
        if (!File.Exists(metaPath))
        {
            throw new InvalidOperationException($"Обязательный файл отсутствует: skill.meta.json в {dir}");
        }

        SkillMeta meta = JsonSerializer.Deserialize<SkillMeta>(File.ReadAllText(metaPath), JsonOpts)
            ?? throw new InvalidOperationException("Не удалось десериализовать skill.meta.json");

        // skill.description.md
        var descPath = Path.Combine(dir, "skill.description.md");
        string description = File.Exists(descPath) ? File.ReadAllText(descPath) : "";

        // skill.prompt.md
        var promptPath = Path.Combine(dir, "skill.prompt.md");
        if (!File.Exists(promptPath))
        {
            throw new InvalidOperationException($"Обязательный файл отсутствует: skill.prompt.md в {dir}");
        }
        string prompt = File.ReadAllText(promptPath);

        return (meta, description, prompt);
    }

    private (SkillPackageManifest Manifest, SkillMeta Meta, string Description, string Prompt) ReadFromZip(string zipPath)
    {
        using FileStream archiveStream = File.OpenRead(zipPath);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);

        // Пробуем прочитать новый формат (folder inside ZIP) — skill.meta.json
        string? metaEntry = ReadEntryOptional(archive, $"skill.{GetSkillIdFromZip(zipPath)}/skill.meta.json");
        string? descEntry = ReadEntryOptional(archive, $"skill.{GetSkillIdFromZip(zipPath)}/skill.description.md");
        string? promptEntry = ReadEntryOptional(archive, $"skill.{GetSkillIdFromZip(zipPath)}/skill.prompt.md");
        string? toolsEntry = ReadEntryOptional(archive, $"skill.{GetSkillIdFromZip(zipPath)}/tool.schema.json");

        if (metaEntry is not null && promptEntry is not null)
        {
            // Новый формат: folder inside ZIP
            var meta = JsonSerializer.Deserialize<SkillMeta>(metaEntry, JsonOpts)
                ?? throw new InvalidOperationException("Не удалось десериализовать skill.meta.json");
            var tools = toolsEntry is not null
                ? JsonSerializer.Deserialize<List<ToolDeclaration>>(toolsEntry, JsonOpts)
                : null;
            meta.Tools = tools;

            // skill.package.json (если есть) — для обратной совместимости
            string? pkgJson = ReadEntryOptional(archive, "skill.package.json");
            SkillPackageManifest manifest;
            if (pkgJson is not null)
            {
                manifest = JsonSerializer.Deserialize<SkillPackageManifest>(pkgJson, JsonOpts)
                    ?? new SkillPackageManifest();
            }
            else
            {
                manifest = new SkillPackageManifest
                {
                    PackageVersion = 1,
                    PackageFormat = "zip",
                    PackageSpecVersion = "1.0.0",
                    Skill = new SkillPackageSkillMeta
                    {
                        Id = meta.Id, Name = meta.Name, Description = meta.Description,
                        PhraseReceivers = meta.PhraseReceivers, Version = meta.Version,
                        CreatedAt = meta.CreatedAt
                    },
                    Tools = meta.Tools
                };
            }

            return (manifest, meta, descEntry ?? "", promptEntry);
        }

        // Legacy формат: skill.package.json + skill.description.md + skill.prompt.md (flat)
        string manifestJson = ReadEntry(archive, "skill.package.json");
        SkillPackageManifest legacyManifest = JsonSerializer.Deserialize<SkillPackageManifest>(manifestJson, JsonOpts)
            ?? throw new InvalidOperationException("Не удалось десериализовать skill.package.json");

        string legacyDesc = ReadEntry(archive, "skill.description.md");
        string legacyPrompt = ReadEntry(archive, "skill.prompt.md");

        var legacyMeta = new SkillMeta
        {
            Id = legacyManifest.Skill.Id,
            Name = legacyManifest.Skill.Name,
            Description = legacyManifest.Skill.Description,
            PhraseReceivers = legacyManifest.Skill.PhraseReceivers,
            Version = legacyManifest.Skill.Version,
            CreatedAt = legacyManifest.Skill.CreatedAt,
            Tools = legacyManifest.Tools
        };

        return (legacyManifest, legacyMeta, legacyDesc, legacyPrompt);
    }

    private static string GetSkillIdFromZip(string zipPath)
    {
        // Извлекаем skill ID из имени файла: {id}-v{N}.skillpkg → {id}
        var name = Path.GetFileNameWithoutExtension(zipPath);
        var dashIdx = name.LastIndexOf("-v");
        return dashIdx > 0 ? name[..dashIdx] : name;
    }

    private static string ReadEntry(ZipArchive archive, string entryName)
    {
        ZipArchiveEntry entry = archive.GetEntry(entryName)
            ?? throw new InvalidOperationException($"Обязательный файл отсутствует в пакете: {entryName}");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static string? ReadEntryOptional(ZipArchive archive, string entryName)
    {
        ZipArchiveEntry? entry = archive.GetEntry(entryName);
        if (entry is null) return null;
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    /// <summary>
    ///     Проверить SHA256 hash и HMAC-SHA256 подпись пакета.
    ///     Добавляет ошибки в список errors.
    /// </summary>
    private void ValidateIntegrity(string packagePath, List<string> errors)
    {
        var hashFile = Path.ChangeExtension(packagePath, ".hash_sha256.txt");
        var sigFile = Path.ChangeExtension(packagePath, ".signature.txt");

        if (File.Exists(hashFile))
        {
            var expectedHash = File.ReadAllText(hashFile).Trim();
            var packageBytes = File.ReadAllBytes(packagePath);
            if (_signing is not null)
            {
                if (!_signing.VerifyHash(packageBytes, expectedHash))
                {
                    errors.Add("SHA256 hash mismatch — package integrity check failed.");
                }
            }
            else
            {
                // Fallback без signing service
                var actualHash = ComputeHashFallback(packageBytes);
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("SHA256 hash mismatch — package integrity check failed.");
                }
            }
        }

        if (File.Exists(sigFile))
        {
            var signature = File.ReadAllText(sigFile).Trim();
            if (_signing is not null)
            {
                // Сначала проверяем hash
                string? hash = null;
                if (File.Exists(hashFile))
                {
                    hash = File.ReadAllText(hashFile).Trim();
                }
                else
                {
                    hash = ComputeHashFallback(File.ReadAllBytes(packagePath));
                }

                if (!_signing.VerifySignature(hash, signature))
                {
                    errors.Add("HMAC-SHA256 signature verification failed — package may be tampered.");
                }
            }
        }
        else if (_signing is not null)
        {
            // Подпись ожидается, но файла нет — пропускаем (не ошибка, backward-compatible)
        }
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
