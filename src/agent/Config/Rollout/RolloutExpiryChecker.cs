using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hercules.Config.Rollout;

/// <summary>
///     Background service that periodically checks for expired bundles and retires them (task_058).
/// </summary>
public sealed class RolloutExpiryChecker : BackgroundService
{
    private readonly ConfigRolloutConfig _config;
    private readonly IRolloutManager _manager;
    private readonly ILogger<RolloutExpiryChecker> _logger;

    public RolloutExpiryChecker(
        ConfigRolloutConfig config,
        IRolloutManager manager,
        ILogger<RolloutExpiryChecker> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.EnableExpiryChecker)
        {
            _logger.LogInformation("Rollout expiry checker is disabled");
            return;
        }

        _logger.LogInformation(
            "Rollout expiry checker started (interval: {Interval} min)",
            _config.ExpiryCheckIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _manager.CheckExpiry();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during expiry check");
            }

            await Task.Delay(TimeSpan.FromMinutes(_config.ExpiryCheckIntervalMinutes), stoppingToken);
        }
    }
}
