using Hercules.Config;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Hercules.Health;

/// <summary>
/// task_079: checks free disk space on the volume holding
/// <see cref="StorageConfig.DataRoot"/>. Degraded when below
/// <see cref="HealthChecksConfig.DiskDegradedMb"/>, Unhealthy when below
/// <see cref="HealthChecksConfig.DiskMinFreeMb"/>.
/// </summary>
public sealed class DiskSpaceHealthCheck : IHealthCheck
{
    private readonly StorageConfig _storage;
    private readonly HealthChecksConfig _healthCfg;
    private readonly ILogger<DiskSpaceHealthCheck> _log;

    public DiskSpaceHealthCheck(StorageConfig storage, HealthChecksConfig healthCfg, ILogger<DiskSpaceHealthCheck> log)
    {
        _storage = storage;
        _healthCfg = healthCfg;
        _log = log;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var path = string.IsNullOrWhiteSpace(_storage.DataRoot)
                ? Path.GetPathRoot(Directory.GetCurrentDirectory()) ?? "/"
                : _storage.DataRoot;

            // Ensure the directory exists so DriveInfo can resolve a volume on first run.
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Could not resolve volume for data root '{path}'"));
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy($"Drive '{root}' is not ready"));
            }

            var freeMb = (long)(drive.AvailableFreeSpace / (1024.0 * 1024.0));
            var data = new Dictionary<string, object>
            {
                ["freeMb"] = freeMb,
                ["totalMb"] = (long)(drive.TotalSize / (1024.0 * 1024.0)),
                ["volume"] = root
            };

            if (freeMb < _healthCfg.DiskMinFreeMb)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"Free disk space on {root} is {freeMb} MB (< {_healthCfg.DiskMinFreeMb} MB threshold)",
                    data: data));
            }

            if (freeMb < _healthCfg.DiskDegradedMb)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Free disk space on {root} is {freeMb} MB (< {_healthCfg.DiskDegradedMb} MB threshold)",
                    data: data));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                $"Free disk space on {root} is {freeMb} MB",
                data));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[HealthCheck] Disk space probe failed");
            return Task.FromResult(HealthCheckResult.Unhealthy("Disk space probe threw", ex));
        }
    }
}
