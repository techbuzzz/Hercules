using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hercules.Config;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.A2A;

/// <summary>
///     Реализация IAgentCardService.
///     Конвертирует локальный AgentManifest в A2A Agent Card,
///     импортирует remote Agent Cards, публикует локальный кард.
/// </summary>
public sealed class AgentCardService : IAgentCardService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AgentManifestService _manifestService;
    private readonly A2AConfig _a2aCfg;
    private readonly HttpClient _http;
    private readonly ILogger<AgentCardService> _logger;

    private AgentCard? _cachedCard;
    private DateTime? _cachedAt;

    public AgentCardService(
        AgentManifestService manifestService,
        A2AConfig a2aCfg,
        HttpClient httpClient,
        ILogger<AgentCardService> logger)
    {
        _manifestService = manifestService ?? throw new ArgumentNullException(nameof(manifestService));
        _a2aCfg = a2aCfg ?? throw new ArgumentNullException(nameof(a2aCfg));
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<AgentCard> GetAgentCardAsync(CancellationToken ct = default)
    {
        if (_cachedCard is not null && IsCurrent())
        {
            _logger.LogDebug("[AgentCard] Serving from cache (fresh)");
            return _cachedCard;
        }

        AgentManifest manifest = _manifestService.Current;
        _cachedCard = FromManifest(manifest);
        _cachedAt = DateTime.UtcNow;
        _logger.LogDebug("[AgentCard] Generated from manifest: {Name}", _cachedCard.Name);
        return _cachedCard;
    }

    /// <inheritdoc />
    public async Task<AgentCard> ImportFromUrlAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL не может быть пустым", nameof(url));

        _logger.LogInformation("[AgentCard] Importing from {Url}", url);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using HttpResponseMessage resp = await _http.SendAsync(req, ct);

            if (!resp.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"HTTP {resp.StatusCode} при импорте Agent Card с {url}");
            }

            string json = await resp.Content.ReadAsStringAsync(ct);
            var card = JsonSerializer.Deserialize<AgentCard>(json, JsonOpts)
                       ?? throw new InvalidOperationException("Agent Card deserialized to null");

            ValidateCard(card, url);
            _logger.LogInformation("[AgentCard] Successfully imported: {Name} ({Url})", card.Name, url);
            return card;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[AgentCard] Invalid JSON in Agent Card from {Url}", url);
            throw new InvalidOperationException($"Invalid Agent Card JSON from {url}: {ex.Message}", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[AgentCard] HTTP error importing from {Url}", url);
            throw new InvalidOperationException($"Failed to fetch Agent Card from {url}: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public bool IsCurrent()
    {
        if (_cachedCard is null || _cachedAt is null)
            return false;

        var ttl = TimeSpan.FromMinutes(_a2aCfg.AgentCard.CacheTtlMinutes);
        return DateTime.UtcNow - _cachedAt.Value < ttl;
    }

    /// <inheritdoc />
    public async Task<string> PublishAsync(CancellationToken ct = default)
    {
        if (!_a2aCfg.AgentCard.Publish)
        {
            _logger.LogDebug("[AgentCard] Publishing disabled in config");
            return "";
        }

        AgentCard card = await GetAgentCardAsync(ct);
        string endpoint = _a2aCfg.AgentCard.Endpoint.TrimStart('/');
        string? dir = Path.GetDirectoryName(endpoint);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(card, JsonOpts);
        string temp = endpoint + ".tmp";
        await File.WriteAllTextAsync(temp, json, Encoding.UTF8, ct);
        File.Move(temp, endpoint, true);

        _logger.LogInformation("[AgentCard] Published to {Path}", endpoint);
        return endpoint;
    }

    /// <inheritdoc />
    public async Task<List<(string Url, AgentCard Card)>> DiscoverAsync(
        IEnumerable<string> urls,
        CancellationToken ct = default)
    {
        var results = new List<(string Url, AgentCard Card)>();

        foreach (string url in urls)
        {
            if (string.IsNullOrWhiteSpace(url))
                continue;

            try
            {
                AgentCard card = await ImportFromUrlAsync(url, ct);
                results.Add((url, card));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AgentCard] Failed to discover Agent Card from {Url}", url);
            }
        }

        return results;
    }

    /// <summary>
    ///     Конвертировать локальный AgentManifest в A2A AgentCard.
    /// </summary>
    internal AgentCard FromManifest(AgentManifest manifest)
    {
        var card = new AgentCard
        {
            Name = manifest.AgentId,
            Description = manifest.Description,
            Url = manifest.Endpoint,
            Version = manifest.Version,
            GeneratedAt = DateTime.UtcNow.ToString("o"),
            Tags = manifest.Tags?.Keys.ToList(),
            DefaultInputModes = new List<string> { "text", "json" },
            DefaultOutputModes = new List<string> { "text", "json" },
            Capabilities = new AgentCardCapabilities
            {
                // Hercules не поддерживает streaming JSON-RPC пока — ставим false
                Streaming = false,
                PushNotifications = false,
                StateTransitionReports = false,
                MultipartResponses = false
            },
            Authentication = manifest.Auth.Type == "none"
                ? null
                : new A2AAuthentication
                {
                    Schemes = new List<string> { manifest.Auth.Type },
                    Credentials = $"Configure via {manifest.Auth.Header ?? "X-Api-Key"} header"
                },
            Provider = new A2AProvider
            {
                Organization = "Hercules Project",
                Url = null
            },
            Skills = manifest.Skills.Select(s => new AgentCardSkill
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description,
                Tags = s.PhraseReceivers,
                Version = s.Version
            }).ToList()
        };

        return card;
    }

    private static void ValidateCard(AgentCard card, string source)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(card.Name))
            errors.Add("AgentCard.name is required");

        if (string.IsNullOrWhiteSpace(card.Url))
            errors.Add("AgentCard.url is required");

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"Invalid Agent Card from {source}: {string.Join("; ", errors)}");
    }
}
