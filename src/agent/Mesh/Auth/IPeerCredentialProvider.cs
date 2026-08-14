using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Hercules.Mesh.Schema;

namespace Hercules.Mesh.Auth;

/// <summary>
///     Resolves outbound credentials (Authorization header + custom headers) for a given
///     peer agent. Implementations can issue short-lived bearer tokens, return static API keys,
///     or proxy mTLS client certs.
///     Specification: docs/ROADMAP-RU.md Phase 3 task_039.
/// </summary>
public interface IPeerCredentialProvider
{
    /// <summary>
    ///     Resolve credentials for an outbound call to <paramref name="targetAgentId"/>.
    ///     Returns null if the peer doesn't require auth (anonymous, mTLS handled at socket level).
    /// </summary>
    /// <param name="targetAgentId">The peer AgentId (recipient).</param>
    /// <param name="auth">Optional existing auth context (for delegation).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Credentials to apply to the HTTP request, or null.</returns>
    Task<PeerCredentials?> ResolveAsync(
        string targetAgentId,
        AuthContext? auth,
        CancellationToken ct = default);
}

/// <summary>
///     Outbound credentials bundle: a bearer token (Authorization header) and optional
///     custom headers (X-Api-Key, X-Delegation-Depth, X-Root-Request-Id, etc.).
/// </summary>
public sealed class PeerCredentials
{
    /// <summary>Bearer token to set in the Authorization header. Null = use custom headers only.</summary>
    public string? BearerToken { get; init; }

    /// <summary>Custom headers (X-Api-Key, X-Agent-Id, etc.).</summary>
    public Dictionary<string, string> CustomHeaders { get; init; } = new();

    /// <summary>Auth method used ("bearer" | "apikey").</summary>
    public string AuthMethod { get; init; } = "bearer";

    /// <summary>Apply these credentials to an HttpRequestHeaders collection.</summary>
    public void Apply(System.Net.Http.Headers.HttpRequestHeaders headers)
    {
        if (!string.IsNullOrEmpty(BearerToken))
        {
            headers.TryAddWithoutValidation("Authorization", $"Bearer {BearerToken}");
        }

        foreach (var kvp in CustomHeaders)
        {
            headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
        }
    }
}
