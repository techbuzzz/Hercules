using Hercules.Mesh.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Hercules.Health;

/// <summary>
/// task_079: probes the configured mesh bus via <see cref="IMeshBus.IsHealthyAsync"/>.
/// In-process bus always reports Healthy; Redis/Postgres/NATS backends return
/// real connectivity. When mesh is disabled this is a no-op Healthy.
/// </summary>
public sealed class MeshBusHealthCheck : IHealthCheck
{
    private readonly IMeshBus? _bus;
    private readonly ILogger<MeshBusHealthCheck> _log;

    public MeshBusHealthCheck(IMeshBus? bus, ILogger<MeshBusHealthCheck> log)
    {
        _bus = bus;
        _log = log;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_bus is null)
        {
            return HealthCheckResult.Healthy("Mesh bus not registered; health check skipped");
        }

        try
        {
            var ok = await _bus.IsHealthyAsync(cancellationToken).ConfigureAwait(false);
            return ok
                ? HealthCheckResult.Healthy($"Mesh bus '{_bus.BackendKind}' responded to ping")
                : HealthCheckResult.Unhealthy($"Mesh bus '{_bus.BackendKind}' ping returned false");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[HealthCheck] Mesh bus probe failed for backend {Backend}", _bus.BackendKind);
            return HealthCheckResult.Unhealthy($"Mesh bus '{_bus.BackendKind}' threw", ex);
        }
    }
}
