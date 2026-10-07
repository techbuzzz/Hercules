namespace Hercules.Agent;

/// <summary>
///     Tracks the number of in-flight agent handle requests so the lifecycle service can wait
///     for them to finish before declaring the agent stopped. Per-session scoping is reserved
///     for future multi-tenant deployments; today every Begin() bumps a single global counter
///     and WaitForEmptyAsync() blocks until that counter reaches zero.
///     Specification: task_080.
/// </summary>
public interface IInFlightTracker
{
    /// <summary>Number of currently running requests.</summary>
    int InFlightCount { get; }

    /// <summary>
    ///     Start a new in-flight scope. The returned <see cref="IDisposable" />, when disposed,
    ///     releases the slot. Designed for <c>using var scope = tracker.Begin();</c>.
    /// </summary>
    IDisposable Begin();

    /// <summary>
    ///     Wait until the in-flight counter reaches zero or <paramref name="timeout" /> elapses.
    ///     Returns <c>true</c> if the counter reached zero in time, <c>false</c> on timeout.
    ///     Safe to call when the counter is already zero (returns <c>true</c> immediately).
    /// </summary>
    Task<bool> WaitForEmptyAsync(TimeSpan timeout, CancellationToken ct = default);
}
