using Hercules.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Hercules.Health;

/// <summary>
/// task_079: probes the file-based skill repository by enumerating the
/// <c>Skills/</c> directory. Unhealthy when enumeration throws.
/// Optional: a no-op Healthy when the repo is not registered.
/// </summary>
public sealed class SkillRegistryHealthCheck : IHealthCheck
{
    private readonly FileSkillRepository? _repo;
    private readonly ILogger<SkillRegistryHealthCheck> _log;

    public SkillRegistryHealthCheck(FileSkillRepository? repo, ILogger<SkillRegistryHealthCheck> log)
    {
        _repo = repo;
        _log = log;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_repo is null)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Skill registry not registered; health check skipped"));
        }

        try
        {
            var skills = _repo.LoadAll();
            return Task.FromResult(HealthCheckResult.Healthy(
                $"Skill registry returned {skills.Count} skills",
                new Dictionary<string, object> { ["count"] = skills.Count }));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[HealthCheck] Skill registry probe failed");
            return Task.FromResult(HealthCheckResult.Unhealthy("Skill registry probe threw", ex));
        }
    }
}
