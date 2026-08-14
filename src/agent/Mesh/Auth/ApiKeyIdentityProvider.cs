using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Auth;

/// <summary>
///     Verifies API keys for inter-agent calls. Each peer has a configured key
///     in <see cref="PeerAuthConfig.PeerKeys"/>. Keys are compared in constant time.
///     Header: <c>X-Api-Key: {key}</c>. Optional: <c>X-Agent-Id: {peer-agent-id}</c> for subject claim.
/// </summary>
public sealed class ApiKeyIdentityProvider : IIdentityProvider
{
    public const string ApiKeyHeader = "X-Api-Key";
    public const string AgentIdHeader = "X-Agent-Id";

    private readonly PeerAuthConfig _config;
    private readonly ILogger<ApiKeyIdentityProvider> _logger;

    public ApiKeyIdentityProvider(PeerAuthConfig config, ILogger<ApiKeyIdentityProvider> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string AuthMethod => "apikey";

    public Task<IdentityResult?> AuthenticateAsync(
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default)
    {
        string? apiKey = null;
        string? agentId = null;

        foreach (var kvp in headers)
        {
            if (string.Equals(kvp.Key, ApiKeyHeader, StringComparison.OrdinalIgnoreCase))
            {
                apiKey = kvp.Value;
            }
            else if (string.Equals(kvp.Key, AgentIdHeader, StringComparison.OrdinalIgnoreCase))
            {
                agentId = kvp.Value;
            }
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            return Task.FromResult<IdentityResult?>(null);
        }

        if (string.IsNullOrEmpty(agentId))
        {
            throw new AuthenticationException(
                $"{AgentIdHeader} header is required when using API key auth.");
        }

        if (!_config.PeerKeys.TryGetValue(agentId, out var expectedKey) ||
            string.IsNullOrEmpty(expectedKey))
        {
            _logger.LogWarning("API key auth: unknown agent ID {AgentId}", agentId);
            throw new AuthenticationException(
                $"API key auth: no key configured for agent '{agentId}'.");
        }

        // Constant-time compare to prevent timing attacks
        var provided = Encoding.UTF8.GetBytes(apiKey);
        var expected = Encoding.UTF8.GetBytes(expectedKey);
        if (provided.Length != expected.Length ||
            !CryptographicOperations.FixedTimeEquals(provided, expected))
        {
            _logger.LogWarning("API key auth: invalid key for agent {AgentId}", agentId);
            throw new AuthenticationException(
                $"API key auth: invalid key for agent '{agentId}'.");
        }

        return Task.FromResult<IdentityResult?>(new IdentityResult
        {
            Subject = $"agent/{agentId}",
            AuthMethod = "apikey",
            Audience = agentId,
            Scopes = _config.DefaultScopes,
            Claims = new Dictionary<string, string>
            {
                ["agent_id"] = agentId,
                ["auth_method"] = "apikey"
            }
        });
    }
}
