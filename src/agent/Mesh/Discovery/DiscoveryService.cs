using Hercules.Config;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Discovery;

/// <summary>
///     Orchestrates all <see cref="IDiscoverySource"/> implementations and provides a
///     unified, deduplicated, TTL-cached view of discovered agents.
///     Discovery itself does NOT grant trust — trust decisions belong to the trust policy layer.
///     Specification: docs/ROADMAP-RU.md Phase 3 task_038.
/// </summary>
public sealed class DiscoveryService : IDiscoveryService
{
    private readonly IDiscoverySource[] _sources;
    private readonly DiscoveryConfig _config;
    private readonly ILogger<DiscoveryService> _logger;

    private readonly object _lock = new();
    private Dictionary<string, DiscoveredAgent> _cache = new();
    private DateTimeOffset _cacheTimestamp = DateTimeOffset.MinValue;

    public DiscoveryService(
        IEnumerable<IDiscoverySource> sources,
        DiscoveryConfig config,
        ILogger<DiscoveryService> logger)
    {
        _sources = (sources ?? throw new ArgumentNullException(nameof(sources))).ToArray();
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>List of all registered discovery sources.</summary>
    public IReadOnlyList<IDiscoverySource> Sources => _sources;

    /// <summary>Whether the cache is still fresh (within TTL).</summary>
    public bool IsCacheFresh
    {
        get
        {
            lock (_lock)
            {
                return _cache.Count > 0 &&
                    DateTimeOffset.UtcNow - _cacheTimestamp < TimeSpan.FromSeconds(_config.CacheTtlSeconds);
            }
        }
    }

    /// <summary>
    ///     Get all discovered agents, using cache if fresh or re-querying all sources if stale.
    ///     Deduplicates agents by AgentId, preferring entries with fewer errors and newer discovery.
    /// </summary>
    public async Task<IReadOnlyList<DiscoveredAgent>> GetAgentsAsync(CancellationToken ct = default)
    {
        if (IsCacheFresh)
        {
            lock (_lock)
            {
                return _cache.Values.ToList();
            }
        }

        return await RefreshAsync(ct);
    }

    /// <summary>
    ///     Force-refresh: query all enabled sources and update the cache.
    ///     Returns the refreshed list of agents.
    /// </summary>
    public async Task<IReadOnlyList<DiscoveredAgent>> RefreshAsync(CancellationToken ct = default)
    {
        var allAgents = new Dictionary<string, DiscoveredAgent>(StringComparer.OrdinalIgnoreCase);

        foreach (IDiscoverySource source in _sources.Where(s => s.IsEnabled))
        {
            try
            {
                _logger.LogDebug("Querying discovery source: {Source}", source.Name);
                DiscoveryResult result = await source.DiscoverAsync(ct);

                foreach (DiscoveredAgent agent in result.Agents)
                {
                    if (!allAgents.TryGetValue(agent.AgentId, out var existing) ||
                        ShouldReplace(existing, agent))
                    {
                        allAgents[agent.AgentId] = agent;
                    }
                }

                if (!result.Success)
                {
                    _logger.LogWarning("Discovery source {Source} returned error: {Error}",
                        source.Name, result.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Discovery source {Source} threw", source.Name);
            }
        }

        lock (_lock)
        {
            _cache = allAgents;
            _cacheTimestamp = DateTimeOffset.UtcNow;
        }

        _logger.LogInformation(
            "Discovery refresh complete. Total agents: {Count} from {Sources} source(s)",
            allAgents.Count,
            _sources.Count(s => s.IsEnabled));

        return allAgents.Values.ToList();
    }

    /// <summary>
    ///     Get agents discovered from a specific source kind.
    /// </summary>
    public async Task<IReadOnlyList<DiscoveredAgent>> GetAgentsBySourceAsync(
        DiscoverySourceKind sourceKind,
        CancellationToken ct = default)
    {
        IDiscoverySource? source = _sources.FirstOrDefault(s => s.Source == sourceKind);
        if (source is null || !source.IsEnabled)
        {
            return Array.Empty<DiscoveredAgent>();
        }

        DiscoveryResult result = await source.DiscoverAsync(ct);
        return result.Agents;
    }

    /// <summary>
    ///     Get the status of all discovery sources.
    /// </summary>
    public IReadOnlyList<SourceStatus> GetSourceStatuses()
    {
        return _sources.Select(s => new SourceStatus
        {
            Source = s.Source,
            Name = s.Name,
            Enabled = s.IsEnabled
        }).ToList();
    }

    /// <summary>
    ///     Replace rule: prefer the agent with fewer errors and more recent discovery time.
    /// </summary>
    private static bool ShouldReplace(DiscoveredAgent existing, DiscoveredAgent candidate)
    {
        // Prefer entries with manifest loaded
        if (existing.ManifestLoaded != candidate.ManifestLoaded)
        {
            return candidate.ManifestLoaded;
        }

        // Prefer entries with no error
        if ((existing.Error is null) != (candidate.Error is null))
        {
            return existing.Error is not null;
        }

        // Prefer more recent
        return candidate.DiscoveredAt > existing.DiscoveredAt;
    }
}

/// <summary>
///     Public interface for the discovery service (exposed to controllers).
/// </summary>
public interface IDiscoveryService
{
    /// <summary>All registered discovery sources.</summary>
    IReadOnlyList<IDiscoverySource> Sources { get; }

    /// <summary>Get all discovered agents (from cache or fresh).</summary>
    Task<IReadOnlyList<DiscoveredAgent>> GetAgentsAsync(CancellationToken ct = default);

    /// <summary>Force-refresh: re-query all enabled sources.</summary>
    Task<IReadOnlyList<DiscoveredAgent>> RefreshAsync(CancellationToken ct = default);

    /// <summary>Get agents from a specific source.</summary>
    Task<IReadOnlyList<DiscoveredAgent>> GetAgentsBySourceAsync(
        DiscoverySourceKind sourceKind, CancellationToken ct = default);

    /// <summary>Get the status of all sources.</summary>
    IReadOnlyList<SourceStatus> GetSourceStatuses();
}

/// <summary>
///     Status of a single discovery source.
/// </summary>
public sealed class SourceStatus
{
    public DiscoverySourceKind Source { get; init; }
    public required string Name { get; init; }
    public bool Enabled { get; init; }
}
