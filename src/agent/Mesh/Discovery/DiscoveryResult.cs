namespace Hercules.Mesh.Discovery;

/// <summary>
///     Discovery source identifier.
/// </summary>
public enum DiscoverySourceKind
{
    /// <summary>Agents configured statically in MeshConfig.Peers.</summary>
    Static = 0,

    /// <summary>Agents found via local capability registry.</summary>
    Registry = 1,

    /// <summary>Agents discovered via mDNS/Bonjour broadcast.</summary>
    Mdns = 2,
}

/// <summary>
///     Information about a single discovered agent, sourced from a specific discovery mechanism.
/// </summary>
public sealed record DiscoveredAgent
{
    /// <summary>Agent ID (unique per mesh).</summary>
    public required string AgentId { get; init; }

    /// <summary>Human-readable display name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Endpoint for inter-agent calls (may be empty if unresolved).</summary>
    public string Endpoint { get; init; } = "";

    /// <summary>Base URL for fetching the agent manifest.</summary>
    public string ManifestUrl { get; init; } = "";

    /// <summary>Source mechanism that found this agent.</summary>
    public DiscoverySourceKind Source { get; init; }

    /// <summary>When this agent was discovered (UTC).</summary>
    public DateTimeOffset DiscoveredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>When this agent entry expires (TTL-based).</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Capabilities reported by the agent (populated after manifest fetch).</summary>
    public List<string> Capabilities { get; init; } = new();

    /// <summary>Error that occurred during discovery of this agent (null = success).</summary>
    public string? Error { get; init; }

    /// <summary>Whether the agent's manifest was successfully fetched.</summary>
    public bool ManifestLoaded { get; init; }
}

/// <summary>
///     Result of a single discovery operation (one source).
/// </summary>
public sealed class DiscoveryResult
{
    /// <summary>Agents discovered from this source.</summary>
    public List<DiscoveredAgent> Agents { get; init; } = new();

    /// <summary>Source that was queried.</summary>
    public DiscoverySourceKind Source { get; init; }

    /// <summary>Human-readable source name (e.g. "Static peers", "Capability registry", "mDNS").</summary>
    public required string SourceName { get; init; }

    /// <summary>Whether the operation succeeded (errors may still be present per-agent).</summary>
    public bool Success { get; init; }

    /// <summary>Error message if the source itself failed (null = OK).</summary>
    public string? Error { get; init; }

    /// <summary>When this result was obtained.</summary>
    public DateTimeOffset QueriedAt { get; init; } = DateTimeOffset.UtcNow;
}
