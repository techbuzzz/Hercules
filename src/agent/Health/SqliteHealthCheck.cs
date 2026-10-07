using Hercules.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Hercules.Health;

/// <summary>
/// task_079: liveness probe for the shared SQLite session store.
/// Wraps <see cref="SqliteSessionStore.IsHealthy"/> so we surface real
/// store state instead of a static stub.
/// </summary>
public sealed class SqliteHealthCheck : IHealthCheck
{
    private readonly SqliteSessionStore _store;
    private readonly ILogger<SqliteHealthCheck> _log;

    public SqliteHealthCheck(SqliteSessionStore store, ILogger<SqliteHealthCheck> log)
    {
        _store = store;
        _log = log;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_store.IsHealthy())
            {
                return Task.FromResult(HealthCheckResult.Healthy("SQLite session store responded to SELECT 1"));
            }

            return Task.FromResult(HealthCheckResult.Unhealthy("SQLite session store did not respond to SELECT 1"));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[HealthCheck] Sqlite probe failed");
            return Task.FromResult(HealthCheckResult.Unhealthy("SQLite session store threw", ex));
        }
    }
}
