using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hercules.Supervisor;

/// <summary>
/// Owns the supervised agents and runs two loops:
/// health monitoring (restarts an agent whose health endpoint keeps failing) and
/// restart coordination (honours <c>POST /api/system/restart</c> issued by an operator).
/// The agent deliberately never kills itself; this process is that owner.
/// </summary>
public sealed class SupervisorService(
    IOptionsMonitor<SupervisorOptions> options,
    SupervisorApiClient apiClient,
    ILoggerFactory loggerFactory,
    ILogger<SupervisorService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, ManagedAgent> _agents = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _stopping = new();
    private IHttpClientFactory? _httpFactory;

    /// <summary>Timestamp of the restart request we already performed, for exactly-once handling.</summary>
    private DateTimeOffset? _lastHandledRequest;

    // ValueCollection only implements ICollection<T>; materialise instead of casting.
    public IReadOnlyCollection<ManagedAgent> Agents => _agents.Values.ToArray();

    public void Configure(IHttpClientFactory httpFactory) => _httpFactory = httpFactory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.CurrentValue;

        foreach (var agentOpts in opts.Agents)
        {
            if (string.IsNullOrWhiteSpace(agentOpts.Name))
            {
                logger.LogError("Agent entry without a name was ignored");
                continue;
            }
            if (string.IsNullOrWhiteSpace(agentOpts.Command))
            {
                logger.LogError("[{Name}] has no command; it will be monitored but not launched", agentOpts.Name);
            }
            var agent = new ManagedAgent(
                agentOpts,
                loggerFactory.CreateLogger($"Hercules.Supervisor.Agent.{agentOpts.Name}"))
            {
                PortFreeTimeoutSeconds = Math.Max(5, opts.RestartGraceSeconds),
            };
            _agents[agentOpts.Name] = agent;
        }

        if (_agents.IsEmpty)
        {
            logger.LogWarning("No agents configured; supervisor is idle");
            return;
        }

        if (opts.AutoStart)
        {
            foreach (var agent in _agents.Values) await SafeStartAsync(agent, stoppingToken);
        }

        // Separate loops so a slow restart never delays health polling.
        var health = Task.Run(() => HealthLoopAsync(opts, stoppingToken), CancellationToken.None);
        var restart = Task.Run(() => RestartLoopAsync(opts, stoppingToken), CancellationToken.None);
        await Task.WhenAll(health, restart);
    }

    private async Task SafeStartAsync(ManagedAgent agent, CancellationToken ct)
    {
        try
        {
            await agent.StartAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{Name}] start failed", agent.Name);
        }
    }

    private async Task HealthLoopAsync(SupervisorOptions opts, CancellationToken ct)
    {
        var cooldown = TimeSpan.FromSeconds(opts.RestartCooldownSeconds);
        while (!ct.IsCancellationRequested)
        {
            foreach (var agent in _agents.Values)
            {
                if (ct.IsCancellationRequested) return;
                try
                {
                    if (_httpFactory is null) continue;
                    using var http = _httpFactory.CreateClient("health");
                    var failure = await agent.ProbeHealthAsync(http, ct);
                    if (failure is null) continue;

                    logger.LogWarning(
                        "[{Name}] health check failed ({Reason}); consecutive={Count}/{Threshold}",
                        agent.Name, failure, agent.ConsecutiveFailures, opts.UnhealthyThreshold);

                    if (agent.Options.AutoRestart
                        && agent.ConsecutiveFailures >= opts.UnhealthyThreshold
                        && !string.IsNullOrWhiteSpace(agent.Options.Command))
                    {
                        await agent.RestartAsync($"health check failed: {failure}", cooldown, ct);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "[{Name}] health loop error", agent.Name);
                }

                // Each agent has its own cadence; sleep after handling it.
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, agent.Options.HealthIntervalSeconds)), ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task RestartLoopAsync(SupervisorOptions opts, CancellationToken ct)
    {
        var cooldown = TimeSpan.FromSeconds(opts.RestartCooldownSeconds);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var state = await apiClient.GetRestartPendingAsync(ct);
                if (state is { Pending: true })
                    await HandleRestartRequestAsync(state, cooldown, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Restart poll failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, opts.RestartPollSeconds)), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Performs an operator restart request exactly once.
    /// <para>
    /// A request means "perform a restart", not "keep restarting until healthy".
    /// Retrying on a slow boot turns one operator action into a restart loop, and
    /// recovery from a failed boot is the health loop's job (AutoRestart), not ours.
    /// </para>
    /// </summary>
    private async Task HandleRestartRequestAsync(
        RestartPendingResponse state, TimeSpan cooldown, CancellationToken ct)
    {
        // Already acted on this exact request (e.g. the flag outlived the agent
        // we just killed). Only make sure it gets cleared.
        if (state.RequestedAt is { } requestedAt && requestedAt == _lastHandledRequest)
        {
            await ClearRestartAsync(ct);
            return;
        }

        var primary = _agents.Values.FirstOrDefault(a => a.Options.Primary)
                     ?? _agents.Values.FirstOrDefault();
        if (primary is null)
        {
            logger.LogWarning("Restart requested but no agent is configured to handle it");
            await ClearRestartAsync(ct);
            return;
        }

        var reason = state.Reason is { Length: > 0 } r
            ? $"operator request: {r}"
            : "operator request";
        await primary.RestartAsync(reason, cooldown, ct);

        if (state.RequestedAt is { } at) _lastHandledRequest = at;
        await ClearRestartAsync(ct);
    }

    /// <summary>
    /// Clears the restart flag. The agent may already be down (we just killed it),
    /// so a failure is expected and only logged — the flag is re-read next tick and
    /// the already-handled guard above prevents a second restart.
    /// </summary>
    private async Task ClearRestartAsync(CancellationToken ct)
    {
        try
        {
            await apiClient.ClearRestartAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex, "Could not clear restart flag (agent may still be booting); will retry next poll");
        }
    }

    /// <summary>Stops every agent so children never outlive the supervisor.</summary>
    public async Task StopAllAsync()
    {
        if (!_stopping.IsCancellationRequested) await _stopping.CancelAsync();
        foreach (var agent in _agents.Values)
        {
            try
            {
                await agent.DisposeAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{Name}] dispose failed", agent.Name);
            }
        }
    }
}