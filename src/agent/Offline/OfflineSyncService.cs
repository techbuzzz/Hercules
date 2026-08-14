using Hercules.Agent;
using Hercules.Config;
using Hercules.Mesh;
using Hercules.Mesh.Abstractions;
using HerculesBus.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hercules.Offline;

/// <summary>
///     Orchestrates offline buffering and sync for task_060.
///     - Maintains a bounded SQLite outbox queue (sensor logs, task results, alerts).
///     - Drains the queue when the network is available.
///     - Subscribes to <see cref="NetworkMonitor.OnReconnected"/> for immediate flush.
///     - Runs a periodic flush via <see cref="BackgroundService"/>.
/// </summary>
public sealed class OfflineSyncService : BackgroundService
{
    private readonly IOutboxStore _store;
    private readonly IMeshBus _bus;
    private readonly INetworkMonitor _network;
    private readonly OfflineSyncConfig _config;
    private readonly MeshConfig _meshConfig;
    private readonly ILogger<OfflineSyncService> _log;

    public OfflineSyncService(
        IOutboxStore store,
        IMeshBus bus,
        INetworkMonitor network,
        OfflineSyncConfig config,
        MeshConfig meshConfig,
        ILogger<OfflineSyncService> log)
    {
        _store = store;
        _bus = bus;
        _network = network;
        _config = config;
        _meshConfig = meshConfig;
        _log = log;
    }

    /// <summary>Enqueue a sensor log for deferred sync.</summary>
    public Task EnqueueSensorLogAsync(string sessionId, string correlationId, object payload, CancellationToken ct = default)
        => EnqueueAsync(OutboxItem.SensorLog(sessionId, correlationId, payload), ct);

    /// <summary>Enqueue a task result for deferred sync.</summary>
    public Task EnqueueTaskResultAsync(string sessionId, string correlationId, object payload, CancellationToken ct = default)
        => EnqueueAsync(OutboxItem.TaskResult(sessionId, correlationId, payload), ct);

    /// <summary>Enqueue an alert for deferred sync.</summary>
    public Task EnqueueAlertAsync(string sessionId, string correlationId, object payload, CancellationToken ct = default)
        => EnqueueAsync(OutboxItem.Alert(sessionId, correlationId, payload), ct);

    /// <summary>
    ///     Enqueue an arbitrary item. Bounded queue; may drop oldest synced items.
    ///     Deduplication: items with duplicate ItemId are silently ignored.
    /// </summary>
    public async Task EnqueueAsync(OutboxItem item, CancellationToken ct = default)
    {
        if (!_config.Enabled)
        {
            _log.LogDebug("OfflineSync: buffering disabled, skipping enqueue");
            return;
        }

        await _store.EnqueueAsync(item, ct);
        _log.LogDebug(
            "OfflineSync: enqueued {Type} item {ItemId}",
            item.Type, item.ItemId);
    }

    /// <summary>
    ///     Flush all pending items to the mesh bus.
    ///     Items are published to type-specific topics and then marked synced or failed.
    ///     Deduplication: already-synced items are skipped.
    ///     Ordering: items sorted by CreatedAt ASC within each priority tier.
    /// </summary>
    public async Task<FlushResult> FlushAsync(CancellationToken ct = default)
    {
        if (!_config.Enabled)
        {
            _log.LogDebug("OfflineSync: buffering disabled, skipping flush");
            return new FlushResult(0, 0, 0, 0, new List<string>());
        }

        var pending = await _store.GetPendingAsync(ct);
        if (pending.Count == 0)
        {
            _log.LogDebug("OfflineSync: nothing to flush");
            return new FlushResult(0, 0, 0, 0, new List<string>());
        }

        _log.LogInformation("OfflineSync: flushing {Count} pending items", pending.Count);

        int success = 0, failure = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var item in pending)
        {
            ct.ThrowIfCancellationRequested();

            // Check max retries
            if (item.RetryCount >= _config.MaxRetries)
            {
                _log.LogWarning(
                    "OfflineSync: item {ItemId} exceeded max retries ({MaxRetries}), marking failed",
                    item.ItemId, _config.MaxRetries);
                await _store.MarkFailedAsync(item.ItemId, item.LastError ?? "max retries exceeded", ct);
                failure++;
                continue;
            }

            var topic = TopicFor(item.Type);
            var envelope = ToEnvelope(item);

            try
            {
                await _bus.PublishAsync(topic, envelope, ct);
                await _store.MarkSyncedAsync(item.ItemId, ct);
                success++;
                _log.LogDebug("OfflineSync: synced {Type} item {ItemId}", item.Type, item.ItemId);
            }
            catch (Exception ex)
            {
                await _store.IncrementRetryAsync(item.ItemId, ex.Message, ct);
                errors.Add($"{item.ItemId}: {ex.Message}");
                failure++;
                _log.LogWarning(ex, "OfflineSync: failed to sync item {ItemId}, retry {RetryCount}",
                    item.ItemId, item.RetryCount + 1);
            }
        }

        _log.LogInformation(
            "OfflineSync: flush complete — success={Success}, failed={Failure}, skipped={Skipped}",
            success, failure, skipped);

        return new FlushResult(success + failure + skipped, success, failure, skipped, errors);
    }

    /// <summary>Number of items currently pending sync.</summary>
    public Task<int> GetPendingCountAsync(CancellationToken ct = default)
        => _store.GetPendingCountAsync(ct);

    /// <summary>Whether the network is currently online.</summary>
    public bool IsOnline => _network.IsOnline;

    // ---- BackgroundService ----

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.Enabled) return;

        // Clean up expired items on startup
        await CleanupExpiredAsync(stoppingToken);

        // Subscribe to network events
        _network.OnReconnected += async (_, _) =>
        {
            _log.LogInformation("OfflineSync: network reconnected, triggering flush");
            try { await FlushAsync(stoppingToken); }
            catch (Exception ex) { _log.LogError(ex, "OfflineSync: flush on reconnect failed"); }
        };

        // Start network monitor polling
        _ = RunNetworkMonitorAsync(stoppingToken);

        // Periodic flush loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_config.FlushIntervalSeconds), stoppingToken);
                if (_network.IsOnline)
                {
                    await FlushAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "OfflineSync: periodic flush failed");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task RunNetworkMonitorAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_config.NetworkPollIntervalSeconds), stoppingToken);
                if (_config.NetworkPollIntervalSeconds > 0)
                {
                    await _network.CheckOnceAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "OfflineSync: network poll failed");
            }
        }
    }

    private async Task CleanupExpiredAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var cutoffs = new Dictionary<OutboxItemType, DateTime>
        {
            [OutboxItemType.SensorLog] = now.AddMinutes(-_config.SensorLogTtlMinutes),
            [OutboxItemType.TaskResult] = now.AddMinutes(-_config.TaskResultTtlMinutes),
            [OutboxItemType.Alert] = now.AddMinutes(-_config.AlertTtlMinutes),
        };

        int total = 0;
        foreach (var (type, cutoff) in cutoffs)
        {
            if (cutoff < now) // TTL > 0
            {
                var deleted = await _store.DeleteOlderThanAsync(cutoff, ct);
                if (deleted > 0)
                    _log.LogInformation("OfflineSync: pruned {Count} expired {Type} items", deleted, type);
                total += deleted;
            }
        }
    }

    private static string TopicFor(OutboxItemType type) => type switch
    {
        OutboxItemType.SensorLog => "offline/sensor-log",
        OutboxItemType.TaskResult => "offline/task-result",
        OutboxItemType.Alert => "offline/alert",
        _ => "offline/unknown"
    };

    private IntentEnvelope ToEnvelope(OutboxItem item)
    {
        return new IntentEnvelope
        {
            RequestId = item.ItemId,
            IdempotencyKey = item.ItemId,
            Sender = _meshConfig.AgentId,
            Intent = "offline.sync",
            Payload = item.Payload,
            Deadline = DateTimeOffset.UtcNow.AddMinutes(5)
        };
    }
}
