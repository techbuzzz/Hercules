namespace Hercules.Offline;

/// <summary>
///     Abstraction for network connectivity monitoring.
///     Allows unit testing without mocking a concrete class.
/// </summary>
public interface INetworkMonitor
{
    /// <summary>Current connectivity state.</summary>
    bool IsOnline { get; }

    /// <summary>
    ///     Check connectivity once. Returns true if reachable.
    ///     Raises OnReconnected / OnDisconnected on state change.
    /// </summary>
    Task<bool> CheckOnceAsync(CancellationToken ct = default);

    /// <summary>Fired when network becomes available after being offline.</summary>
    event EventHandler? OnReconnected;

    /// <summary>Fired when network goes offline.</summary>
    event EventHandler? OnDisconnected;
}
