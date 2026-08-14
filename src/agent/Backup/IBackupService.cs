namespace Hercules.Backup;

/// <summary>
///     Backup service interface (task_063).
/// </summary>
public interface IBackupService
{
    /// <summary>Creates a new backup archive of the configured components.</summary>
    Task<BackupResult> CreateBackupAsync(string? passphrase = null, CancellationToken ct = default);

    /// <summary>Restores a backup archive. Returns summary of restored files.</summary>
    Task<RestoreResult> RestoreAsync(string backupId, string? passphrase = null, string? targetDir = null, CancellationToken ct = default);

    /// <summary>Lists all available backup archives.</summary>
    Task<IReadOnlyList<BackupSummary>> ListBackupsAsync(CancellationToken ct = default);

    /// <summary>Verifies integrity and hash validity of a backup archive.</summary>
    Task<VerifyResult> VerifyBackupAsync(string backupId, string? passphrase = null, CancellationToken ct = default);

    /// <summary>Deletes a backup archive by ID.</summary>
    Task DeleteBackupAsync(string backupId, CancellationToken ct = default);
}
