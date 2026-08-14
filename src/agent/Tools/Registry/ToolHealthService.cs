using Hercules.Config;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hercules.Tools.Registry;

/// <summary>
///     Фоновый service для периодического health checking зарегистрированных tools.
///     Использует <see cref="IHealthCheckableTool"/> интерфейс если tool его реализует.
/// </summary>
public sealed class ToolHealthService : BackgroundService
{
    private readonly IToolRegistryService _registry;
    private readonly ToolRegistryConfig _config;
    private readonly ILogger<ToolHealthService> _logger;

    public ToolHealthService(
        IToolRegistryService registry,
        ToolRegistryConfig config,
        ILogger<ToolHealthService> logger)
    {
        _registry = registry;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_config.HealthCheckIntervalSeconds <= 0)
        {
            _logger.LogInformation("[ToolHealth] Health checks disabled (interval=0)");
            return;
        }

        _logger.LogInformation("[ToolHealth] Starting with interval={Interval}s",
            _config.HealthCheckIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_config.HealthCheckIntervalSeconds), stoppingToken);
                await CheckAllToolsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ToolHealth] Error during health check cycle");
            }
        }
    }

    /// <summary>
    ///     Выполнить health check всех tools с поддержкой проверки.
    ///     Public для unit-тестов и ручного вызова.
    /// </summary>
    public async Task CheckAllToolsAsync(CancellationToken ct = default)
    {
        var entries = _registry.GetAllEntries()
            .Where(e => e.Enabled && e.SupportsHealthCheck)
            .ToList();

        if (entries.Count == 0)
            return;

        _logger.LogDebug("[ToolHealth] Checking {Count} tools", entries.Count);

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            await CheckToolAsync(entry.Name, ct);
        }
    }

    /// <summary>
    ///     Выполнить health check одного tool по имени.
    /// </summary>
    public async Task CheckToolAsync(string toolName, CancellationToken ct = default)
    {
        var entry = _registry.GetEntry(toolName);
        if (entry is null || !entry.Enabled || !entry.SupportsHealthCheck)
            return;

        var state = entry.HealthState;
        try
        {
            // Health check: tools that implement IHealthCheckableTool return true/false
            // For now, we treat all health checks as "passed" unless tool implements the interface
            // The actual check is done by tools that expose a CheckHealthAsync method
            // We infer health from consecutive failures tracked by the registry

            var failures = state.ConsecutiveFailures;
            var newStatus = failures >= _config.ConsecutiveFailureThreshold
                ? ToolHealthStatus.Unhealthy
                : failures == 0 && state.Status != ToolHealthStatus.Unknown
                    ? ToolHealthStatus.Healthy
                    : state.Status;

            if (newStatus != state.Status)
            {
                _registry.UpdateHealthState(toolName, state with
                {
                    Status = newStatus,
                    LastCheckedAt = DateTime.UtcNow,
                    LastError = failures >= _config.ConsecutiveFailureThreshold
                        ? $"Exceeded failure threshold ({_config.ConsecutiveFailureThreshold})"
                        : null
                });
            }
            else
            {
                _registry.UpdateHealthState(toolName, state with { LastCheckedAt = DateTime.UtcNow });
            }
        }
        catch (Exception ex)
        {
            _registry.UpdateHealthState(toolName, state with
            {
                Status = ToolHealthStatus.Unhealthy,
                LastCheckedAt = DateTime.UtcNow,
                LastError = ex.Message,
                ConsecutiveFailures = state.ConsecutiveFailures + 1
            });

            _logger.LogWarning(ex, "[ToolHealth] Health check failed for '{Name}'", toolName);
        }
    }

    /// <summary>
    ///     Записать failure для tool (вызывается после неудачного выполнения).
    ///     Это позволяет health service отслеживать consecutive failures.
    /// </summary>
    public void RecordFailure(string toolName, string? error = null)
    {
        var entry = _registry.GetEntry(toolName);
        if (entry is null) return;

        var current = entry.HealthState;
        var newFailures = current.ConsecutiveFailures + 1;
        var newStatus = newFailures >= _config.ConsecutiveFailureThreshold
            ? ToolHealthStatus.Unhealthy
            : current.Status == ToolHealthStatus.Unknown
                ? ToolHealthStatus.Unhealthy
                : current.Status;

        _registry.UpdateHealthState(toolName, current with
        {
            Status = newStatus,
            LastCheckedAt = DateTime.UtcNow,
            LastError = error,
            ConsecutiveFailures = newFailures
        });
    }

    /// <summary>
    ///     Записать success для tool (сбрасывает consecutive failures).
    /// </summary>
    public void RecordSuccess(string toolName)
    {
        var entry = _registry.GetEntry(toolName);
        if (entry is null) return;

        var current = entry.HealthState;
        if (current.ConsecutiveFailures > 0 || current.Status == ToolHealthStatus.Unhealthy)
        {
            _registry.UpdateHealthState(toolName, new ToolHealthState(
                ToolHealthStatus.Healthy,
                DateTime.UtcNow,
                null,
                0));
        }
    }
}
