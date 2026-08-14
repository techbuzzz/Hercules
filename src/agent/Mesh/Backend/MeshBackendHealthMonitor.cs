using System.Collections.Concurrent;
using Hercules.Config;
using Hercules.Mesh.Abstractions;
using Hercules.Mesh.Observability;
using Hercules.Mesh.Profiles;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MeshProfileRecords = Hercules.Mesh.Profiles;

namespace Hercules.Mesh.Backend;

/// <summary>
///     Background health monitoring for mesh backends.
///     Periodically calls IsHealthyAsync on IMeshBus, ITaskQueue, IMeshStateStore,
///     tracks consecutive failures, transitions states, and emits OTel metrics.
///     Spec: task_070.
/// </summary>
public sealed class MeshBackendHealthMonitor : BackgroundService, IMeshBackendHealthMonitor
{
    private readonly IMeshBus _bus;
    private readonly ITaskQueue _queue;
    private readonly IMeshStateStore _stateStore;
    private readonly MeshProfilesConfig _config;
    private readonly IMeshObservabilityService? _observability;
    private readonly ILogger<MeshBackendHealthMonitor> _log;
    private readonly ConcurrentDictionary<string, BackendHealthSnapshot> _statuses = new();
    private readonly ConcurrentDictionary<string, List<Func<BackendHealthChangedEvent, Task>>> _subscribers = new();

    public MeshBackendHealthMonitor(
        IMeshBus bus,
        ITaskQueue queue,
        IMeshStateStore stateStore,
        MeshProfilesConfig config,
        IMeshObservabilityService? observability,
        ILogger<MeshBackendHealthMonitor> log)
    {
        _bus = bus;
        _queue = queue;
        _stateStore = stateStore;
        _config = config;
        _observability = observability;
        _log = log;

        InitializeStatuses();
    }

    /// <inheritdoc />
    public IReadOnlyList<MeshBackendHealthStatus> GetBackendStatuses()
    {
        return _statuses.Values
            .Select(s => s.ToStatus())
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc />
    public IDisposable SubscribeToChanges(Func<BackendHealthChangedEvent, Task> callback)
    {
        var key = Guid.NewGuid().ToString();
        var list = _subscribers.GetOrAdd(key, static _ => new List<Func<BackendHealthChangedEvent, Task>>());
        lock (list)
        {
            list.Add(callback);
        }

        return new Subscription(() =>
        {
            lock (list)
            {
                list.Remove(callback);
            }
            _subscribers.TryRemove(key, out _);
        });
    }

    /// <inheritdoc />
    public async Task CheckAllAsync(CancellationToken ct = default)
    {
        await CheckBackendAsync("bus", async () => await _bus.IsHealthyAsync(ct), _bus.BackendKind, ct);
        await CheckBackendAsync("queue", async () => await _queue.IsHealthyAsync(ct), _queue.BackendKind, ct);
        await CheckBackendAsync("stateStore", async () => await _stateStore.IsHealthyAsync(ct), _stateStore.BackendKind, ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.Enabled)
        {
            _log.LogDebug("[MeshBackendHealth] Health monitoring disabled");
            return;
        }

        var interval = TimeSpan.FromSeconds(_config.HealthCheckIntervalSec);
        _log.LogInformation("[MeshBackendHealth] Starting health monitor, interval={Interval}s", interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[MeshBackendHealth] Health check failed");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _log.LogInformation("[MeshBackendHealth] Health monitor stopped");
    }

    private void InitializeStatuses()
    {
        // In-process backends are always healthy; external backends start as Unknown
        // and get re-checked on first CheckAllAsync() call
        _statuses["bus"] = new BackendHealthSnapshot
        {
            BackendRole = "bus",
            BackendKind = _bus.BackendKind,
            State = _bus.BackendKind == "in-process" ? BackendHealthState.Healthy : BackendHealthState.Unknown,
            LastCheckedAt = DateTimeOffset.UtcNow
        };

        _statuses["queue"] = new BackendHealthSnapshot
        {
            BackendRole = "queue",
            BackendKind = _queue.BackendKind,
            State = _queue.BackendKind == "in-process" ? BackendHealthState.Healthy : BackendHealthState.Unknown,
            LastCheckedAt = DateTimeOffset.UtcNow
        };

        _statuses["stateStore"] = new BackendHealthSnapshot
        {
            BackendRole = "stateStore",
            BackendKind = _stateStore.BackendKind,
            State = _stateStore.BackendKind == "in-process" ? BackendHealthState.Healthy : BackendHealthState.Unknown,
            LastCheckedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task CheckBackendAsync(
        string role,
        Func<ValueTask<bool>> healthCheck,
        string backendKind,
        CancellationToken ct)
    {
        // Skip in-process backends — always healthy
        if (backendKind == "in-process")
        {
            var snapshot = _statuses.GetOrAdd(role, static r => new BackendHealthSnapshot { BackendRole = r });
            UpdateSnapshot(snapshot, BackendHealthState.Healthy, null, DateTimeOffset.UtcNow);
            return;
        }

        var current = _statuses.GetOrAdd(role, static r => new BackendHealthSnapshot { BackendRole = r });

        try
        {
            var timeout = TimeSpan.FromSeconds(_config.HealthCheckTimeoutSec);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            var healthTask = healthCheck().AsTask();
            var winner = await Task.WhenAny(healthTask, Task.Delay(Timeout.Infinite, cts.Token));
            if (winner != healthTask || cts.Token.IsCancellationRequested)
            {
                // Timeout or overall cancellation
                var error = cts.Token.IsCancellationRequested ? "Cancelled" : "Health check timed out";
                var newState = current.ConsecutiveFailures + 1 >= _config.MaxConsecutiveFailures
                    ? BackendHealthState.Unavailable
                    : BackendHealthState.Degraded;
                UpdateSnapshot(current, newState, error, DateTimeOffset.UtcNow);
                return;
            }

            var isHealthy = await healthTask;

            if (isHealthy)
            {
                UpdateSnapshot(current, BackendHealthState.Healthy, null, DateTimeOffset.UtcNow);
            }
            else
            {
                // Partial failure — transition to Degraded
                var error = "Backend reported unhealthy";
                UpdateSnapshot(current, BackendHealthState.Degraded, error, DateTimeOffset.UtcNow);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Only caught here if the healthCheck itself threw OCE (not the timeout CTS)
            var error = "Health check cancelled";
            var newState = current.ConsecutiveFailures + 1 >= _config.MaxConsecutiveFailures
                ? BackendHealthState.Unavailable
                : BackendHealthState.Degraded;
            UpdateSnapshot(current, newState, error, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            var error = ex.Message;
            var newState = current.ConsecutiveFailures + 1 >= _config.MaxConsecutiveFailures
                ? BackendHealthState.Unavailable
                : BackendHealthState.Degraded;
            UpdateSnapshot(current, newState, error, DateTimeOffset.UtcNow);
        }
    }

    private void UpdateSnapshot(BackendHealthSnapshot snapshot, BackendHealthState newState, string? error, DateTimeOffset now)
    {
        var previousState = snapshot.State;
        var previousFailures = snapshot.ConsecutiveFailures;

        snapshot.State = newState;
        snapshot.LastCheckedAt = now;
        snapshot.LastError = error;

        if (newState == BackendHealthState.Healthy)
        {
            snapshot.ConsecutiveFailures = 0;
        }
        else
        {
            snapshot.ConsecutiveFailures = previousState != newState ? 1 : previousFailures + 1;
        }

        // Emit metric
        RecordMetric(snapshot.BackendRole, newState);

        // Fire transition events only when state actually changes
        if (previousState != newState)
        {
            var evt = new BackendHealthChangedEvent
            {
                BackendRole = snapshot.BackendRole,
                BackendKind = snapshot.BackendKind,
                PreviousState = previousState,
                NewState = newState,
                OccurredAt = now,
                Reason = error ?? GetTransitionReason(previousState, newState)
            };

            _log.LogWarning(
                "[MeshBackendHealth] Backend '{Role}' ({Kind}): {Prev} → {New}. Reason: {Reason}",
                snapshot.BackendRole, snapshot.BackendKind,
                previousState, newState, error ?? "n/a");

            FireEvent(evt);
        }
    }

    private static string GetTransitionReason(BackendHealthState from, BackendHealthState to)
    {
        return (from, to) switch
        {
            (BackendHealthState.Unknown, BackendHealthState.Healthy) => "Initial health check succeeded",
            (BackendHealthState.Unknown, BackendHealthState.Degraded) => "Initial health check degraded",
            (BackendHealthState.Unknown, BackendHealthState.Unavailable) => "Initial health check failed",
            (BackendHealthState.Healthy, BackendHealthState.Degraded) => "Health check returned degraded",
            (BackendHealthState.Healthy, BackendHealthState.Unavailable) => "Health check failed",
            (BackendHealthState.Degraded, BackendHealthState.Healthy) => "Health check recovered",
            (BackendHealthState.Degraded, BackendHealthState.Unavailable) => "Consecutive failures threshold reached",
            (BackendHealthState.Unavailable, BackendHealthState.Healthy) => "Backend recovered",
            (BackendHealthState.Unavailable, BackendHealthState.Degraded) => "Backend partially recovered",
            _ => "State transition"
        };
    }

    private void RecordMetric(string role, BackendHealthState state)
    {
        if (_observability == null || !_observability.IsEnabled)
            return;

        var value = state switch
        {
            BackendHealthState.Healthy => 1.0,
            BackendHealthState.Degraded => 0.5,
            BackendHealthState.Unavailable => 0.0,
            _ => -1.0
        };

        _observability.RecordMeshMetric("mesh_backend_health", value, intent: null, outcome: $"{role}:{state.ToString().ToLowerInvariant()}");
    }

    private void FireEvent(BackendHealthChangedEvent evt)
    {
        foreach (var kvp in _subscribers)
        {
            List<Func<BackendHealthChangedEvent, Task>> snapshot;
            lock (kvp.Value)
            {
                snapshot = new List<Func<BackendHealthChangedEvent, Task>>(kvp.Value);
            }

            foreach (var callback in snapshot)
            {
                _ = Task.Run(async () =>
                {
                    try { await callback(evt); }
                    catch (Exception ex) { _log.LogWarning(ex, "[MeshBackendHealth] Subscriber callback threw"); }
                });
            }
        }
    }

    private sealed class BackendHealthSnapshot
    {
        public string BackendRole { get; set; } = "";
        public string BackendKind { get; set; } = "";
        public BackendHealthState State { get; set; } = BackendHealthState.Unknown;
        public DateTimeOffset LastCheckedAt { get; set; }
        public int ConsecutiveFailures { get; set; }
        public string? LastError { get; set; }

        public MeshBackendHealthStatus ToStatus() => new()
        {
            BackendRole = BackendRole,
            BackendKind = BackendKind,
            State = State,
            LastCheckedAt = LastCheckedAt,
            ConsecutiveFailures = ConsecutiveFailures,
            LastError = LastError
        };
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action _unsubscribe;
        private bool _disposed;

        public Subscription(Action unsubscribe) => _unsubscribe = unsubscribe;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, true) == false)
                _unsubscribe();
        }
    }
}
