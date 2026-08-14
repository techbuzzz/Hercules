using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Auth;

/// <summary>
///     Issues and verifies HMAC-SHA256 signed delegation tokens for inter-agent auth.
///     Tokens are short-lived (default 5 min) and carry identity claims + tool authority scope.
///     The token format is a compact JSON payload, base64url-encoded, with a "." + HMAC signature.
///     Format: <c>{base64url-payload}.{base64url-signature}</c>.
///     Specification: docs/ROADMAP-RU.md Phase 3 task_039.
/// </summary>
public sealed class TokenIssuer
{
    private readonly byte[] _secretBytes;
    private readonly PeerAuthConfig _config;
    private readonly ILogger<TokenIssuer> _logger;

    public TokenIssuer(PeerAuthConfig config, ILogger<TokenIssuer> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var secret = config.TokenSecret;
        if (string.IsNullOrEmpty(secret))
        {
            // Generate a random secret for this process (won't survive restarts).
            // Production deployments should set HERCULES_SECRET_MESH_TOKEN_SECRET.
            secret = GenerateRandomSecret();
            _logger.LogWarning(
                "PeerAuth.TokenSecret is empty; using an ephemeral random secret. "
                + "Set the env var HERCULES_SECRET_MESH_TOKEN_SECRET for stable tokens across restarts.");
        }

        _secretBytes = Encoding.UTF8.GetBytes(secret);
    }

    /// <summary>
    ///     Issue a new delegation token for a peer call.
    /// </summary>
    /// <param name="subject">Agent ID of the caller (e.g. "agent/hercules").</param>
    /// <param name="audience">Agent ID of the intended recipient.</param>
    /// <param name="scope">Tool authority scope (comma-separated).</param>
    /// <param name="delegationDepth">Delegation depth (0 = original call).</param>
    /// <param name="claims">Optional identity claims.</param>
    /// <param name="rootRequestId">Root request ID for chain audit.</param>
    /// <returns>Signed token string (base64url-payload.base64url-signature).</returns>
    public string Issue(
        string subject,
        string audience,
        string scope,
        int delegationDepth = 0,
        Dictionary<string, string>? claims = null,
        string? rootRequestId = null)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("Subject is required.", nameof(subject));
        }

        if (delegationDepth < 0)
        {
            throw new ArgumentException("Delegation depth cannot be negative.", nameof(delegationDepth));
        }

        if (delegationDepth > _config.MaxDelegationDepth)
        {
            throw new InvalidOperationException(
                $"Delegation depth {delegationDepth} exceeds max {_config.MaxDelegationDepth}.");
        }

        var now = DateTimeOffset.UtcNow;
        var token = new DelegationToken
        {
            Subject = subject,
            Issuer = subject, // Self-issued for now (could be a separate root CA)
            Audience = audience,
            IssuedAt = now,
            ExpiresAt = now.AddSeconds(_config.TokenTtlSeconds),
            DelegationDepth = delegationDepth,
            Scope = scope,
            Claims = claims ?? new Dictionary<string, string>(),
            RootRequestId = rootRequestId
        };

        return SignToken(token);
    }

    /// <summary>
    ///     Issue a reduced-scope delegation token (called by intermediate agents
    ///     to further delegate authority, with smaller scope and increased depth).
    /// </summary>
    public string Delegate(
        string parentToken,
        string audience,
        string reducedScope,
        string rootRequestId)
    {
        DelegationToken? parent = Verify(parentToken);
        if (parent is null)
        {
            throw new AuthenticationException("Parent token is invalid or expired.");
        }

        // Scope reduction: child scope must be a subset of parent scope
        if (!IsScopeSubset(reducedScope, parent.Scope))
        {
            throw new AuthenticationException(
                $"Reduced scope '{reducedScope}' is not a subset of parent scope '{parent.Scope}'.");
        }

        return Issue(
            subject: parent.Subject,
            audience: audience,
            scope: reducedScope,
            delegationDepth: parent.DelegationDepth + 1,
            claims: new Dictionary<string, string>(parent.Claims),
            rootRequestId: parent.RootRequestId ?? rootRequestId);
    }

    /// <summary>
    ///     Verify a delegation token. Returns the parsed token or null if invalid.
    ///     Throws <see cref="AuthenticationException"/> for signature mismatches or expired tokens.
    /// </summary>
    public DelegationToken? Verify(string tokenString)
    {
        if (string.IsNullOrEmpty(tokenString))
        {
            return null;
        }

        var parts = tokenString.Split('.');
        if (parts.Length != 2)
        {
            return null;
        }

        var payloadJsonBytes = Base64UrlDecode(parts[0]);
        var payloadJson = Encoding.UTF8.GetString(payloadJsonBytes);
        var providedSignature = Base64UrlDecode(parts[1]);

        var expectedSignature = ComputeHmac(payloadJson);
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, providedSignature))
        {
            throw new AuthenticationException("Token signature mismatch.");
        }

        var token = DelegationToken.FromJson(payloadJson);
        if (token is null)
        {
            throw new AuthenticationException("Token payload is malformed.");
        }

        if (token.IsExpired)
        {
            throw new AuthenticationException("Token has expired.");
        }

        if (token.DelegationDepth > _config.MaxDelegationDepth)
        {
            throw new AuthenticationException(
                $"Token delegation depth {token.DelegationDepth} exceeds max {_config.MaxDelegationDepth}.");
        }

        return token;
    }

    /// <summary>Sign a DelegationToken and return the compact token string.</summary>
    private string SignToken(DelegationToken token)
    {
        var payloadJson = token.ToJson();
        var signature = ComputeHmac(payloadJson);
        return $"{Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson))}.{Base64UrlEncode(signature)}";
    }

    private byte[] ComputeHmac(string payloadJson)
    {
        using var hmac = new HMACSHA256(_secretBytes);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadJson));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        var b64 = s.Replace('-', '+').Replace('_', '/');
        switch (b64.Length % 4)
        {
            case 2: b64 += "=="; break;
            case 3: b64 += "="; break;
        }

        return Convert.FromBase64String(b64);
    }

    /// <summary>Check whether childScope is a subset of parentScope (both comma-separated).</summary>
    public static bool IsScopeSubset(string childScope, string parentScope)
    {
        if (string.IsNullOrEmpty(childScope))
        {
            return true; // empty scope is always a subset
        }

        if (string.IsNullOrEmpty(parentScope))
        {
            return false;
        }

        var parent = parentScope
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .ToHashSet();

        var child = childScope
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant());

        return child.All(parent.Contains);
    }

    /// <summary>Generate a random 32-byte secret encoded as base64.</summary>
    public static string GenerateRandomSecret()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}
