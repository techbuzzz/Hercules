namespace Hercules.Slo;

/// <summary>
///     Abstraction over the network-monitor / offline-sync state for the SLO
///     service. Allows the SLO recovery-time objective to be computed from
///     real offline→online transition durations (task_087) without coupling
///     the SLO service directly to <c>NetworkMonitor</c>.
/// </summary>
public interface IConnectivityStateProvider
{
    /// <summary>True if the local node currently has network connectivity.</summary>
    bool IsOnline { get; }

    /// <summary>
    ///     Duration of the most recent offline→online transition. Zero when
    ///     the node has not yet had a recoverable outage since process start.
    ///     Measured wall-clock between the last <c>OnDisconnected</c> and
    ///     <c>OnReconnected</c> events.
    /// </summary>
    TimeSpan LastOutageDuration { get; }
}
