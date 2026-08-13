using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Auth;

/// <summary>
///     Verifies HMAC-SHA256 signed bearer tokens for inter-agent calls.
///     Token format: <c>Authorization: Bearer {base64url-payload}.{base64url-signature}</c>.
/// </summary>
public sealed class BearerIdentityProvider : IIdentityProvider
{
    /// <summary>HTTP header for bearer auth.</summary>
    public const string AuthorizationHeader = "Authorization";

    /// <summary>Bearer scheme prefix.</summary>
    public const string BearerScheme = "Bearer ";

    private readonly TokenIssuer _issuer;
    private readonly ILogger<BearerIdentityProvider> _logger;

    public BearerIdentityProvider(TokenIssuer issuer, ILogger<BearerIdentityProvider> logger)
    {
        _issuer = issuer ?? throw new ArgumentNullException(nameof(issuer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string AuthMethod => "bearer";

    public Task<IdentityResult?> AuthenticateAsync(
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default)
    {
        // Case-insensitive header lookup
        string? authHeader = null;
        foreach (var kvp in headers)
        {
            if (string.Equals(kvp.Key, AuthorizationHeader, StringComparison.OrdinalIgnoreCase))
            {
                authHeader = kvp.Value;
                break;
            }
        }

        if (string.IsNullOrEmpty(authHeader))
        {
            return Task.FromResult<IdentityResult?>(null);
        }

        if (!authHeader.StartsWith(BearerScheme, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IdentityResult?>(null);
        }

        var token = authHeader.Substring(BearerScheme.Length).Trim();
        if (string.IsNullOrEmpty(token))
        {
            throw new AuthenticationException("Bearer token is empty.");
        }

        DelegationToken? verified;
        try
        {
            verified = _issuer.Verify(token);
        }
        catch (AuthenticationException)
        {
            // Re-throw to be caught by middleware
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bearer token verification failed");
            throw new AuthenticationException("Bearer token verification failed.", ex);
        }

        if (verified is null)
        {
            throw new AuthenticationException("Bearer token could not be verified.");
        }

        return Task.FromResult<IdentityResult?>(new IdentityResult
        {
            Subject = verified.Subject,
            AuthMethod = "bearer",
            Issuer = verified.Issuer,
            Audience = verified.Audience,
            Scopes = verified.GetScopes(),
            ExpiresAt = verified.ExpiresAt,
            DelegationDepth = verified.DelegationDepth,
            RootRequestId = verified.RootRequestId,
            Claims = verified.Claims
        });
    }
}
