namespace Hercules.Degradation;

/// <summary>
///     Degradation modes representing the operational state of the agent.
/// </summary>
public enum DegradationMode
{
    /// <summary>Full capability - all services available.</summary>
    Full,

    /// <summary>Degraded - some services unavailable, running with reduced capabilities.</summary>
    Degraded,

    /// <summary>Offline - cloud services unavailable, running in local-only mode.</summary>
    Offline
}

/// <summary>
///     Health status of individual services.
/// </summary>
public enum ServiceHealth
{
    Healthy,
    Degraded,
    Unhealthy,
    Unknown
}

/// <summary>
///     Service health snapshot.
/// </summary>
public sealed record ServiceHealthSnapshot(
    string ServiceName,
    ServiceHealth Health,
    string? Message = null,
    DateTimeOffset CheckedAt = default);

/// <summary>
///     Current degradation state snapshot.
/// </summary>
public sealed record DegradationState(
    DegradationMode Mode,
    IReadOnlyList<ServiceHealthSnapshot> ServiceStatuses,
    DateTimeOffset Since,
    string? Reason = null);
