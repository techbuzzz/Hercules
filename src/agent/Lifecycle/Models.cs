namespace Hercules.Lifecycle;

/// <summary>
///     Inventory entry describing a single agent or skill package in the fleet.
/// </summary>
public sealed record AgentInventoryEntry
{
    /// <summary>Unique agent ID.</summary>
    public required string AgentId { get; init; }

    /// <summary>Human-readable display name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Current lifecycle state: Running, Draining, Stopped, Decommissioned.</summary>
    public required string State { get; init; }

    /// <summary>Agent version / build.</summary>
    public string Version { get; init; } = "";

    /// <summary>When the agent started.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>Last health check time.</summary>
    public DateTimeOffset? LastHealthCheck { get; init; }

    /// <summary>Current health status: Healthy, Degraded, Unhealthy.</summary>
    public string HealthStatus { get; init; } = "Unknown";

    /// <summary>Deployed skill package IDs.</summary>
    public IReadOnlyList<string> SkillPackages { get; init; } = [];

    /// <summary>Endpoint URL (null for local agent).</summary>
    public string? Endpoint { get; init; }

    /// <summary>Resource usage snapshot.</summary>
    public AgentResourceUsage Resources { get; init; } = new();
}

/// <summary>
///     Resource usage snapshot for an agent.
/// </summary>
public sealed record AgentResourceUsage
{
    public double CpuPercent { get; init; }
    public long MemoryMb { get; init; }
    public int ActiveRequests { get; init; }
    public int QueueDepth { get; init; }
}

/// <summary>
///     Skill package inventory entry.
/// </summary>
public sealed record SkillPackageInventoryEntry
{
    /// <summary>Unique package ID.</summary>
    public required string PackageId { get; init; }

    /// <summary>Package version.</summary>
    public required string Version { get; init; }

    /// <summary>Deployed to which agents.</summary>
    public IReadOnlyList<string> DeployedToAgents { get; init; } = [];

    /// <summary>Deployment state: Deployed, Updating, Rollback, Failed.</summary>
    public string State { get; init; } = "Deployed";

    /// <summary>When deployed.</summary>
    public DateTimeOffset? DeployedAt { get; init; }

    /// <summary>Whether canary deployment is active.</summary>
    public bool IsCanary { get; init; }
}

/// <summary>
///     Supported lifecycle actions.
/// </summary>
public enum LifecycleAction
{
    Start,
    Stop,
    Drain,
    Update,
    Canary,
    HealthCheck,
    Rollback,
    Decommission
}

/// <summary>
///     Result of a lifecycle action.
/// </summary>
public sealed record LifecycleActionResult
{
    public required string Action { get; init; }
    public required string TargetId { get; init; }
    public required bool Success { get; init; }
    public string? Message { get; init; }
    public string? PreviousState { get; init; }
    public string? NewState { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public Dictionary<string, string> Metadata { get; init; } = new();
}

/// <summary>
///     Health check result for an agent.
/// </summary>
public sealed record HealthCheckResult
{
    public required string AgentId { get; init; }
    public required string Status { get; init; } // Healthy | Degraded | Unhealthy
    public required DateTimeOffset CheckedAt { get; init; }
    public long LatencyMs { get; init; }
    public IReadOnlyList<string> Issues { get; init; } = [];
    public IReadOnlyDictionary<string, string> Details { get; init; } = new Dictionary<string, string>();
}

/// <summary>
///     Canary deployment result.
/// </summary>
public sealed record CanaryResult
{
    public required string PackageId { get; init; }
    public required string TargetAgentId { get; init; }
    public required bool Success { get; init; }
    public required int TrafficPercent { get; init; }
    public string? Error { get; init; }
    public DateTimeOffset DeployedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
///     Rollback result.
/// </summary>
public sealed record RollbackResult
{
    public required string TargetId { get; init; }
    public required bool Success { get; init; }
    public required string FromVersion { get; init; }
    public required string ToVersion { get; init; }
    public string? Error { get; init; }
    public DateTimeOffset RolledBackAt { get; init; } = DateTimeOffset.UtcNow;
}
