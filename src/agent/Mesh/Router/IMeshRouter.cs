namespace Hercules.Mesh.Router;

/// <summary>
///     Interface for the advanced mesh router (Phase 4 task_043).
///     Selects the best trusted peer(s) for a given capability or intent
///     using capability lookup, trust filtering, health tracking, and
///     weighted composite ranking.
/// </summary>
public interface IMeshRouter
{
    /// <summary>
    ///     Route a capability/intent request to the best available peer(s).
    ///     Returns ranked candidates, filtered by trust policy and confidence threshold.
    /// </summary>
    /// <param name="capability">Capability name or user intent.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ranked list of <see cref="PeerCandidate"/>, highest score first.</returns>
    Task<IReadOnlyList<PeerCandidate>> RouteAsync(string capability, CancellationToken ct = default);

    /// <summary>
    ///     Route with an explicit budget constraint.
    ///     Candidates whose cost hint exceeds <paramref name="maxCostUsd"/> are excluded.
    /// </summary>
    Task<IReadOnlyList<PeerCandidate>> RouteAsync(
        string capability,
        decimal maxCostUsd,
        CancellationToken ct = default);
}
