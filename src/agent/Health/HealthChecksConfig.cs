namespace Hercules.Health;

/// <summary>
/// Configuration for the Web API health checks infrastructure (task_079).
/// Bound from <c>appsettings.json</c> section <c>HealthChecks</c>.
/// </summary>
public sealed class HealthChecksConfig
{
    /// <summary>Enable the /api/health and /api/ready endpoints. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Timeout for the LLM primary provider ping in seconds. Default: 5.</summary>
    public int LlmPingTimeoutSec { get; set; } = 5;

    /// <summary>Free disk space threshold (MB) below which the disk check reports Unhealthy. Default: 100.</summary>
    public int DiskMinFreeMb { get; set; } = 100;

    /// <summary>Disk space threshold (MB) below which the disk check reports Degraded. Default: 1024.</summary>
    public int DiskDegradedMb { get; set; } = 1024;

    /// <summary>Pending outbox items above which the outbox check reports Degraded. Default: 1000.</summary>
    public int OutboxMaxPending { get; set; } = 1000;
}
