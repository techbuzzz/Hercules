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
    ///     Falls back to <see cref="NetworkFallbackPollUrl"/> if null/empty.
    /// </summary>
    public string? NetworkPollUrl { get; set; }

    /// <summary>
    ///     [task_087] Fallback URL used when <see cref="NetworkPollUrl"/> is empty.
    ///     Default: <c>https://1.1.1.1</c> (Cloudflare) — reachable in the vast
    ///     majority of networks, very small payload, never returns HTML so the
    ///     HEAD check is reliable. Set to empty string to disable polling.
    /// </summary>
    public string NetworkFallbackPollUrl { get; set; } = "https://1.1.1.1";

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

    /// <summary>
    ///     Soft threshold for the Synced tail. When <c>GetSyncedCountAsync()</c>
    ///     exceeds this value, <c>PruneSyncedToCapAsync(PruneSyncedKeep)</c> is
    ///     invoked during <c>EnqueueAsync</c> to bring the tail back to
    ///     <see cref="PruneSyncedKeep"/>. Default 500.
    /// </summary>
    public int PruneSyncedThreshold { get; set; } = 500;

    /// <summary>
    ///     Number of Synced rows to keep after pruning. Older Synced rows are
    ///     deleted (FIFO by <c>synced_at</c>). Default 500.
    /// </summary>
    public int PruneSyncedKeep { get; set; } = 500;

    /// <summary>
    ///     Default deadline (minutes) attached to the <c>IntentEnvelope</c> for
    ///     each outbox item. Was previously hardcoded to 5 minutes inside
    ///     <c>OfflineSyncService.ToEnvelope</c>. Default 5.
    /// </summary>
    public int DefaultDeadlineMinutes { get; set; } = 5;

    /// <summary>
    ///     Upper bound for exponential backoff between retries of the same item
    ///     (milliseconds). Default 5 minutes.
    /// </summary>
    public int MaxBackoffMs { get; set; } = 5 * 60 * 1000;
}
