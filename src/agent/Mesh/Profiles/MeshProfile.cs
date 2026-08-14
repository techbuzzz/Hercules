using System.Text.Json.Serialization;

namespace Hercules.Mesh.Profiles;

/// <summary>
///     Deployment profile kinds for mesh backends.
///     Spec: task_070.
/// </summary>
public enum MeshBackendProfile
{
    /// <summary>
    ///     Single-process, no external dependencies. Uses in-process implementations
    ///     of IMeshBus, ITaskQueue, IMeshStateStore.
    /// </summary>
    Local,

    /// <summary>
    ///     Redis or Valkey for pub/sub bus, task queue, and state store.
    ///     Requires a RESP-compatible server.
    /// </summary>
    Redis,

    /// <summary>
    ///     NATS / JetStream for pub/sub bus and durable task queue.
    ///     State store falls back to Local.
    /// </summary>
    Nats,

    /// <summary>
    ///     PostgreSQL for state store and job queue (FOR UPDATE SKIP LOCKED).
    ///     Bus falls back to Local.
    /// </summary>
    Postgres,

    /// <summary>
    ///     Hybrid: Redis for bus + queue, PostgreSQL for state store.
    /// </summary>
    Hybrid
}

/// <summary>
///     Configuration for a single mesh backend (bus, queue, or state store).
///     Spec: task_070.
/// </summary>
public sealed class MeshBackendConfig
{
    /// <summary>
    ///     Backend kind: "in-process" | "redis" | "nats" | "postgres".
    /// </summary>
    public string Kind { get; set; } = "in-process";

    /// <summary>
    ///     Connection string or host list. Empty = use defaults / in-process.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    ///     Comma-separated host:port pairs (alternative to connectionString).
    /// </summary>
    public List<string> Hosts { get; set; } = new();

    /// <summary>
    ///     Whether this backend is enabled in the current profile.
    ///     If false, falls back to in-process equivalent.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Interval in seconds between health checks for this backend.
    ///     Default: 30.
    /// </summary>
    public int HealthCheckIntervalSec { get; set; } = 30;

    /// <summary>
    ///     Connection/operation timeout in seconds.
    ///     Default: 5.
    /// </summary>
    public int TimeoutSec { get; set; } = 5;

    /// <summary>
    ///     Maximum consecutive retries on connection failure before marking Unavailable.
    ///     Default: 3.
    /// </summary>
    public int MaxRetries { get; set; } = 3;
}

/// <summary>
///     Degradation policy when a backend becomes unavailable.
///     Spec: task_070.
/// </summary>
public sealed class DegradationPolicy
{
    /// <summary>
    ///     Degradation mode when backend fails:
    ///     - FailSilent: continue with remaining backends, log warning
    ///     - DegradeToLocal: switch to in-process implementations
    ///     - RefuseDelegations: stop accepting new inter-agent delegations
    ///     Default: DegradeToLocal.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DegradationMode Mode { get; set; } = DegradationMode.DegradeToLocal;

    /// <summary>
    ///     Optional webhook URL to call when a backend transitions to degraded or unavailable.
    /// </summary>
    public string? AlertWebhook { get; set; }

    /// <summary>
    ///     Maximum time in seconds to remain in degraded mode before forcing full-stop.
    ///     0 = no limit. Default: 0 (no forced stop).
    /// </summary>
    public int MaxDegradedSeconds { get; set; } = 0;
}

/// <summary>
///     Degradation mode for mesh backends.
///     Spec: task_070.
/// </summary>
public enum DegradationMode
{
    /// <summary>
    ///     Continue with remaining healthy backends; log warnings.
    /// </summary>
    FailSilent,

    /// <summary>
    ///     Switch to in-process (local) implementations when external backend fails.
    /// </summary>
    DegradeToLocal,

    /// <summary>
    ///     Stop accepting new inter-agent delegations when mesh backends are degraded.
    /// </summary>
    RefuseDelegations
}

/// <summary>
///     A named mesh deployment profile.
///     Spec: task_070.
/// </summary>
public sealed class MeshProfileDefinition
{
    /// <summary>
    ///     Unique profile name (e.g. "local", "redis-ha", "nats-cluster").
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    ///     Human-readable description.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    ///     MeshBackendProfile enum value (stored as string in JSON).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MeshBackendProfile Profile { get; set; } = MeshBackendProfile.Local;

    /// <summary>
    ///     Backend configurations: bus, queue, state store.
    ///     Each key is a backend role: "bus" | "queue" | "stateStore".
    /// </summary>
    public Dictionary<string, MeshBackendConfig> Backends { get; set; } = new();

    /// <summary>
    ///     Deployment constraints (optional): max agents, region, etc.
    /// </summary>
    public MeshProfileConstraints Constraints { get; set; } = new();

    /// <summary>
    ///     Degradation policy when backends become unavailable.
    /// </summary>
    public DegradationPolicy DegradationPolicy { get; set; } = new();
}

/// <summary>
///     Deployment constraints for a mesh profile.
///     Spec: task_070.
/// </summary>
public sealed class MeshProfileConstraints
{
    /// <summary>
    ///     Maximum number of agents in this deployment.
    ///     0 = no limit.
    /// </summary>
    public int MaxAgents { get; set; } = 0;

    /// <summary>
    ///     Target region or availability zone.
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    ///     Required external services (e.g. ["redis:6379", "postgres:5432"]).
    /// </summary>
    public List<string> RequiredServices { get; set; } = new();
}

/// <summary>
///     Snapshot of mesh backend health.
///     Spec: task_070.
/// </summary>
public sealed record MeshBackendHealthStatus
{
    /// <summary>Backend role: "bus" | "queue" | "stateStore".</summary>
    public required string BackendRole { get; init; }

    /// <summary>Backend kind: "in-process" | "redis" | "nats" | "postgres".</summary>
    public required string BackendKind { get; init; }

    /// <summary>Health state.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BackendHealthState State { get; init; } = BackendHealthState.Unknown;

    /// <summary>Last check timestamp.</summary>
    public DateTimeOffset LastCheckedAt { get; init; }

    /// <summary>Consecutive failures since last healthy check.</summary>
    public int ConsecutiveFailures { get; init; }

    /// <summary>Last error message, if any.</summary>
    public string? LastError { get; init; }
}

/// <summary>
///     Health state of a mesh backend.
///     Spec: task_070.
/// </summary>
public enum BackendHealthState
{
    Unknown,
    Healthy,
    Degraded,
    Unavailable
}

/// <summary>
///     Event emitted when a backend health state transitions.
///     Spec: task_070.
/// </summary>
public sealed record BackendHealthChangedEvent
{
    public required string BackendRole { get; init; }
    public required string BackendKind { get; init; }
    public required BackendHealthState PreviousState { get; init; }
    public required BackendHealthState NewState { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public string? Reason { get; init; }
}
