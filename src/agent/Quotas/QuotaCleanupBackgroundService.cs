using Hercules.Config;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hercules.Quotas;

/// <summary>
///     Периодически вызывает <see cref="QuotaService.SweepAllBuckets"/>, чтобы
///     rate-limit buckets (per <c>scope:scopeId:type</c>) не накапливались
///     бесконечно между обращениями к <see cref="IQuotaService.GetStatus"/>.
///     Без этого очистка происходила только on-demand при insert/query и в
///     редко запрашиваемых корзинах entries копились неделями.
///     Спецификация: task_072.
/// </summary>
public sealed class QuotaCleanupBackgroundService : BackgroundService
{
    private readonly IQuotaService _quotaService;
    private readonly QuotasConfig _cfg;
    private readonly ILogger<QuotaCleanupBackgroundService> _logger;

    public QuotaCleanupBackgroundService(
        IQuotaService quotaService,
        QuotasConfig cfg,
        ILogger<QuotaCleanupBackgroundService> logger)
    {
        _quotaService = quotaService;
        _cfg = cfg;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_cfg.Enabled)
        {
            _logger.LogDebug("[QuotaCleanup] Quotas disabled — background cleanup not started");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, _cfg.CleanupIntervalSeconds));
        _logger.LogInformation(
            "[QuotaCleanup] Background sweep started, interval={IntervalSeconds}s, window={WindowSeconds}s",
            interval.TotalSeconds, _cfg.RateLimitWindowSeconds);

        // Periodic loop. First sweep happens after one interval to avoid stomping on
        // startup work; subsequent sweeps run on the same cadence.
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    if (_quotaService is QuotaService concrete)
                    {
                        var swept = concrete.SweepAllBuckets();
                        if (swept > 0)
                        {
                            _logger.LogDebug("[QuotaCleanup] Swept {Swept} expired entries", swept);
                        }
                    }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // Sweep is best-effort: do not crash the host if cleanup fails.
                    _logger.LogWarning(ex, "[QuotaCleanup] Sweep iteration failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }

        _logger.LogInformation("[QuotaCleanup] Background sweep stopped");
    }
}
