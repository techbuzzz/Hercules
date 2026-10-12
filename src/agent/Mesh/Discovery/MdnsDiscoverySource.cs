using Hercules.Config;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Discovery;

/// <summary>
///     Discovery source that uses mDNS/Bonjour (DNS-SD multicast) to find peers
///     advertising the Hercules mesh service on the local network.
/// </summary>
public sealed class MdnsDiscoverySource : IDiscoverySource
{
    private readonly IMdnsClient _mdns;
    private readonly DiscoveryConfig _config;
    private readonly ILogger<MdnsDiscoverySource> _logger;

    public MdnsDiscoverySource(
        IMdnsClient mdns,
        DiscoveryConfig config,
        ILogger<MdnsDiscoverySource> logger)
    {
        _mdns = mdns ?? throw new ArgumentNullException(nameof(mdns));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public DiscoverySourceKind Source => DiscoverySourceKind.Mdns;
    public string Name => "mDNS/Bonjour";

    public bool IsEnabled => _config.EnableMdns;

    public async Task<DiscoveryResult> DiscoverAsync(CancellationToken ct = default)
    {
        if (!_config.EnableMdns)
        {
            return new DiscoveryResult
            {
                Agents = new List<DiscoveredAgent>(),
                Source = Source,
                SourceName = Name,
                Success = true,
                Error = null
            };
        }

        var agents = new List<DiscoveredAgent>();
        string serviceType = _config.MdnsServiceType ?? "_hercules._tcp";

        try
        {
            await foreach (MdnsServiceInstance instance in _mdns.BrowseAsync(serviceType, ct))
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    var resolved = await _mdns.ResolveAsync(instance, ct);
                    var endpoint = resolved?.Endpoint ?? $"http://{instance.HostName}:{instance.Port}";
                    var manifestUrl = $"{endpoint.TrimEnd('/')}/{Hercules.BuiltIn.AgentManifestFileName}";

                    // Extract agentId from TXT record if available
                    var agentId = instance.TxtRecords.TryGetValue("agentId", out var txtAgentId)
                        ? txtAgentId
                        : SanitizeAgentId(instance.Name);

                    agents.Add(new DiscoveredAgent
                    {
                        AgentId = agentId,
                        DisplayName = instance.TxtRecords.TryGetValue("displayName", out var dn)
                            ? dn
                            : instance.Name,
                        Endpoint = endpoint,
                        ManifestUrl = manifestUrl,
                        Source = Source,
                        DiscoveredAt = DateTimeOffset.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to resolve mDNS instance {Name}", instance.Name);
                    agents.Add(new DiscoveredAgent
                    {
                        AgentId = SanitizeAgentId(instance.Name),
                        DisplayName = instance.Name,
                        Endpoint = "",
                        Source = Source,
                        DiscoveredAt = DateTimeOffset.UtcNow,
                        Error = ex.Message
                    });
                }
            }

            return new DiscoveryResult
            {
                Agents = agents,
                Source = Source,
                SourceName = Name,
                Success = true,
                Error = null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "mDNS browse failed for service type {ServiceType}", serviceType);
            return new DiscoveryResult
            {
                Agents = agents,
                Source = Source,
                SourceName = Name,
                Success = false,
                Error = ex.Message
            };
        }
    }

    private static string SanitizeAgentId(string name)
    {
        // Strip domain suffix from mDNS name: "hercules-peer._hercules._tcp.local" -> "hercules-peer"
        return name.Split('.')[0];
    }
}
