using System.IO;
using System.IO.Compression;
using Hercules.Backup;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Backup;

public class BackupServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dataRoot;
    private readonly BackupConfig _config;

    public BackupServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-backup-test-{Guid.NewGuid():N}");
        _dataRoot = Path.Combine(_tempDir, "data");
        Directory.CreateDirectory(_dataRoot);
        Directory.CreateDirectory(Path.Combine(_dataRoot, "config"));
        Directory.CreateDirectory(Path.Combine(_dataRoot, "skills"));
        Directory.CreateDirectory(Path.Combine(_dataRoot, "memory"));
        Directory.CreateDirectory(Path.Combine(_dataRoot, "security", "identity"));

        File.WriteAllText(Path.Combine(_dataRoot, "config", "appsettings.json"), "{\"key\": \"value\"}");
        File.WriteAllText(Path.Combine(_dataRoot, "skills", "test-skill.md"), "# Test Skill");
        File.WriteAllText(Path.Combine(_dataRoot, "memory", "fact.md"), "Test fact content");
        File.WriteAllBytes(Path.Combine(_dataRoot, "hercules.db"), new byte[] { 1, 2, 3 });

        _config = new BackupConfig
        {
            Enabled = true,
            BackupDir = Path.Combine(_tempDir, "backups"),
            Passphrase = "test-passphrase-123",
            IncludeConfig = true,
            IncludeSkills = true,
            IncludeMemory = true,
            IncludeSqliteDb = true,
            IncludeIdentity = false,
            RetentionCount = 3,
            FilePrefix = "test-backup"
        };
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore cleanup errors */ }
    }

    private BackupService CreateService() => new(
        _config,
        new EncryptionService(NullLogger<EncryptionService>.Instance),
        NullLogger<BackupService>.Instance,
        _dataRoot);

    [Fact]
    public async Task CreateBackupAsync_Encrypted_Creates_Archive()
    {
        var svc = CreateService();
        var result = await svc.CreateBackupAsync();

        Assert.NotEmpty(result.BackupId);
        Assert.True(result.SizeBytes > 0);
        Assert.True(result.UncompressedBytes > 0);
        Assert.True(result.FilesCount > 0);
        Assert.True(result.Encrypted);
        Assert.NotNull(result.KeyFingerprint);
        Assert.True(File.Exists(result.ArchivePath));
    }

    [Fact]
    public async Task CreateBackupAsync_Unencrypted_Creates_Archive()
    {
        var unencryptedConfig = new BackupConfig
        {
            BackupDir = Path.Combine(_tempDir, "backups2"),
            Passphrase = "",
            IncludeConfig = true,
            FilePrefix = "unenc"
        };
        var svc = new BackupService(
            unencryptedConfig,
            new EncryptionService(NullLogger<EncryptionService>.Instance),
            NullLogger<BackupService>.Instance,
            _dataRoot);

        var result = await svc.CreateBackupAsync();

        Assert.False(result.Encrypted);
        Assert.True(result.SizeBytes > 0);
        Assert.NotEmpty(result.BackupId);
    }

    [Fact]
    public async Task ListBackupsAsync_Returns_At_Least_One()
    {
        var svc = CreateService();
        var result = await svc.CreateBackupAsync();

        // Diagnostic: verify backup directory contents
        var backupDir = _config.BackupDir;
        var dirExists = Directory.Exists(backupDir);
        var allFiles = dirExists ? Directory.GetFiles(backupDir) : Array.Empty<string>();
        var parentDir = Path.GetDirectoryName(backupDir) ?? "";
        var parentFiles = Directory.Exists(parentDir) ? Directory.GetFiles(parentDir) : Array.Empty<string>();
        Assert.True(dirExists,
            "Backup dir does not exist: " + backupDir + ". All files in parent: " + string.Join(", ", parentFiles.Select(Path.GetFileName)) + ". Config BackupDir: " + _config.BackupDir);

        var backups = await svc.ListBackupsAsync();
        Assert.True(backups.Count >= 1, $"Expected >= 1 backup, got {backups.Count}");
        var latest = backups[0];
        Assert.True(latest.Encrypted);
        Assert.NotEmpty(latest.Id);
        Assert.True(latest.SizeBytes > 0);
    }

    [Fact]
    public async Task RestoreAsync_Restores_Files()
    {
        var svc = CreateService();
        await svc.CreateBackupAsync();
        var backups = await svc.ListBackupsAsync();
        var latestId = backups[0].Id;

        var restoreDir = Path.Combine(_tempDir, "restore-target");
        Directory.CreateDirectory(restoreDir);

        var result = await svc.RestoreAsync(latestId, "test-passphrase-123", restoreDir);

        Assert.True(result.Success, $"Restore failed: {string.Join(", ", result.Errors)}");
        Assert.True(result.FilesRestored > 0);
    }

    [Fact]
    public async Task RestoreAsync_Wrong_Passphrase_Returns_Error()
    {
        var svc = CreateService();
        await svc.CreateBackupAsync();
        var backups = await svc.ListBackupsAsync();
        var latestId = backups[0].Id;

        var result = await svc.RestoreAsync(latestId, "wrong-passphrase");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task RestoreAsync_Nonexistent_Backup_Returns_Error()
    {
        var svc = CreateService();
        var result = await svc.RestoreAsync("nonexistent-id-12345");
        Assert.False(result.Success);
    }

    [Fact]
    public async Task DeleteBackupAsync_Removes_Archive()
    {
        var svc = CreateService();
        var createResult = await svc.CreateBackupAsync();
        var archivePath = createResult.ArchivePath;

        await svc.DeleteBackupAsync(createResult.BackupId);

        Assert.False(File.Exists(archivePath));
    }

    [Fact]
    public async Task Retention_Enforces_Count()
    {
        var svc = CreateService();
        for (int i = 0; i < 5; i++)
            await svc.CreateBackupAsync();

        var backups = await svc.ListBackupsAsync();
        Assert.True(backups.Count <= _config.RetentionCount,
            $"Expected <= {_config.RetentionCount} backups, got {backups.Count}");
    }

    [Fact]
    public async Task CreateBackupAsync_With_No_Source_Files_Returns_Zero_Archive()
    {
        var emptyConfig = new BackupConfig
        {
            BackupDir = Path.Combine(_tempDir, "backups-empty"),
            Passphrase = "",
            IncludeConfig = false,
            IncludeSkills = false,
            IncludeMemory = false,
            IncludeSqliteDb = false,
            FilePrefix = "empty"
        };
        var svc = new BackupService(
            emptyConfig,
            new EncryptionService(NullLogger<EncryptionService>.Instance),
            NullLogger<BackupService>.Instance,
            _dataRoot);

        var result = await svc.CreateBackupAsync();

        Assert.Equal(0, result.FilesCount);
    }

    [Fact]
    public async Task CreateBackupAsync_Produces_Valid_Encrypted_Blob()
    {
        var svc = CreateService();
        var result = await svc.CreateBackupAsync();
        var rawBytes = await File.ReadAllBytesAsync(result.ArchivePath);

        var salt = rawBytes.AsSpan(0, 16).ToArray();
        var encryptedPayload = rawBytes.AsSpan(16).ToArray();

        var encSvc = new EncryptionService(NullLogger<EncryptionService>.Instance);
        encSvc.DeriveKeyWithSalt("test-passphrase-123", salt, out var key, out var _);
        var zipBytes = encSvc.Decrypt(encryptedPayload, key);

        using var zipStream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var entries = archive.Entries.ToList();

        Assert.True(entries.Count > 0);
        Assert.Contains(entries, e => e.Name == "backup-manifest.json");
    }
}
