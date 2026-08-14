using System.Diagnostics;
using System.Net.Http.Json;
using Hercules.Config;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh;

/// <summary>
///     Фоновый service для периодического health-check агентов в capability registry.
///     Также выполняет cleanup просроченных записей (TTL expiry).
/// </summary>
public sealed class CapabilityHealthService : BackgroundService
{
    private readonly ICapabilityRegistryService _registry;
    private readonly MeshConfig _config;
    private readonly HttpClient _http;
    private readonly ILogger<CapabilityHealthService> _logger;

    public CapabilityHealthService(
        ICapabilityRegistryService registry,
        MeshConfig config,
        HttpClient httpClient,
        ILogger<CapabilityHealthService> logger)
    {
        _registry = registry;
        _config = config;
        _http = httpClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = _config.CapabilityHealthCheckIntervalSeconds;
        if (interval <= 0)
        {
            _logger.LogInformation("[CapHealth] Health checks disabled (interval=0)");
            return;
        }

        _logger.LogInformation("[CapHealth] Starting with interval={Interval}s, failureThreshold={Threshold}",
            interval, _config.CapabilityHealthFailureThreshold);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[CapHealth] Error during health check cycle");
            }
        }
    }

    /// <summary>Выполнить один цикл: TTL cleanup + health-check всех агентов.</summary>
    public async Task RunCycleAsync(CancellationToken ct = default)
    {
        int cleaned = _registry.CleanupExpired();
        if (cleaned > 0)
        {
            _logger.LogInformation("[CapHealth] Cleaned up {Count} expired agents", cleaned);
        }

        var agents = _registry.ListAll();
        if (agents.Count == 0)
        {
            _logger.LogDebug("[CapHealth] No agents to check");
            return;
        }

        _logger.LogDebug("[CapHealth] Checking {Count} agents", agents.Count);

        foreach (var agent in agents)
        {
            ct.ThrowIfCancellationRequested();
            await CheckAgentAsync(agent, ct);
        }
    }

    /// <summary>Выполнить health-check одного агента по его endpoint.</summary>
    public async Task CheckAgentAsync(RegistryAgentFullEntry agent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(agent.Endpoint))
        {
            return;
        }

        // Construct health endpoint: append /api/health or use the endpoint itself
        var healthUrl = agent.Endpoint.TrimEnd('/') + "/api/health";

        var sw = Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, healthUrl);
            using HttpResponseMessage resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();

            var status = resp.IsSuccessStatusCode ? AgentHealthStatus.Healthy : AgentHealthStatus.Unhealthy;
            _registry.UpdateHealthStatus(agent.AgentId, status, null, status == AgentHealthStatus.Healthy ? 0 : agent.ConsecutiveFailures + 1);
            _registry.UpdateLatencyHint(agent.AgentId, (int)sw.ElapsedMilliseconds);

            if (status == AgentHealthStatus.Unhealthy)
            {
                int failures = agent.ConsecutiveFailures + 1;
                _logger.LogWarning("[CapHealth] Agent {Id} health-check failed ({StatusCode}) after {Ms}ms",
                    agent.AgentId, (int)resp.StatusCode, sw.ElapsedMilliseconds);

                if (failures >= _config.CapabilityHealthFailureThreshold)
                {
                    _registry.UpdateHealthStatus(agent.AgentId, AgentHealthStatus.Unhealthy, null, failures);
                }
            }
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            int failures = agent.ConsecutiveFailures + 1;
            _logger.LogWarning(ex, "[CapHealth] Agent {Id} unreachable after {Ms}ms", agent.AgentId, sw.ElapsedMilliseconds);

            _registry.UpdateHealthStatus(agent.AgentId, AgentHealthStatus.Unreachable, ex.Message, failures);
            if (failures >= _config.CapabilityHealthFailureThreshold)
            {
                _registry.UpdateHealthStatus(agent.AgentId, AgentHealthStatus.Unhealthy, null, failures);
            }
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "[CapHealth] Agent {Id} health-check error", agent.AgentId);
        }
    }

    /// <summary>
    ///     Записать success для агента (сброс consecutive failures).
    ///     Вызывается после успешного inter-agent вызова.
    /// </summary>
    public void RecordSuccess(string agentId)
    {
        _registry.UpdateHealthStatus(agentId, AgentHealthStatus.Healthy);
    }

    /// <summary>
    ///     Записать failure для агента (увеличить consecutive failures).
    ///     Вызывается после неудачного inter-agent вызова.
    /// </summary>
    public void RecordFailure(string agentId)
    {
        var entry = _registry.GetEntry(agentId);
        if (entry is null) return;

        // Increment consecutive failures from the current stored value
        int failures = entry.ConsecutiveFailures + 1;
        var status = failures >= _config.CapabilityHealthFailureThreshold
            ? AgentHealthStatus.Unhealthy
            : AgentHealthStatus.Unreachable;

        // Pass the incremented failures count explicitly so it's not reset to 0/1
        _registry.UpdateHealthStatus(agentId, status, null, failures);
    }
}
