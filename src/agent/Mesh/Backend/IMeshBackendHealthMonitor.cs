using Hercules.Mesh.Profiles;

namespace Hercules.Mesh.Backend;

/// <summary>
///     Monitors mesh backend health (bus, queue, state store).
///     Emits events on state transitions and records OTel metrics.
///     Spec: task_070.
/// </summary>
public interface IMeshBackendHealthMonitor
{
    /// <summary>
    ///     Current health status snapshot for all monitored backends.
    /// </summary>
    IReadOnlyList<MeshBackendHealthStatus> GetBackendStatuses();

    /// <summary>
    ///     Subscribe to backend health state changes.
    ///     The callback is invoked on state transitions (Healthy → Degraded → Unavailable).
    /// </summary>
    IDisposable SubscribeToChanges(Func<BackendHealthChangedEvent, Task> callback);

    /// <summary>
    ///     Trigger an immediate health check for all backends.
    /// </summary>
    Task CheckAllAsync(CancellationToken ct = default);
}
