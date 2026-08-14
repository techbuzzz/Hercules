namespace Hercules.Mesh.Discovery;

/// <summary>
///     A source of peer agent discovery.
///     Implementations: static config, capability registry, mDNS/Bonjour.
///     Discovery itself does NOT grant trust — that is handled by the trust policy layer.
///     Specification: docs/ROADMAP-RU.md Phase 3 task_038.
/// </summary>
public interface IDiscoverySource
{
    /// <summary>Kind of this discovery source.</summary>
    DiscoverySourceKind Source { get; }

    /// <summary>Human-readable name for logs and UI.</summary>
    string Name { get; }

    /// <summary>
    ///     Discover agents available in this source.
    ///     Returns agents (possibly with unresolved endpoints), along with per-agent errors
    ///     for individual agent failures.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Discovery result for this source.</returns>
    Task<DiscoveryResult> DiscoverAsync(CancellationToken ct = default);

    /// <summary>Whether this source is enabled in the current configuration.</summary>
    bool IsEnabled { get; }
}
