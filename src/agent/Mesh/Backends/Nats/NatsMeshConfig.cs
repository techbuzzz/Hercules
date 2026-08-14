namespace Hercules.Mesh.Backends.Nats;

/// <summary>
///     NATS/JetStream-specific configuration for mesh backends.
///     Spec: task_068.
/// </summary>
public sealed class NatsMeshConfig
{
    /// <summary>
    ///     Enable the NATS backend. Default: false.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    ///     Comma-separated list of NATS server URLs.
    ///     Default: "nats://localhost:4222".
    /// </summary>
    public string Servers { get; set; } = "nats://localhost:4222";

    /// <summary>
    ///     Optional connection name (appears in NATS server monitoring).
    ///     Default: "hercules-mesh".
    /// </summary>
    public string Name { get; set; } = "hercules-mesh";

    /// <summary>
    ///     Optional auth token (NATS Token authentication).
    ///     Default: empty (no auth).
    /// </summary>
    public string? AuthToken { get; set; }

    /// <summary>
    ///     Optional user credentials file path (NATS NKey or JWT authentication).
    ///     Default: empty.
    /// </summary>
    public string? CredentialsFile { get; set; }

    /// <summary>
    ///     Subject prefix for all mesh messages (pub/sub).
    ///     Default: "hercules.mesh.".
    /// </summary>
    public string SubjectPrefix { get; set; } = "hercules.mesh.";

    /// <summary>
    ///     Stream prefix for JetStream task queue and KV state store.
    ///     Default: "hercules".
    /// </summary>
    public string StreamPrefix { get; set; } = "hercules";

    /// <summary>
    ///     Default visibility timeout for task queue (seconds). Default: 30.
    /// </summary>
    public int DefaultVisibilityTimeoutSec { get; set; } = 30;

    /// <summary>
    ///     Enable JetStream for durable task queue and KV state store.
    ///     Falls back to core NATS pub/sub if disabled. Default: true.
    /// </summary>
    public bool JetStreamEnabled { get; set; } = true;

    /// <summary>
    ///     Max bytes per JetStream stream. Default: 1GB.
    /// </summary>
    public long JetStreamMaxBytes { get; set; } = 1_073_741_824;

    /// <summary>
    ///     Max age for messages in JetStream streams (days). Default: 7.
    /// </summary>
    public int JetStreamMaxAgeDays { get; set; } = 7;

    /// <summary>
    ///     Connect timeout in milliseconds. Default: 5000.
    /// </summary>
    public int ConnectTimeoutMs { get; set; } = 5000;

    /// <summary>
    ///     Ping interval in milliseconds. Default: 30000.
    /// </summary>
    public int PingIntervalMs { get; set; } = 30000;

    /// <summary>
    ///     Default TTL for state store keys without explicit TTL (seconds). Default: 3600.
    /// </summary>
    public int DefaultTtlSeconds { get; set; } = 3600;
}
