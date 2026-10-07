using Hercules.Offline;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Hercules.Health;

/// <summary>
/// task_079: counts pending items in the offline outbox and reports
/// <c>Degraded</c> when the count exceeds <see cref="HealthChecksConfig.OutboxMaxPending"/>.
/// Optional: if no <see cref="IOutboxStore"/> is registered, the check is a no-op Healthy.
/// </summary>
public sealed class OutboxHealthCheck : IHealthCheck
{
    private readonly IOutboxStore? _store;
    private readonly HealthChecksConfig _healthCfg;
    private readonly ILogger<OutboxHealthCheck> _log;

    public OutboxHealthCheck(
        IOutboxStore? store,
        HealthChecksConfig healthCfg,
        ILogger<OutboxHealthCheck> log)
    {
        _store = store;
        _healthCfg = healthCfg;
        _log = log;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_store is null)
        {
            return HealthCheckResult.Healthy("Outbox store not registered; health check skipped");
        }

        try
        {
            var pending = await _store.GetPendingCountAsync(cancellationToken).ConfigureAwait(false);
            var data = new Dictionary<string, object> { ["pending"] = pending };

            if (pending > _healthCfg.OutboxMaxPending)
            {
                return HealthCheckResult.Degraded(
                    $"Outbox has {pending} pending items (> {_healthCfg.OutboxMaxPending} threshold)",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                $"Outbox has {pending} pending items",
                data);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[HealthCheck] Outbox probe failed");
            return HealthCheckResult.Unhealthy("Outbox probe threw", ex);
        }
    }
}
