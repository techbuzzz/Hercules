namespace Hercules.Mesh.Backends.Redis;

/// <summary>
///     Redis/Valkey-specific configuration for mesh backends.
///     Spec: task_067.
/// </summary>
public sealed class RedisMeshConfig
{
    /// <summary>
    ///     Enable the Redis backend. Default: false.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    ///     RESP connection string (e.g. "localhost:6379,abortConnect=false").
    ///     Default: "localhost:6379,abortConnect=false,connectTimeout=5000,syncTimeout=5000".
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379,abortConnect=false,connectTimeout=5000,syncTimeout=5000";

    /// <summary>
    ///     Key prefix for all mesh keys (to isolate multiple agents on same Redis).
    ///     Default: "hercules:mesh:".
    /// </summary>
    public string KeyPrefix { get; set; } = "hercules:mesh:";

    /// <summary>
    ///     Pub/sub channel prefix. Default: "hercules:bus:".
    /// </summary>
    public string ChannelPrefix { get; set; } = "hercules:bus:";

    /// <summary>
    ///     Default expiry for state store keys without explicit TTL (seconds). Default: 3600 (1h).
    /// </summary>
    public int DefaultTtlSeconds { get; set; } = 3600;

    /// <summary>
    ///     Default visibility timeout for task queue (seconds). Default: 30.
    /// </summary>
    public int DefaultVisibilityTimeoutSec { get; set; } = 30;

    /// <summary>
    ///     Use keyspace notifications for state store watch (requires Redis CONFIG SET notify-keyspace-events Ex).
    ///     Falls back to polling if not available. Default: true.
    /// </summary>
    public bool UseKeyspaceNotifications { get; set; } = true;

    /// <summary>
    ///     Watch polling interval when keyspace notifications are unavailable (ms). Default: 500.
    /// </summary>
    public int WatchPollingIntervalMs { get; set; } = 500;
}
