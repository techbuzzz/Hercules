using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hercules.Backup;

/// <summary>
///     Background service that runs scheduled backups at the configured interval (task_063).
/// </summary>
public sealed class BackupScheduler : BackgroundService
{
    private readonly IBackupService _backup;
    private readonly BackupConfig _config;
    private readonly ILogger<BackupScheduler> _logger;

    public BackupScheduler(
        IBackupService backup,
        BackupConfig config,
        ILogger<BackupScheduler> logger)
    {
        _backup = backup ?? throw new ArgumentNullException(nameof(backup));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.Enabled)
        {
            _logger.LogInformation("Backup scheduler is disabled");
            return;
        }

        // [task_087] Warn at startup if backups are unencrypted (Passphrase empty).
        // The Passphrase is intentionally empty in dev to make onboarding easy, but
        // production deployments must set HERCULES_BACKUP_PASSPHRASE or set
        // Backup.RequirePassphrase=true to fail-fast.
        if (string.IsNullOrEmpty(_config.Passphrase))
        {
            if (_config.RequirePassphrase)
            {
                _logger.LogError(
                    "Backup.Passphrase is empty but Backup.RequirePassphrase=true — backups will be created unencrypted. " +
                    "Set HERCULES_BACKUP_PASSPHRASE or Backup.Passphrase in configuration.");
            }
            else
            {
                _logger.LogWarning(
                    "Backup.Passphrase is empty — backups will be created UNENCRYPTED. " +
                    "Set HERCULES_BACKUP_PASSPHRASE env var or Backup.Passphrase to enable AES-256-GCM encryption.");
            }
        }

        _logger.LogInformation(
            "Backup scheduler started: interval={IntervalHours}h, retention={Retention}",
            _config.IntervalHours, _config.RetentionCount);

        // Optional backup-on-startup
        if (_config.BackupOnStartup)
        {
            _logger.LogInformation("Running startup backup");
            try
            {
                await _backup.CreateBackupAsync(ct: stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Startup backup failed");
            }
        }

        var interval = TimeSpan.FromHours(_config.IntervalHours);
        var nextRun = DateTimeOffset.UtcNow.Add(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = nextRun - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, stoppingToken);
                }

                if (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Scheduled backup starting");
                    var result = await _backup.CreateBackupAsync(ct: stoppingToken);
                    _logger.LogInformation(
                        "Scheduled backup {Id} completed: {Files} files, {SizeKB} KB in {Ms}ms",
                        result.BackupId, result.FilesCount,
                        result.SizeBytes / 1024, result.Duration.TotalMilliseconds);
                    nextRun = DateTimeOffset.UtcNow.Add(interval);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled backup failed — will retry next interval");
                nextRun = DateTimeOffset.UtcNow.Add(interval);
            }
        }

        _logger.LogInformation("Backup scheduler stopped");
    }
}
