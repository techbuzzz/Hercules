namespace Hercules.Offline;

/// <summary>
///     Configuration for offline resilience (task_060).
///     Controls bounded queue size, TTL per item type, flush interval, and network polling.
/// </summary>
public sealed class OfflineSyncConfig
{
    /// <summary>Whether offline buffering is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Maximum number of items held in the outbox queue (per type).</summary>
    public int MaxQueueSize { get; set; } = 1000;

    /// <summary>
    ///     How often (seconds) the background service attempts to flush pending items.
    ///     Ignored when network is down; flush is always triggered on re-connect.
    /// </summary>
    public int FlushIntervalSeconds { get; set; } = 30;

    /// <summary>
    ///     How often (seconds) the network monitor checks connectivity.
    ///     Set to 0 to disable periodic polling (flush will only happen on re-connect).
    /// </summary>
    public int NetworkPollIntervalSeconds { get; set; } = 15;

    /// <summary>
    ///     URI to poll for network connectivity (HTTP HEAD).
    ///     Falls back to Mesh bus endpoint if null/empty.
    /// </summary>
    public string? NetworkPollUrl { get; set; }

    /// <summary>
    ///     Timeout in seconds for each network poll request.
    /// </summary>
    public int NetworkPollTimeoutSeconds { get; set; } = 5;

    /// <summary>
    ///     Maximum number of retry attempts for a single failed item before it is marked failed.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    ///     Base delay in milliseconds before retrying a failed item (exponential backoff applied).
    /// </summary>
    public int RetryBaseDelayMs { get; set; } = 1000;

    /// <summary>
    ///     TTL in minutes for each item type. Items older than this are dropped on startup.
    ///     0 = no TTL (keep forever).
    /// </summary>
    public int SensorLogTtlMinutes { get; set; } = 1440; // 24 h
    public int TaskResultTtlMinutes { get; set; } = 60;
    public int AlertTtlMinutes { get; set; } = 30;

    /// <summary>
    ///     Whether to drop the oldest synced items first when queue cap is reached,
    ///     or reject new items. Default true = drop oldest synced.
    /// </summary>
    public bool DropOldestSyncedOnCap { get; set; } = true;
}
