using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hercules.Config;
using Hercules.Storage;
using Microsoft.Extensions.Logging;

namespace Hercules.Backup;

/// <summary>
///     Creates encrypted backup archives of config, skills, memory, SQLite data, and device identity (task_063).
/// </summary>
public sealed class BackupService : IBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly BackupConfig _config;
    private readonly EncryptionService _encryption;
    private readonly ILogger<BackupService> _logger;
    private readonly string _dataRoot;

    public BackupService(
        BackupConfig config,
        EncryptionService encryption,
        ILogger<BackupService> logger,
        string dataRoot)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataRoot = dataRoot ?? throw new ArgumentNullException(nameof(dataRoot));
    }

    public async Task<BackupResult> CreateBackupAsync(string? passphrase = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var warnings = new List<string>();
        var id = Ulid.NewUlid().ToString();
        var effectivePassphrase = passphrase ?? _config.Passphrase;
        var useEncryption = !string.IsNullOrEmpty(effectivePassphrase);

        _logger.LogInformation("Starting backup {BackupId} (encrypted={Encrypted})", id, useEncryption);

        Directory.CreateDirectory(_config.BackupDir);

        // Collect files to back up
        var entries = new List<BackupEntry>();
        var sourceMap = BuildSourceMap();
        long uncompressedSize = 0;

        foreach (var (label, sourcePaths, destRelPath) in sourceMap)
        {
            if (ct.IsCancellationRequested) ct.ThrowIfCancellationRequested();

            foreach (var sourcePath in sourcePaths)
            {
                if (!Directory.Exists(sourcePath) && !File.Exists(sourcePath))
                {
                    warnings.Add($"Source not found, skipping: {sourcePath}");
                    _logger.LogDebug("Skipping missing source: {Path}", sourcePath);
                    continue;
                }

                if (Directory.Exists(sourcePath))
                {
                    foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
                    {
                        var rel = Path.GetRelativePath(sourcePath, file).Replace('\\', '/');
                        var entry = await AddFileEntryAsync(file, Path.Combine(destRelPath, rel), ct);
                        if (entry != null)
                        {
                            entries.Add(entry);
                            uncompressedSize += entry.SizeBytes;
                        }
                    }
                }
                else if (File.Exists(sourcePath))
                {
                    var entry = await AddFileEntryAsync(sourcePath, destRelPath, ct);
                    if (entry != null)
                    {
                        entries.Add(entry);
                        uncompressedSize += entry.SizeBytes;
                    }
                }
            }
        }

        if (entries.Count == 0)
        {
            _logger.LogWarning("Backup {BackupId} contains no files — nothing to back up", id);
            return new BackupResult(id, "", 0, 0, 0, useEncryption, null, sw.Elapsed, warnings);
        }

        // Build manifest
        var manifest = new BackupManifest
        {
            Id = id,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            HerculesVersion = typeof(BackupService).Assembly.GetName().Version?.ToString() ?? "unknown",
            Hostname = Environment.MachineName,
            Entries = entries,
            UncompressedSizeBytes = uncompressedSize,
            Encrypted = useEncryption,
            Components = new BackupComponents
            {
                Config = _config.IncludeConfig,
                Skills = _config.IncludeSkills,
                Memory = _config.IncludeMemory,
                SqliteDb = _config.IncludeSqliteDb,
                Identity = _config.IncludeIdentity
            }
        };

        // Create ZIP in memory
        byte[] zipBytes;
        using (var zipStream = new MemoryStream())
        {
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                // Write manifest
                var manifestEntry = archive.CreateEntry("backup-manifest.json", CompressionLevel.Optimal);
                await using (var ms = manifestEntry.Open())
                    await JsonSerializer.SerializeAsync(ms, manifest, JsonOptions);

                // Write files
                foreach (var entry in entries)
                {
                    if (!File.Exists(entry.SourcePath)) continue;
                    var zipEntry = archive.CreateEntry(entry.RelativePath, CompressionLevel.Optimal);
                    await using (var entryStream = zipEntry.Open())
                    {
                        await using var fileStream = File.OpenRead(entry.SourcePath);
                        await fileStream.CopyToAsync(entryStream, ct);
                    }
                }
            }
            zipBytes = zipStream.ToArray();
        }

        // Encrypt if passphrase provided
        byte[] finalBytes;
        byte[]? key = null;
        string? keyFingerprint = null;

        if (useEncryption)
        {
            var (keyDerived, nonce, salt) = _encryption.DeriveKey(effectivePassphrase);
            key = keyDerived;
            keyFingerprint = EncryptionService.ComputeKeyFingerprint(key);
            manifest.KeyFingerprint = keyFingerprint;

            var encrypted = _encryption.Encrypt(zipBytes, key, nonce);

            // Prepend salt to encrypted payload
            finalBytes = new byte[salt.Length + encrypted.Length];
            salt.CopyTo(finalBytes, 0);
            encrypted.CopyTo(finalBytes, salt.Length);
        }
        else
        {
            finalBytes = zipBytes;
        }

        manifest.CompressedSizeBytes = finalBytes.Length;

        // Write archive
        var archivePath = Path.Combine(_config.BackupDir, $"{_config.FilePrefix}-{id}.hba");
        await File.WriteAllBytesAsync(archivePath, finalBytes, ct);

        // Re-write manifest for storage alongside (plaintext metadata)
        await WriteManifestAsync(id, manifest, ct);

        // Enforce retention
        await EnforceRetentionAsync(ct);

        sw.Stop();
        _logger.LogInformation(
            "Backup {BackupId} completed: {Files} files, {SizeKB} KB, encrypted={Encrypted} in {Ms}ms",
            id, entries.Count, finalBytes.Length / 1024, useEncryption, sw.ElapsedMilliseconds);

        return new BackupResult(
            id, archivePath, finalBytes.Length, uncompressedSize, entries.Count,
            useEncryption, keyFingerprint, sw.Elapsed, warnings);
    }

    public async Task<RestoreResult> RestoreAsync(
        string backupId,
        string? passphrase = null,
        string? targetDir = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var effectiveTarget = targetDir ?? _dataRoot;
        var effectivePassphrase = passphrase ?? _config.Passphrase;
        var errors = new List<string>();
        var warnings = new List<string>();
        var filesRestored = 0;
        var filesSkipped = 0;

        _logger.LogInformation("Restoring backup {BackupId} to {Target}", backupId, effectiveTarget);

        // Locate archive (wrap in try-catch for robustness with invalid IDs)
        string? archivePath;
        try
        {
            archivePath = Directory.GetFiles(_config.BackupDir, $"{_config.FilePrefix}-{backupId}.hba")
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate backups directory");
            archivePath = null;
        }

        if (archivePath == null)
        {
            _logger.LogError("Backup archive not found: {BackupId}", backupId);
            return new RestoreResult(backupId, false, 0, 0, new List<string> { $"Backup {backupId} not found." }, warnings, sw.Elapsed);
        }

        var encryptedData = await File.ReadAllBytesAsync(archivePath, ct);
        byte[] zipBytes;

        // Read manifest to determine encryption
        var manifest = await ReadManifestAsync(backupId, ct);
        if (manifest == null)
        {
            errors.Add("Could not read backup manifest.");
            return new RestoreResult(backupId, false, 0, 0, errors, warnings, sw.Elapsed);
        }

        if (manifest.Encrypted)
        {
            if (string.IsNullOrEmpty(effectivePassphrase))
            {
                errors.Add("Backup is encrypted but no passphrase was provided.");
                return new RestoreResult(backupId, false, 0, 0, errors, warnings, sw.Elapsed);
            }

            try
            {
                var salt = encryptedData.AsSpan(0, 16).ToArray();
                var encryptedPayload = encryptedData.AsSpan(16).ToArray();
                _encryption.DeriveKeyWithSalt(effectivePassphrase, salt, out var key, out var nonce);
                zipBytes = _encryption.Decrypt(encryptedPayload, key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt backup {BackupId} — wrong passphrase?", backupId);
                errors.Add("Decryption failed — wrong passphrase or corrupted archive.");
                return new RestoreResult(backupId, false, 0, 0, errors, warnings, sw.Elapsed);
            }
        }
        else
        {
            zipBytes = encryptedData;
        }

        // Extract ZIP
        using var zipStream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var manifestEntry = archive.GetEntry("backup-manifest.json");
        if (manifestEntry == null)
        {
            errors.Add("Archive is missing backup-manifest.json.");
            return new RestoreResult(backupId, false, 0, 0, errors, warnings, sw.Elapsed);
        }

        BackupManifest restoredManifest;
        await using (var ms = manifestEntry.Open())
        {
            restoredManifest = await JsonSerializer.DeserializeAsync<BackupManifest>(ms, cancellationToken: ct)
                ?? throw new InvalidOperationException("Failed to deserialize manifest.");
        }

        foreach (var entry in archive.Entries)
        {
            if (ct.IsCancellationRequested) ct.ThrowIfCancellationRequested();
            if (entry.Name == "backup-manifest.json") continue;

            try
            {
                var destPath = Path.Combine(effectiveTarget, entry.FullName);
                var destDir = Path.GetDirectoryName(destPath);
                if (destDir != null) Directory.CreateDirectory(destDir);

                if (File.Exists(destPath))
                {
                    warnings.Add($"File already exists, skipping: {destPath}");
                    filesSkipped++;
                    continue;
                }

                entry.ExtractToFile(destPath);
                filesRestored++;
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to restore {entry.FullName}: {ex.Message}");
                _logger.LogWarning(ex, "Failed to restore {Path}", entry.FullName);
            }
        }

        sw.Stop();
        _logger.LogInformation(
            "Restore {BackupId} completed: {Restored} restored, {Skipped} skipped, {Errors} errors in {Ms}ms",
            backupId, filesRestored, filesSkipped, errors.Count, sw.ElapsedMilliseconds);

        return new RestoreResult(backupId, errors.Count == 0, filesRestored, filesSkipped, errors, warnings, sw.Elapsed);
    }

    public Task<IReadOnlyList<BackupSummary>> ListBackupsAsync(CancellationToken ct = default)
    {
        var backups = new List<BackupSummary>();

        if (!Directory.Exists(_config.BackupDir))
            return Task.FromResult<IReadOnlyList<BackupSummary>>(backups);

        var pattern = $"{_config.FilePrefix}-*.hba";
        foreach (var file in Directory.EnumerateFiles(_config.BackupDir, pattern))
        {
            var filename = Path.GetFileNameWithoutExtension(file);
            var id = filename.StartsWith($"{_config.FilePrefix}-")
                ? filename[(_config.FilePrefix.Length + 1)..]
                : filename;

            var manifest = ReadManifestAsync(id, ct).GetAwaiter().GetResult();
            var fi = new FileInfo(file);

            backups.Add(new BackupSummary
            {
                Id = id,
                Filename = Path.GetFileName(file),
                CreatedAt = manifest?.CreatedAt ?? fi.CreationTimeUtc.ToString("O"),
                SizeBytes = fi.Length,
                Encrypted = manifest?.Encrypted ?? false,
                KeyFingerprint = manifest?.KeyFingerprint,
                Components = manifest?.Components ?? new BackupComponents(),
                FilesCount = manifest?.Entries.Count ?? 0
            });
        }

        var sorted = backups.OrderByDescending(b => b.CreatedAt).ToList();
        return Task.FromResult<IReadOnlyList<BackupSummary>>(sorted);
    }

    public async Task<VerifyResult> VerifyBackupAsync(string backupId, string? passphrase = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var warnings = new List<string>();
        var validHashes = new List<string>();
        var invalidHashes = new List<string>();
        bool? encrypted = null;
        string? keyFingerprint = null;

        string? archivePath;
        try
        {
            archivePath = Directory.GetFiles(_config.BackupDir, $"{_config.FilePrefix}-{backupId}.hba")
                .FirstOrDefault();
        }
        catch
        {
            return new VerifyResult(backupId, false, false, null, validHashes, invalidHashes,
                new List<string> { $"Backup {backupId} not found." }, sw.Elapsed);
        }

        if (archivePath == null)
            return new VerifyResult(backupId, false, false, null, validHashes, invalidHashes,
                new List<string> { $"Backup {backupId} not found." }, sw.Elapsed);

        BackupManifest? manifest;
        byte[] zipBytes;
        try
        {
            var encryptedData = await File.ReadAllBytesAsync(archivePath, ct);
            manifest = await ReadManifestAsync(backupId, ct);
            if (manifest == null)
                return new VerifyResult(backupId, false, false, null, validHashes, invalidHashes,
                    new List<string> { "Could not read manifest." }, sw.Elapsed);

            encrypted = manifest.Encrypted;
            keyFingerprint = manifest.KeyFingerprint;

            if (manifest.Encrypted)
            {
                var effectivePassphrase = passphrase ?? _config.Passphrase;
                if (string.IsNullOrEmpty(effectivePassphrase))
                {
                    warnings.Add("Backup is encrypted — passphrase required to verify file hashes.");
                    return new VerifyResult(backupId, true, true, manifest.KeyFingerprint, validHashes, invalidHashes, warnings, sw.Elapsed);
                }

                try
                {
                    var salt = encryptedData.AsSpan(0, 16).ToArray();
                    var encryptedPayload = encryptedData.AsSpan(16).ToArray();
                    _encryption.DeriveKeyWithSalt(effectivePassphrase, salt, out var key, out var _);
                    zipBytes = _encryption.Decrypt(encryptedPayload, key);
                }
                catch (Exception dex)
                {
                    _logger.LogWarning(dex, "Decryption failed for {BackupId}", backupId);
                    return new VerifyResult(backupId, false, true, manifest.KeyFingerprint, validHashes, invalidHashes,
                        new List<string> { "Decryption failed — wrong passphrase or corrupted archive." }, sw.Elapsed);
                }
            }
            else
            {
                zipBytes = encryptedData;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read backup file for {BackupId}", backupId);
            return new VerifyResult(backupId, false, encrypted ?? false, keyFingerprint, validHashes, invalidHashes,
                new List<string> { $"Backup read failed: {ex.Message}" }, sw.Elapsed);
        }

        // Verify file hashes inside ZIP
        try
        {
            await using var zipStream = new MemoryStream(zipBytes);
            await using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

            foreach (var manifestEntry in manifest.Entries)
            {
                var zipEntry = archive.GetEntry(manifestEntry.RelativePath);
                if (zipEntry == null)
                {
                    invalidHashes.Add($"{manifestEntry.RelativePath}: not found in archive");
                    continue;
                }

                await using var entryStream = zipEntry.Open();
                var actualHash = await EncryptionService.ComputeFileHashAsync(entryStream, ct);
                if (actualHash == manifestEntry.ContentHash)
                    validHashes.Add(manifestEntry.RelativePath);
                else
                    invalidHashes.Add($"{manifestEntry.RelativePath}: hash mismatch");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read ZIP archive for {BackupId}", backupId);
            warnings.Add($"Archive read failed: {ex.Message}");
        }

        sw.Stop();
        var isValid = invalidHashes.Count == 0 && warnings.Count == 0;

        _logger.LogInformation(
            "Verify {BackupId}: valid={Valid}, {ValidCount} OK, {InvalidCount} bad in {Ms}ms",
            backupId, isValid, validHashes.Count, invalidHashes.Count, sw.ElapsedMilliseconds);

        return new VerifyResult(
            backupId, isValid, encrypted ?? false, keyFingerprint,
            validHashes, invalidHashes, warnings, sw.Elapsed);
    }

    public Task DeleteBackupAsync(string backupId, CancellationToken ct = default)
    {
        var archivePath = Directory.GetFiles(_config.BackupDir, $"{_config.FilePrefix}-{backupId}.hba")
            .FirstOrDefault();

        if (archivePath != null)
        {
            File.Delete(archivePath);
            _logger.LogInformation("Deleted backup {BackupId}", backupId);
        }

        var manifestPath = Path.Combine(_config.BackupDir, $"{_config.FilePrefix}-{backupId}.manifest.json");
        if (File.Exists(manifestPath)) File.Delete(manifestPath);

        // Backward compat: also try old naming {id}.manifest.json
        var oldManifestPath = Path.Combine(_config.BackupDir, $"{backupId}.manifest.json");
        if (File.Exists(oldManifestPath)) File.Delete(oldManifestPath);

        return Task.CompletedTask;
    }

    // ─── Private helpers ────────────────────────────────────────────────────────

    private List<(string Label, string[] Paths, string DestRelative)> BuildSourceMap()
    {
        var map = new List<(string, string[], string)>();
        var cfgDir = Path.Combine(_dataRoot, "config");
        var skillsDir = Path.Combine(_dataRoot, "skills");
        var memoryDir = Path.Combine(_dataRoot, "memory");
        var identityDir = Path.Combine(_dataRoot, "security", "identity");

        if (_config.IncludeConfig && Directory.Exists(cfgDir))
            map.Add(("config", new[] { cfgDir }, "config/"));

        if (_config.IncludeSkills && Directory.Exists(skillsDir))
            map.Add(("skills", new[] { skillsDir }, "skills/"));

        if (_config.IncludeMemory && Directory.Exists(memoryDir))
            map.Add(("memory", new[] { memoryDir }, "memory/"));

        if (_config.IncludeSqliteDb)
        {
            var dbPath = Path.Combine(_dataRoot, "hercules.db");
            if (File.Exists(dbPath))
                map.Add(("sqlite", new[] { dbPath }, "hercules.db"));
        }

        if (_config.IncludeIdentity && Directory.Exists(identityDir))
            map.Add(("identity", new[] { identityDir }, "security/identity/"));

        return map;
    }

    private async Task<BackupEntry?> AddFileEntryAsync(string filePath, string relativePath, CancellationToken ct)
    {
        try
        {
            var fi = new FileInfo(filePath);
            await using var fs = File.OpenRead(filePath);
            var hash = await EncryptionService.ComputeFileHashAsync(fs, ct);

            return new BackupEntry
            {
                RelativePath = relativePath.Replace('\\', '/'),
                SourcePath = filePath,
                SizeBytes = fi.Length,
                LastModified = fi.LastWriteTimeUtc.ToString("O"),
                ContentHash = hash
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not hash file {Path}", filePath);
            return null;
        }
    }

    private async Task WriteManifestAsync(string id, BackupManifest manifest, CancellationToken ct)
    {
        // Write manifest alongside the archive using the same naming convention: {prefix}-{id}.manifest.json
        var path = Path.Combine(_config.BackupDir, $"{_config.FilePrefix}-{id}.manifest.json");
        await using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, manifest, JsonOptions);
    }

    private async Task<BackupManifest?> ReadManifestAsync(string id, CancellationToken ct)
    {
        var path = Path.Combine(_config.BackupDir, $"{_config.FilePrefix}-{id}.manifest.json");
        if (!File.Exists(path)) return null;
        await using var fs = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<BackupManifest>(fs, cancellationToken: ct);
    }

    private async Task EnforceRetentionAsync(CancellationToken ct)
    {
        if (_config.RetentionCount <= 0) return;

        var manifests = new List<(string Id, string CreatedAt)>();
        foreach (var mf in Directory.EnumerateFiles(_config.BackupDir, "*.manifest.json"))
        {
            try
            {
                await using var fs = File.OpenRead(mf);
                var m = await JsonSerializer.DeserializeAsync<BackupManifest>(fs, cancellationToken: ct);
                if (m != null)
                    manifests.Add((m.Id, m.CreatedAt));
            }
            catch { /* skip invalid manifest */ }
        }

        var toDelete = manifests
            .OrderByDescending(m => m.CreatedAt)
            .Skip(_config.RetentionCount)
            .Select(m => m.Id)
            .ToList();

        foreach (var id in toDelete)
        {
            await DeleteBackupAsync(id, ct);
            _logger.LogInformation("Retention policy deleted old backup {Id}", id);
        }
    }
}

// ─── ULID minimal implementation ─────────────────────────────────────────────────
file static class Ulid
{
    private static readonly RandomShared Random = new();

    public static string NewUlid()
    {
        byte[] bytes = new byte[16];
        Random.GetBytes(bytes);
        return Base32Crockford.Encode(bytes);
    }

    private static class Base32Crockford
    {
        private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        private static readonly char[] Table = Alphabet.ToCharArray();

        public static string Encode(byte[] bytes)
        {
            var sb = new StringBuilder(26);
            var bitBuffer = 0;
            var bitsLeft = 0;

            foreach (var b in bytes)
            {
                bitBuffer = (bitBuffer << 8) | b;
                bitsLeft += 8;
                while (bitsLeft >= 5)
                {
                    bitsLeft -= 5;
                    sb.Append(Table[(bitBuffer >> bitsLeft) & 31]);
                }
            }

            return sb.ToString();
        }
    }
}

file sealed class RandomShared
{
    private readonly byte[] _lock = new byte[0];
    private readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();

    public void GetBytes(Span<byte> bytes)
    {
        lock (_lock)
        {
            _rng.GetBytes(bytes);
        }
    }
}
