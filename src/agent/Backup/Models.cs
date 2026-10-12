using System.Text.Json.Serialization;

namespace Hercules.Backup;

/// <summary>
///     Backup configuration (task_063).
/// </summary>
public sealed class BackupConfig
{
    /// <summary>Enable automatic backups. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Backup archive storage directory. Default: "data/backups".</summary>
    public string BackupDir { get; set; } = "data/backups";

    /// <summary>Encryption algorithm: "AES-256-GCM". Default: AES-256-GCM.</summary>
    public string EncryptionAlgorithm { get; set; } = "AES-256-GCM";

    /// <summary>
    ///     Backup passphrase. If empty — backups are not encrypted.
    ///     It is strongly recommended to set this via environment variable HERCULES_BACKUP_PASSPHRASE.
    /// </summary>
    public string Passphrase { get; set; } = "";

    /// <summary>
    ///     [task_087] If true and <see cref="Passphrase"/> is empty, log an
    ///     error (instead of a warning) at scheduler startup so production
    ///     deployments fail loudly when backups would be created unencrypted.
    ///     Default: false (warning only — dev-friendly).
    /// </summary>
    public bool RequirePassphrase { get; set; } = false;

    /// <summary>Include config files in backup. Default: true.</summary>
    public bool IncludeConfig { get; set; } = true;

    /// <summary>Include skills in backup. Default: true.</summary>
    public bool IncludeSkills { get; set; } = true;

    /// <summary>Include selected memory files in backup. Default: true.</summary>
    public bool IncludeMemory { get; set; } = true;

    /// <summary>Include SQLite database in backup. Default: true.</summary>
    public bool IncludeSqliteDb { get; set; } = true;

    /// <summary>Include device identity in backup. Default: true.</summary>
    public bool IncludeIdentity { get; set; } = true;

    /// <summary>
    ///     Retention: number of backups to keep. Older archives are deleted after new backup completes.
    ///     Default: 10. 0 = keep all.
    /// </summary>
    public int RetentionCount { get; set; } = 10;

    /// <summary>Interval between automatic backups in hours. Default: 24.</summary>
    public int IntervalHours { get; set; } = 24;

    /// <summary>Maximum backup archive size in MB. Default: 500.</summary>
    public long MaxSizeMb { get; set; } = 500;

    /// <summary>Backup on startup. Default: false.</summary>
    public bool BackupOnStartup { get; set; } = false;

    /// <summary>Backup filename prefix. Default: "hercules-backup".</summary>
    public string FilePrefix { get; set; } = "hercules-backup";
}

/// <summary>
///     Metadata stored inside each backup archive (backup-manifest.json).
/// </summary>
public sealed class BackupManifest
{
    /// <summary>Unique backup ID (ULID).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>When the backup was created.</summary>
    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = "";

    /// <summary>Hercules version that created this backup.</summary>
    [JsonPropertyName("hercules_version")]
    public string HerculesVersion { get; set; } = "";

    /// <summary>Hostname of the machine where backup was created.</summary>
    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = "";

    /// <summary>Files included in the backup (relative paths inside the archive).</summary>
    [JsonPropertyName("entries")]
    public List<BackupEntry> Entries { get; set; } = new();

    /// <summary>Total uncompressed size in bytes.</summary>
    [JsonPropertyName("uncompressed_size_bytes")]
    public long UncompressedSizeBytes { get; set; }

    /// <summary>Total compressed size in bytes (after encryption).</summary>
    [JsonPropertyName("compressed_size_bytes")]
    public long CompressedSizeBytes { get; set; }

    /// <summary>Whether the archive is encrypted.</summary>
    [JsonPropertyName("encrypted")]
    public bool Encrypted { get; set; }

    /// <summary>
    ///     SHA-256 fingerprint of the master key (first 16 hex chars) used for encryption.
    ///     Null if not encrypted.
    /// </summary>
    [JsonPropertyName("key_fingerprint")]
    public string? KeyFingerprint { get; set; }

    /// <summary>Which components were included.</summary>
    [JsonPropertyName("components")]
    public BackupComponents Components { get; set; } = new();
}

/// <summary>
///     Which components were included in the backup.
/// </summary>
public sealed class BackupComponents
{
    [JsonPropertyName("config")]
    public bool Config { get; set; }

    [JsonPropertyName("skills")]
    public bool Skills { get; set; }

    [JsonPropertyName("memory")]
    public bool Memory { get; set; }

    [JsonPropertyName("sqlite_db")]
    public bool SqliteDb { get; set; }

    [JsonPropertyName("identity")]
    public bool Identity { get; set; }
}

/// <summary>
///     One file entry inside a backup archive.
/// </summary>
public sealed class BackupEntry
{
    /// <summary>Relative path inside the archive.</summary>
    [JsonPropertyName("relative_path")]
    public string RelativePath { get; set; } = "";

    /// <summary>Original source path (absolute or relative to data root).</summary>
    [JsonPropertyName("source_path")]
    public string SourcePath { get; set; } = "";

    /// <summary>Original file size in bytes.</summary>
    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; set; }

    /// <summary>When the file was last modified.</summary>
    [JsonPropertyName("last_modified")]
    public string LastModified { get; set; } = "";

    /// <summary>SHA-256 hash of the file content (before encryption).</summary>
    [JsonPropertyName("content_hash")]
    public string ContentHash { get; set; } = "";
}

/// <summary>
///     Result of a backup creation operation.
/// </summary>
public sealed record BackupResult(
    string BackupId,
    string ArchivePath,
    long SizeBytes,
    long UncompressedBytes,
    int FilesCount,
    bool Encrypted,
    string? KeyFingerprint,
    TimeSpan Duration,
    List<string> Warnings);

/// <summary>
///     Result of a restore operation.
/// </summary>
public sealed record RestoreResult(
    string BackupId,
    bool Success,
    int FilesRestored,
    int FilesSkipped,
    List<string> Errors,
    List<string> Warnings,
    TimeSpan Duration);

/// <summary>
///     Result of a backup verification operation.
/// </summary>
public sealed record VerifyResult(
    string BackupId,
    bool Valid,
    bool Encrypted,
    string? KeyFingerprint,
    List<string> FileHashesValid,
    List<string> FileHashesInvalid,
    List<string> Warnings,
    TimeSpan Duration);

/// <summary>
///     Summary of a backup archive (returned by ListBackupsAsync).
/// </summary>
public sealed class BackupSummary
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("filename")]
    public string Filename { get; set; } = "";

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = "";

    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("encrypted")]
    public bool Encrypted { get; set; }

    [JsonPropertyName("key_fingerprint")]
    public string? KeyFingerprint { get; set; }

    [JsonPropertyName("components")]
    public BackupComponents Components { get; set; } = new();

    [JsonPropertyName("files_count")]
    public int FilesCount { get; set; }
}
