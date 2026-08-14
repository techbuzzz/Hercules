using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Discovery;

/// <summary>
///     Discovery source that re-exports agents from the local capability registry.
///     Provides a unified view of agents that have registered themselves via the mesh protocol.
/// </summary>
public sealed class RegistryDiscoverySource : IDiscoverySource
{
    private readonly CapabilityRegistry _registry;
    private readonly ILogger<RegistryDiscoverySource> _logger;
    private readonly string _selfAgentId;

    public RegistryDiscoverySource(
        CapabilityRegistry registry,
        string selfAgentId,
        ILogger<RegistryDiscoverySource> logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _selfAgentId = selfAgentId ?? throw new ArgumentNullException(nameof(selfAgentId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public DiscoverySourceKind Source => DiscoverySourceKind.Registry;
    public string Name => "Capability registry";
    public bool IsEnabled => true; // Always available; re-exports whatever is in the registry

    public Task<DiscoveryResult> DiscoverAsync(CancellationToken ct = default)
    {
        try
        {
            List<RegistryAgentEntry> entries = _registry.ListAgents();

            var agents = entries
                .Where(e => !e.AgentId.Equals(_selfAgentId, StringComparison.OrdinalIgnoreCase))
                .Select(e => new DiscoveredAgent
                {
                    AgentId = e.AgentId,
                    DisplayName = e.DisplayName,
                    Endpoint = e.Endpoint,
                    Source = Source,
                    DiscoveredAt = DateTimeOffset.UtcNow
                })
                .ToList();

            return Task.FromResult(new DiscoveryResult
            {
                Agents = agents,
                Source = Source,
                SourceName = Name,
                Success = true,
                Error = null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query capability registry for discovery");
            return Task.FromResult(new DiscoveryResult
            {
                Agents = new List<DiscoveredAgent>(),
                Source = Source,
                SourceName = Name,
                Success = false,
                Error = ex.Message
            });
        }
    }
}
