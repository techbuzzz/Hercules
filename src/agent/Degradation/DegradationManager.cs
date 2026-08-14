using System.Collections.Concurrent;
using Hercules.LLM;
using Hercules.Offline;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hercules.Degradation;

/// <summary>
///     Orchestrates local-first degradation behavior.
///     Monitors service health, transitions between degradation modes,
///     and coordinates fallback strategies.
///     Task 061: Local-first degradation.
/// </summary>
public sealed class DegradationManager : BackgroundService
{
    private readonly DegradationConfig _config;
    private readonly DeterministicFallbackEngine _fallbackEngine;
    private readonly OperatorNotificationService _notificationService;
    private readonly ILLMClient? _llmClient;
    private readonly INetworkMonitor? _networkMonitor;
    private readonly ILogger<DegradationManager> _log;

    private readonly ConcurrentDictionary<string, ServiceHealthState> _serviceStates = new();
    private DegradationMode _currentMode = DegradationMode.Full;
    private DateTimeOffset _modeSince = DateTimeOffset.UtcNow;
    private string? _lastReason;

    private readonly object _stateLock = new();

    public DegradationManager(
        DegradationConfig config,
        OperatorNotificationService notificationService,
        ILLMClient? llmClient,
        INetworkMonitor? networkMonitor,
        ILogger<DegradationManager> log)
    {
        _config = config;
        _notificationService = notificationService;
        _llmClient = llmClient;
        _networkMonitor = networkMonitor;
        _log = log;
        _fallbackEngine = new DeterministicFallbackEngine(config, llmClient, log as ILogger<DeterministicFallbackEngine> ?? null!);

        // Initialize service states
        InitializeServiceStates();
    }

    /// <summary>Current degradation mode.</summary>
    public DegradationMode CurrentMode
    {
        get { lock (_stateLock) return _currentMode; }
    }

    /// <summary>Current degradation state snapshot.</summary>
    public DegradationState GetState()
    {
        lock (_stateLock)
        {
            var statuses = _serviceStates
                .Select(kvp => new ServiceHealthSnapshot(
                    kvp.Key,
                    kvp.Value.CurrentHealth,
                    kvp.Value.LastMessage,
                    kvp.Value.LastChecked))
                .ToList();

            return new DegradationState(_currentMode, statuses, _modeSince, _lastReason);
        }
    }

    /// <summary>Current fallback strategy based on degradation state.</summary>
    public FallbackStrategy CurrentStrategy => _fallbackEngine.GetFallbackStrategy(GetState());

    /// <summary>Whether new delegations should be accepted.</summary>
    public bool ShouldAcceptDelegations => _fallbackEngine.ShouldAcceptDelegations(GetState());

    /// <summary>Whether work should be queued instead of processed.</summary>
    public bool ShouldQueueWork => CurrentStrategy == FallbackStrategy.QueueWork;

    /// <summary>Event fired when degradation mode changes.</summary>
    public event EventHandler<DegradationModeChangedEventArgs>? OnModeChanged;

    private void InitializeServiceStates()
    {
        if (_config.HealthCheck.CheckLlmProvider)
        {
            _serviceStates["llm"] = new ServiceHealthState();
        }

        if (_config.HealthCheck.CheckNetwork)
        {
            _serviceStates["network"] = new ServiceHealthState();
        }

        if (_config.HealthCheck.CheckMeshBus)
        {
            _serviceStates["mesh-bus"] = new ServiceHealthState();
        }

        if (_config.HealthCheck.CheckSkillRegistry)
        {
            _serviceStates["skill-registry"] = new ServiceHealthState();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.Enabled || !_config.HealthCheck.Enabled)
        {
            _log.LogInformation("DegradationManager: disabled");
            return;
        }

        _log.LogInformation("DegradationManager: started with {ServiceCount} services monitored",
            _serviceStates.Count);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunHealthCheckCycleAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(_config.HealthCheck.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "DegradationManager: health check cycle failed");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task RunHealthCheckCycleAsync(CancellationToken ct)
    {
        var tasks = new List<Task>();

        if (_config.HealthCheck.CheckLlmProvider)
        {
            tasks.Add(CheckLlmHealthAsync(ct));
        }

        if (_config.HealthCheck.CheckNetwork)
        {
            tasks.Add(CheckNetworkHealthAsync(ct));
        }

        if (_config.HealthCheck.CheckMeshBus)
        {
            tasks.Add(CheckMeshBusHealthAsync(ct));
        }

        if (_config.HealthCheck.CheckSkillRegistry)
        {
            tasks.Add(CheckSkillRegistryHealthAsync(ct));
        }

        await Task.WhenAll(tasks);
        EvaluateDegradationMode();
    }

    private async Task CheckLlmHealthAsync(CancellationToken ct)
    {
        var state = _serviceStates.GetOrAdd("llm", _ => new ServiceHealthState());

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_config.HealthCheck.TimeoutSeconds));

            // Simple health check: try to get model info
            if (_llmClient != null)
            {
                // For now, assume LLM is healthy if client exists
                // In production, add actual health check call
                state.RecordCheck(ServiceHealth.Healthy, "LLM client available");
            }
            else
            {
                state.RecordCheck(ServiceHealth.Degraded, "No LLM client configured");
            }
        }
        catch (Exception ex)
        {
            state.RecordCheck(ServiceHealth.Unhealthy, ex.Message);
        }

        await Task.CompletedTask;
    }

    private async Task CheckNetworkHealthAsync(CancellationToken ct)
    {
        var state = _serviceStates.GetOrAdd("network", _ => new ServiceHealthState());

        try
        {
            if (_networkMonitor != null)
            {
                var isOnline = await _networkMonitor.CheckOnceAsync(ct);
                if (isOnline)
                {
                    state.RecordCheck(ServiceHealth.Healthy, "Network available");
                }
                else
                {
                    state.RecordCheck(ServiceHealth.Unhealthy, "Network unavailable");
                }
            }
            else
            {
                // task_078: removed hardcoded google.com ping. If no NetworkMonitor
                // is registered, mark network as Degraded (cannot determine status
                // without a configured probe endpoint).
                state.RecordCheck(
                    ServiceHealth.Degraded,
                    "NetworkMonitor not registered; cannot probe network health");
            }
        }
        catch (Exception ex)
        {
            state.RecordCheck(ServiceHealth.Unhealthy, ex.Message);
        }
    }

    private async Task CheckMeshBusHealthAsync(CancellationToken ct)
    {
        var state = _serviceStates.GetOrAdd("mesh-bus", _ => new ServiceHealthState());

        // Mesh bus health check - for now, assume healthy
        // In production, add actual mesh bus health check
        state.RecordCheck(ServiceHealth.Healthy, "Mesh bus available");
        await Task.CompletedTask;
    }

    private async Task CheckSkillRegistryHealthAsync(CancellationToken ct)
    {
        var state = _serviceStates.GetOrAdd("skill-registry", _ => new ServiceHealthState());

        // Skill registry health check - assume healthy
        state.RecordCheck(ServiceHealth.Healthy, "Skill registry available");
        await Task.CompletedTask;
    }

    private void EvaluateDegradationMode()
    {
        var previousMode = _currentMode;
        var newMode = DetermineNewMode();
        var reason = BuildDegradationReason();

        if (previousMode != newMode)
        {
            lock (_stateLock)
            {
                _currentMode = newMode;
                _modeSince = DateTimeOffset.UtcNow;
                _lastReason = reason;
            }

            _log.LogWarning(
                "Degradation mode changed: {PreviousMode} -> {NewMode}. Reason: {Reason}",
                previousMode, newMode, reason);

            // Fire event and send notification
            OnModeChanged?.Invoke(this, new DegradationModeChangedEventArgs(previousMode, newMode, reason));
            _ = _notificationService.NotifyModeChangeAsync(previousMode, newMode, reason);
        }
    }

    private DegradationMode DetermineNewMode()
    {
        var states = _serviceStates.Values.ToList();

        // Check if any critical service is unhealthy
        var hasUnhealthy = states.Any(s => s.CurrentHealth == ServiceHealth.Unhealthy);
        var hasDegraded = states.Any(s => s.CurrentHealth == ServiceHealth.Degraded);

        // If network or LLM is unhealthy -> Offline
        var networkState = _serviceStates.GetValueOrDefault("network");
        var llmState = _serviceStates.GetValueOrDefault("llm");

        if ((networkState?.CurrentHealth == ServiceHealth.Unhealthy) ||
            (llmState?.CurrentHealth == ServiceHealth.Unhealthy))
        {
            return DegradationMode.Offline;
        }

        // If any service is degraded -> Degraded
        if (hasDegraded || hasUnhealthy)
        {
            return DegradationMode.Degraded;
        }

        return DegradationMode.Full;
    }

    private string? BuildDegradationReason()
    {
        var reasons = new List<string>();

        foreach (var (name, state) in _serviceStates)
        {
            if (state.CurrentHealth != ServiceHealth.Healthy)
            {
                reasons.Add($"{name}: {state.CurrentHealth}");
            }
        }

        return reasons.Count > 0 ? string.Join(", ", reasons) : null;
    }

    /// <summary>
    ///     Manually transition to a specific degradation mode (for testing or emergency use).
    /// </summary>
    public void SetMode(DegradationMode mode, string? reason = null)
    {
        var previousMode = _currentMode;

        lock (_stateLock)
        {
            _currentMode = mode;
            _modeSince = DateTimeOffset.UtcNow;
            _lastReason = reason ?? "Manual mode change";
        }

        if (previousMode != mode)
        {
            _log.LogWarning(
                "Degradation mode manually changed: {PreviousMode} -> {NewMode}. Reason: {Reason}",
                previousMode, mode, reason);

            OnModeChanged?.Invoke(this, new DegradationModeChangedEventArgs(previousMode, mode, reason));
            _ = _notificationService.NotifyModeChangeAsync(previousMode, mode, reason);
        }
    }

    /// <summary>
    ///     Check if work should be queued and notify if needed.
    /// </summary>
    public async Task NotifyWorkQueuedAsync(int queueSize, CancellationToken ct = default)
    {
        await _notificationService.NotifyWorkQueuedAsync(queueSize, ct);
    }
}

/// <summary>
///     Tracks health state for a single service.
/// </summary>
internal sealed class ServiceHealthState
{
    private int _consecutiveFailures;
    private int _consecutiveSuccesses;

    public ServiceHealth CurrentHealth { get; private set; } = ServiceHealth.Healthy;
    public string? LastMessage { get; private set; }
    public DateTimeOffset LastChecked { get; private set; }

    public void RecordCheck(ServiceHealth health, string? message)
    {
        LastChecked = DateTimeOffset.UtcNow;
        LastMessage = message;

        if (health == ServiceHealth.Healthy || health == ServiceHealth.Degraded)
        {
            _consecutiveSuccesses++;
            _consecutiveFailures = 0;

            if (_consecutiveSuccesses >= 2)
            {
                CurrentHealth = health == ServiceHealth.Healthy
                    ? ServiceHealth.Healthy
                    : ServiceHealth.Degraded;
            }
        }
        else
        {
            _consecutiveFailures++;
            _consecutiveSuccesses = 0;

            if (_consecutiveFailures >= 3)
            {
                CurrentHealth = ServiceHealth.Unhealthy;
            }
        }
    }
}

/// <summary>
///     Event args for degradation mode changes.
/// </summary>
public sealed class DegradationModeChangedEventArgs : EventArgs
{
    public DegradationMode PreviousMode { get; }
    public DegradationMode NewMode { get; }
    public string? Reason { get; }

    public DegradationModeChangedEventArgs(DegradationMode previousMode, DegradationMode newMode, string? reason)
    {
        PreviousMode = previousMode;
        NewMode = newMode;
        Reason = reason;
    }
}
