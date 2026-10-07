using Hercules.Agent;
using Hercules.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hercules.Lifecycle;

/// <summary>
///     Bridges <see cref="IHostApplicationLifetime" /> shutdown with the agent's graceful
///     drain logic. <see cref="StopAsync" /> is awaited by the .NET host, so by registering
///     this service we guarantee that the host won't return from <c>RunAsync</c> until
///     <see cref="ILifecycleService.DrainAgentAsync" /> has finished waiting for in-flight
///     requests (or its timeout has elapsed).
///     Specification: task_080.
/// </summary>
public sealed class DrainHostedService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly IAgentLifecycleState _state;
    private readonly ShutdownConfig _shutdown;
    private readonly ILogger<DrainHostedService> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    public DrainHostedService(
        IServiceProvider services,
        IAgentLifecycleState state,
        ShutdownConfig shutdown,
        IHostApplicationLifetime lifetime,
        ILogger<DrainHostedService> logger)
    {
        _services = services;
        _state = state;
        _shutdown = shutdown;
        _lifetime = lifetime;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_shutdown.Enabled)
        {
            _logger.LogInformation("[DrainHostedService] Shutdown drain disabled in config — skipping");
            return;
        }

        // The host calls StopAsync on registered services. We're typically the last
        // one registered, so this fires after all other services have already begun
        // their own shutdown. By this point cancellation tokens are signalled but
        // pending HTTP responses can still complete.
        if (_state.State == AgentLifecycleState.Stopped ||
            _state.State == AgentLifecycleState.Decommissioned)
        {
            _logger.LogInformation(
                "[DrainHostedService] Agent already in terminal state {State} — no drain needed",
                _state.State);
            return;
        }

        try
        {
            var lifecycle = _services.GetRequiredService<ILifecycleService>();
            var agent = _services.GetService<AgentCore>();
            var localAgentId = agent?.SessionId ?? "local";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await lifecycle.DrainAgentAsync(localAgentId, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            _logger.LogInformation(
                "[DrainHostedService] Drain completed in {ElapsedMs}ms — drainedCleanly={DrainedCleanly} remaining={Remaining}",
                sw.ElapsedMilliseconds,
                result.Metadata.GetValueOrDefault("drainedCleanly"),
                result.Metadata.GetValueOrDefault("remainingInFlight"));
        }
        catch (Exception ex)
        {
            // Don't let drain failure crash the host. We still want the process to exit
            // so other shutdown logic can run; K8s will treat the pod as failed if needed.
            _logger.LogError(ex, "[DrainHostedService] Drain failed: {Error}", ex.Message);
        }
    }
}
