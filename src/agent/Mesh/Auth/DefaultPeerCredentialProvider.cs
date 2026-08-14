using Hercules.Mesh.Schema;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Auth;

/// <summary>
///     Default outbound credential provider. Resolves the right credentials
///     based on the global <see cref="PeerAuthConfig.OutboundMode"/>:
///     <list type="bullet">
///         <item><c>"bearer"</c> (default) — issue a short-lived HMAC-signed token via <see cref="TokenIssuer"/>.</item>
///         <item><c>"apikey"</c> — look up the peer's API key in <see cref="PeerAuthConfig.PeerKeys"/>.</item>
///     </list>
///     The auth context from the caller (delegation chain) is honored: the issued token's
///     scope is the intersection of the caller's scope and the default scopes,
///     and the delegation depth is incremented.
/// </summary>
public sealed class DefaultPeerCredentialProvider : IPeerCredentialProvider
{
    private readonly PeerAuthConfig _config;
    private readonly TokenIssuer _issuer;
    private readonly string _selfAgentId;
    private readonly ILogger<DefaultPeerCredentialProvider> _logger;

    public DefaultPeerCredentialProvider(
        PeerAuthConfig config,
        TokenIssuer issuer,
        string selfAgentId,
        ILogger<DefaultPeerCredentialProvider> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _issuer = issuer ?? throw new ArgumentNullException(nameof(issuer));
        _selfAgentId = selfAgentId ?? throw new ArgumentNullException(nameof(selfAgentId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<PeerCredentials?> ResolveAsync(
        string targetAgentId,
        AuthContext? auth,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(targetAgentId))
        {
            return Task.FromResult<PeerCredentials?>(null);
        }

        var mode = _config.OutboundMode?.ToLowerInvariant() ?? "bearer";

        return mode switch
        {
            "apikey" => Task.FromResult(ResolveApiKey(targetAgentId)),
            "bearer" => Task.FromResult<PeerCredentials?>(IssueBearer(targetAgentId, auth)),
            "none" => Task.FromResult<PeerCredentials?>(null),
            _ => Task.FromResult<PeerCredentials?>(IssueBearer(targetAgentId, auth))
        };
    }

    private PeerCredentials? ResolveApiKey(string targetAgentId)
    {
        if (!_config.PeerKeys.TryGetValue(targetAgentId, out var apiKey) ||
            string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning(
                "API key mode: no key configured for peer {Peer}. Auth will be anonymous.",
                targetAgentId);
            return null;
        }

        return new PeerCredentials
        {
            AuthMethod = "apikey",
            CustomHeaders = new Dictionary<string, string>
            {
                ["X-Api-Key"] = apiKey,
                ["X-Agent-Id"] = _selfAgentId
            }
        };
    }

    private PeerCredentials? IssueBearer(string targetAgentId, AuthContext? auth)
    {
        try
        {
            // Determine scope: intersection of caller's scope and configured defaults
            var scope = string.Join(",", _config.DefaultScopes);
            var depth = 0;
            string? rootRequestId = null;

            if (auth is not null)
            {
                depth = auth.DelegationDepth + 1;
                rootRequestId = auth.RootRequestId;

                // Reduce scope to intersection with caller's scope
                var callerScope = auth.ClaimsScope();
                if (!string.IsNullOrEmpty(callerScope))
                {
                    var callerScopes = callerScope
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var defaultScopes = _config.DefaultScopes;
                    var intersection = callerScopes
                        .Intersect(defaultScopes, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    scope = intersection.Length > 0
                        ? string.Join(",", intersection)
                        : "delegate:read"; // safe minimum
                }
            }

            if (depth > _config.MaxDelegationDepth)
            {
                _logger.LogWarning(
                    "Cannot issue token to {Peer}: depth {Depth} exceeds max {Max}",
                    targetAgentId, depth, _config.MaxDelegationDepth);
                return null;
            }

            var claims = new Dictionary<string, string>
            {
                ["agent_id"] = _selfAgentId,
                ["auth_method"] = "bearer"
            };

            var token = _issuer.Issue(
                subject: $"agent/{_selfAgentId}",
                audience: $"agent/{targetAgentId}",
                scope: scope,
                delegationDepth: depth,
                claims: claims,
                rootRequestId: rootRequestId);

            return new PeerCredentials
            {
                AuthMethod = "bearer",
                BearerToken = token,
                CustomHeaders = depth > 0
                    ? new Dictionary<string, string> { ["X-Delegation-Depth"] = depth.ToString() }
                    : new Dictionary<string, string>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to issue bearer token for peer {Peer}", targetAgentId);
            return null;
        }
    }
}

/// <summary>
///     Extension to AuthContext to support a `claims_scope` claim that mirrors DelegationToken.Scope.
///     AuthContext uses Claims dict; this provides a convenience accessor.
/// </summary>
public static class AuthContextExtensions
{
    /// <summary>Get the scope claim from an AuthContext (key: "scope").</summary>
    public static string? ClaimsScope(this AuthContext auth)
    {
        return auth.Claims.TryGetValue("scope", out var s) ? s : null;
    }
}
