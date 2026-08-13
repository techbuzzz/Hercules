namespace Hercules.Mesh.Auth;

/// <summary>
///     Identity provider abstraction. Implementations verify peer credentials
///     (bearer tokens, API keys, mTLS client certs) and return a verified principal.
///     Specification: docs/ROADMAP-RU.md Phase 3 task_039.
/// </summary>
public interface IIdentityProvider
{
    /// <summary>Auth method this provider handles ("bearer" | "apikey" | "mtls").</summary>
    string AuthMethod { get; }

    /// <summary>
    ///     Verify a peer credential presented in the request headers.
    ///     Returns null if the credential is not for this provider.
    ///     Throws <see cref="AuthenticationException"/> if the credential is invalid.
    /// </summary>
    /// <param name="headers">Request headers (lowercased keys).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Verified identity, or null if headers don't contain credentials for this provider.</returns>
    Task<IdentityResult?> AuthenticateAsync(
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default);
}

/// <summary>
///     Thrown when peer credentials are present but invalid (expired, wrong signature,
///     unknown key, etc.). Maps to HTTP 401.
/// </summary>
public sealed class AuthenticationException : Exception
{
    public AuthenticationException(string message) : base(message) { }
    public AuthenticationException(string message, Exception inner) : base(message, inner) { }
}
