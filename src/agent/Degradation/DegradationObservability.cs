using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Hercules.Degradation;

/// <summary>
///     Observability for degradation state.
///     Provides metrics, logging, and status endpoint support.
///     Task 061: Local-first degradation.
/// </summary>
public sealed class DegradationObservability : IDisposable
{
    private readonly DegradationConfig _config;
    private readonly ILogger<DegradationObservability> _log;
    private readonly Meter? _meter;
    private readonly Histogram<double>? _modeDuration;
    private readonly Counter<long>? _modeTransitions;
    private readonly Counter<long>? _fallbackStrategies;
    private bool _disposed;

    public DegradationObservability(DegradationConfig config, ILogger<DegradationObservability> log)
    {
        _config = config;
        _log = log;

        if (_config.Observability.EnableMetrics)
        {
            _meter = new Meter("Hercules.Degradation", "1.0.0");

            _modeDuration = _meter.CreateHistogram<double>(
                "hercules.degradation.mode_duration_seconds",
                unit: "s",
                description: "Duration in current degradation mode");

            _modeTransitions = _meter.CreateCounter<long>(
                "hercules.degradation.mode_transitions_total",
                description: "Total number of degradation mode transitions");

            _fallbackStrategies = _meter.CreateCounter<long>(
                "hercules.degradation.fallback_strategies_total",
                description: "Total fallback strategy selections");
        }
    }

    /// <summary>
    ///     Record a degradation mode transition.
    /// </summary>
    public void RecordModeTransition(DegradationMode previousMode, DegradationMode newMode, string? reason)
    {
        if (!_config.Observability.EnableMetrics || _modeTransitions == null)
        {
            return;
        }

        _modeTransitions.Add(1,
            new KeyValuePair<string, object?>("previous_mode", previousMode.ToString()),
            new KeyValuePair<string, object?>("new_mode", newMode.ToString()));

        if (_config.Observability.EnableLogging)
        {
            var logLevel = newMode switch
            {
                DegradationMode.Full => LogLevel.Information,
                DegradationMode.Degraded => LogLevel.Warning,
                DegradationMode.Offline => LogLevel.Error,
                _ => LogLevel.Information
            };

            _log.Log(logLevel,
                "Degradation mode transition: {PreviousMode} -> {NewMode}. Reason: {Reason}",
                previousMode, newMode, reason ?? "unknown");
        }
    }

    /// <summary>
    ///     Record current mode duration.
    /// </summary>
    public void RecordModeDuration(DegradationState state)
    {
        if (!_config.Observability.EnableMetrics || _modeDuration == null)
        {
            return;
        }

        var durationSeconds = (DateTimeOffset.UtcNow - state.Since).TotalSeconds;
        _modeDuration.Record(durationSeconds,
            new KeyValuePair<string, object?>("mode", state.Mode.ToString()));
    }

    /// <summary>
    ///     Record a fallback strategy selection.
    /// </summary>
    public void RecordFallbackStrategy(FallbackStrategy strategy, DegradationMode mode)
    {
        if (!_config.Observability.EnableMetrics || _fallbackStrategies == null)
        {
            return;
        }

        _fallbackStrategies.Add(1,
            new KeyValuePair<string, object?>("strategy", strategy.ToString()),
            new KeyValuePair<string, object?>("mode", mode.ToString()));

        if (_config.Observability.EnableLogging)
        {
            _log.LogDebug(
                "Fallback strategy selected: {Strategy} (mode: {Mode})",
                strategy, mode);
        }
    }

    /// <summary>
    ///     Generate a status report for the current degradation state.
    /// </summary>
    public DegradationStatusReport GenerateStatusReport(DegradationState state)
    {
        return new DegradationStatusReport
        {
            Mode = state.Mode,
            SinceUtc = state.Since,
            Reason = state.Reason,
            DurationSeconds = (DateTimeOffset.UtcNow - state.Since).TotalSeconds,
            ServiceStatuses = state.ServiceStatuses
                .Select(s => new ServiceStatusDto
                {
                    Name = s.ServiceName,
                    Health = s.Health.ToString(),
                    Message = s.Message,
                    CheckedAtUtc = s.CheckedAt
                })
                .ToList(),
            Recommendations = GenerateRecommendations(state)
        };
    }

    private IReadOnlyList<string> GenerateRecommendations(DegradationState state)
    {
        var recommendations = new List<string>();

        if (state.Mode == DegradationMode.Offline)
        {
            recommendations.Add("Agent is in offline mode. Work is being queued for later processing.");
            recommendations.Add("Check network connectivity and LLM provider availability.");
        }
        else if (state.Mode == DegradationMode.Degraded)
        {
            recommendations.Add("Agent is in degraded mode with reduced capabilities.");
            recommendations.Add("Consider switching to reduced-capability LLM models.");
            recommendations.Add("Use local skills instead of cloud-hosted skills.");
        }

        var unhealthyServices = state.ServiceStatuses
            .Where(s => s.Health == ServiceHealth.Unhealthy)
            .Select(s => s.ServiceName)
            .ToList();

        if (unhealthyServices.Count > 0)
        {
            recommendations.Add($"Services requiring attention: {string.Join(", ", unhealthyServices)}");
        }

        return recommendations;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _meter?.Dispose();
    }
}

/// <summary>
///     Degradation status report for API endpoints and monitoring.
/// </summary>
public sealed class DegradationStatusReport
{
    public DegradationMode Mode { get; set; }
    public DateTimeOffset SinceUtc { get; set; }
    public string? Reason { get; set; }
    public double DurationSeconds { get; set; }
    public List<ServiceStatusDto> ServiceStatuses { get; set; } = new();
    public IReadOnlyList<string> Recommendations { get; set; } = Array.Empty<string>();
}

/// <summary>
///     Service status DTO for API responses.
/// </summary>
public sealed class ServiceStatusDto
{
    public string Name { get; set; } = string.Empty;
    public string Health { get; set; } = string.Empty;
    public string? Message { get; set; }
    public DateTimeOffset CheckedAtUtc { get; set; }
}
