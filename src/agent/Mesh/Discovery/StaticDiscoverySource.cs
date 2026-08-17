using System.Net.Http.Json;
using Hercules.Config;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Discovery;

/// <summary>
///     Discovery source that reads peers from static configuration (MeshConfig.Peers)
///     and optionally fetches their manifests to populate capabilities.
/// </summary>
public sealed class StaticDiscoverySource : IDiscoverySource
{
    private readonly MeshConfig _meshCfg;
    private readonly HttpClient _http;
    private readonly ILogger<StaticDiscoverySource> _logger;
    private readonly string _selfAgentId;

    public StaticDiscoverySource(
        MeshConfig meshCfg,
        HttpClient http,
        string selfAgentId,
        ILogger<StaticDiscoverySource> logger)
    {
        _meshCfg = meshCfg ?? throw new ArgumentNullException(nameof(meshCfg));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _selfAgentId = selfAgentId ?? throw new ArgumentNullException(nameof(selfAgentId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public DiscoverySourceKind Source => DiscoverySourceKind.Static;
    public string Name => "Static peers";
    public bool IsEnabled => _meshCfg.Peers.Count > 0;

    public async Task<DiscoveryResult> DiscoverAsync(CancellationToken ct = default)
    {
        var agents = new List<DiscoveredAgent>();
        var errors = new List<string>();

        foreach (MeshPeerConfig peer in _meshCfg.Peers)
        {
            if (string.IsNullOrWhiteSpace(peer.AgentId) || string.IsNullOrWhiteSpace(peer.Endpoint))
            {
                _logger.LogWarning("Static peer has empty AgentId or Endpoint, skipping: {AgentId}", peer.AgentId);
                continue;
            }

            // Skip self
            if (peer.AgentId.Equals(_selfAgentId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var discovered = new DiscoveredAgent
            {
                AgentId = peer.AgentId,
                DisplayName = peer.AgentId,
                Endpoint = peer.Endpoint,
                Source = Source,
                DiscoveredAt = DateTimeOffset.UtcNow,
                ManifestUrl = NormalizeManifestUrl(peer.Endpoint)
            };

            // Optionally fetch manifest to populate capabilities
            if (!string.IsNullOrWhiteSpace(discovered.ManifestUrl))
            {
                try
                {
                    var manifest = await FetchManifestAsync(discovered.ManifestUrl, ct);
                    if (manifest is not null)
                    {
                        discovered = discovered with
                        {
                            DisplayName = !string.IsNullOrWhiteSpace(manifest.DisplayName)
                                ? manifest.DisplayName
                                : peer.AgentId,
                            Capabilities = manifest.Capabilities.Select(c => c.Name).ToList(),
                            ManifestLoaded = true
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch manifest for peer {AgentId} from {Url}",
                        peer.AgentId, discovered.ManifestUrl);
                    discovered = discovered with { Error = ex.Message };
                }
            }

            agents.Add(discovered);
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

    private async Task<AgentManifest?> FetchManifestAsync(string manifestUrl, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, manifestUrl);
            using var response = await _http.SendAsync(req, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<AgentManifest>(cancellationToken: ct);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeManifestUrl(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return "";
        }

        var baseUrl = endpoint.TrimEnd('/');
        // Prefer well-known Agent Card URL (A2A spec) over legacy manifest path
        return $"{baseUrl}/{Hercules.BuiltIn.AgentManifestFileName}";
    }
}
