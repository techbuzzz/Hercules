using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hercules.Mesh.Auth;

/// <summary>
///     A signed delegation token carried in the <c>Authorization: Bearer ...</c> header
///     for inter-agent calls. Contains identity claims and a tool authority scope.
///     The token is signed with HMAC-SHA256 over the JSON payload to prevent tampering.
///     Specification: docs/ROADMAP-RU.md Phase 3 task_039.
/// </summary>
public sealed class DelegationToken
{
    /// <summary>Текущая версия формата токена (semver).</summary>
    public const string CurrentVersion = "1.0";

    /// <summary>Версия формата токена.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = CurrentVersion;

    /// <summary>Subject (agent ID of the caller, e.g. "agent/hercules").</summary>
    [JsonPropertyName("sub")]
    public string Subject { get; set; } = "";

    /// <summary>Issuer (who minted the token, e.g. "agent/root" or "hercules-main").</summary>
    [JsonPropertyName("iss")]
    public string Issuer { get; set; } = "";

    /// <summary>Audience (intended recipient agent ID, e.g. "agent/csharp-refactor").</summary>
    [JsonPropertyName("aud")]
    public string Audience { get; set; } = "";

    /// <summary>UTC time of issuance (ISO 8601).</summary>
    [JsonPropertyName("iat")]
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>UTC time of expiry (ISO 8601). Tokens are short-lived.</summary>
    [JsonPropertyName("exp")]
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddMinutes(5);

    /// <summary>Unique JWT-style ID. Used for replay protection.</summary>
    [JsonPropertyName("jti")]
    public string JwtId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Delegation depth (0 = original call, 1 = first hop, ...).</summary>
    [JsonPropertyName("depth")]
    public int DelegationDepth { get; set; } = 0;

    /// <summary>
    ///     Comma-separated list of allowed tool authorities (e.g. "read,delegate:write").
    ///     On each delegation, scope is reduced to a subset of the parent's scope.
    /// </summary>
    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "";

    /// <summary>Identity claims (subject, role, team, etc.).</summary>
    [JsonPropertyName("claims")]
    public Dictionary<string, string> Claims { get; set; } = new();

    /// <summary>Root request ID (first sender in the chain).</summary>
    [JsonPropertyName("root_req")]
    public string? RootRequestId { get; set; }

    /// <summary>Whether the token has expired (UTC now > ExpiresAt).</summary>
    [JsonIgnore]
    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;

    /// <summary>Get the list of scopes as a string array.</summary>
    public string[] GetScopes() => string.IsNullOrEmpty(Scope)
        ? Array.Empty<string>()
        : Scope.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Check whether the token has a specific scope.</summary>
    public bool HasScope(string scope)
    {
        if (string.IsNullOrEmpty(scope))
        {
            return false;
        }

        return GetScopes().Contains(scope, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>JSON serialization for compact transport.</summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOpts);
    }

    public static DelegationToken? FromJson(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<DelegationToken>(json, JsonOpts);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}

/// <summary>
///     Result of a successful identity verification. Carries the verified principal
///     (subject), auth method, and granted scopes for the request.
/// </summary>
public sealed class IdentityResult
{
    /// <summary>Subject of the verified principal (e.g. "agent/hercules").</summary>
    public required string Subject { get; init; }

    /// <summary>Authentication method used ("bearer" | "apikey" | "mtls" | "none").</summary>
    public required string AuthMethod { get; init; }

    /// <summary>Issuer of the token (only for "bearer" method).</summary>
    public string? Issuer { get; init; }

    /// <summary>Audience the token was issued for.</summary>
    public string? Audience { get; init; }

    /// <summary>Granted scopes (tool authorities).</summary>
    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();

    /// <summary>Token expiry (for "bearer" method).</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Delegation depth (0 = original call).</summary>
    public int DelegationDepth { get; init; }

    /// <summary>Root request ID (for audit trail).</summary>
    public string? RootRequestId { get; init; }

    /// <summary>Raw claims (for finer-grained policy decisions).</summary>
    public IReadOnlyDictionary<string, string> Claims { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Check if the identity has a specific scope.</summary>
    public bool HasScope(string scope)
    {
        if (string.IsNullOrEmpty(scope))
        {
            return false;
        }

        return Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Create an anonymous identity (no auth).</summary>
    public static IdentityResult Anonymous()
    {
        return new IdentityResult
        {
            Subject = "anonymous",
            AuthMethod = "none"
        };
    }
}
