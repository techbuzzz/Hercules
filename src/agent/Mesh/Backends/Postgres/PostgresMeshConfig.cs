namespace Hercules.Mesh.Backends.Postgres;

/// <summary>
///     PostgreSQL-specific configuration for mesh backends.
///     Implements cross-agent workflow state, durable task queue (with SELECT ... FOR UPDATE SKIP LOCKED),
///     and LISTEN/NOTIFY-based pub/sub bus.
///     Spec: task_069.
/// </summary>
public sealed class PostgresMeshConfig
{
    /// <summary>
    ///     Enable the PostgreSQL backend. Default: false.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    ///     Npgsql connection string. Default targets local Postgres on port 5432.
    ///     Example: "Host=localhost;Port=5432;Username=hercules;Password=...;Database=hercules_mesh;Pooling=true;Maximum Pool Size=20"
    /// </summary>
    public string ConnectionString { get; set; } = "Host=localhost;Port=5432;Username=hercules;Password=hercules;Database=hercules_mesh;Pooling=true;Maximum Pool Size=20;Timeout=5;Command Timeout=10";

    /// <summary>
    ///     Database schema for all mesh tables. Default: "hercules_mesh".
    /// </summary>
    public string Schema { get; set; } = "hercules_mesh";

    /// <summary>
    ///     Table name for the state store. Default: "state".
    /// </summary>
    public string StateTable { get; set; } = "state";

    /// <summary>
    ///     Table name for the task queue. Default: "tasks".
    /// </summary>
    public string TasksTable { get; set; } = "tasks";

    /// <summary>
    ///     Table name for the dead-letter queue. Default: "tasks_dlq".
    /// </summary>
    public string DlqTable { get; set; } = "tasks_dlq";

    /// <summary>
    ///     Channel prefix for LISTEN/NOTIFY pub/sub bus. Default: "hercules_bus_".
    /// </summary>
    public string ChannelPrefix { get; set; } = "hercules_bus_";

    /// <summary>
    ///     Default expiry for state store keys without explicit TTL (seconds). Default: 3600 (1h).
    /// </summary>
    public int DefaultTtlSeconds { get; set; } = 3600;

    /// <summary>
    ///     Default visibility timeout for task queue (seconds). Default: 30.
    /// </summary>
    public int DefaultVisibilityTimeoutSec { get; set; } = 30;

    /// <summary>
    ///     Max delivery attempts before a task moves to DLQ. Default: 3.
    /// </summary>
    public int MaxDeliveryAttempts { get; set; } = 3;

    /// <summary>
    ///     Use LISTEN/NOTIFY for state watch notifications.
    ///     Falls back to polling if disabled or unavailable. Default: true.
    /// </summary>
    public bool UseListenNotify { get; set; } = true;

    /// <summary>
    ///     Watch polling interval when LISTEN/NOTIFY is unavailable (ms). Default: 500.
    /// </summary>
    public int WatchPollingIntervalMs { get; set; } = 500;

    /// <summary>
    ///     Connect timeout in seconds. Default: 5.
    /// </summary>
    public int ConnectTimeoutSeconds { get; set; } = 5;

    /// <summary>
    ///     Create schema and tables automatically on first use. Default: true.
    /// </summary>
    public bool AutoCreateSchema { get; set; } = true;

    /// <summary>
    ///     Background requeue interval for timed-out in-flight tasks (ms). Default: 1000.
    /// </summary>
    public int RequeueTimerIntervalMs { get; set; } = 1000;
}
