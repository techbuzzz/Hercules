using Hercules.Config;
using Hercules.LLM;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Hercules.Health;

/// <summary>
/// task_079: probes the configured primary LLM provider via
/// <see cref="ILLMProviderProbe"/>. Reports <c>Degraded</c> when the
/// primary fails but a fallback provider is configured (chain still active),
/// and <c>Unhealthy</c> when nothing answers.
/// </summary>
public sealed class LlmHealthCheck : IHealthCheck
{
    private readonly LlmConfig _config;
    private readonly ILLMProviderProbe _checker;
    private readonly HealthChecksConfig _healthCfg;
    private readonly ILogger<LlmHealthCheck> _log;

    public LlmHealthCheck(
        LlmConfig config,
        ILLMProviderProbe checker,
        HealthChecksConfig healthCfg,
        ILogger<LlmHealthCheck> log)
    {
        _config = config;
        _checker = checker;
        _healthCfg = healthCfg;
        _log = log;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_config.Provider))
        {
            // LLM intentionally disabled (e.g. local-only deployment). No-op Healthy.
            return HealthCheckResult.Healthy("LLM provider not configured; health check skipped");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _healthCfg.LlmPingTimeoutSec)));

        try
        {
            var primary = await _checker.CheckAsync(_config.Provider, cts.Token).ConfigureAwait(false);

            if (primary.Healthy)
            {
                return HealthCheckResult.Healthy(
                    $"Primary LLM provider '{_config.Provider}' responded in {primary.LatencyMs} ms");
            }

            if (_config.Fallback is { Count: > 0 })
            {
                // Primary failed but fallback chain exists. Probe first fallback.
                var fallbackName = _config.Fallback[0];
                var fallback = await _checker.CheckAsync(fallbackName, cts.Token).ConfigureAwait(false);
                if (fallback.Healthy)
                {
                    return HealthCheckResult.Degraded(
                        $"Primary LLM '{_config.Provider}' unhealthy; fallback '{fallbackName}' is responding");
                }

                return HealthCheckResult.Unhealthy(
                    $"Primary LLM '{_config.Provider}' and fallback '{fallbackName}' are both unhealthy");
            }

            return HealthCheckResult.Unhealthy(
                $"Primary LLM provider '{_config.Provider}' is unhealthy: {primary.Error ?? primary.Status}");
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _log.LogWarning("[HealthCheck] LLM probe timed out for {Provider}", _config.Provider);
            return HealthCheckResult.Degraded(
                $"LLM probe for '{_config.Provider}' timed out after {_healthCfg.LlmPingTimeoutSec}s; degraded");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[HealthCheck] LLM probe failed for {Provider}", _config.Provider);
            return HealthCheckResult.Unhealthy($"LLM probe for '{_config.Provider}' threw", ex);
        }
    }
}
