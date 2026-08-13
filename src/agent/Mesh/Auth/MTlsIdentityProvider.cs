using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Auth;

/// <summary>
///     Verifies mTLS client certificates for inter-agent calls. Stub implementation
///     for cross-platform compatibility; real verification requires platform-specific
///     handling (X.509 chain validation against a configured CA bundle).
///     On Windows: requires SslStream with ClientCertificateValidationCallback.
///     On Linux: requires custom CertificateValidation.
///     The stub accepts the cert subject from the <c>X-Client-Cert-Subject</c>
///     header (set by the upstream TLS-terminating proxy) and returns identity
///     based on the cert subject's CN field.
///     Specification: docs/ROADMAP-RU.md Phase 3 task_039.
/// </summary>
public sealed class MTlsIdentityProvider : IIdentityProvider
{
    /// <summary>Header set by the upstream TLS-terminating proxy with the client cert subject.</summary>
    public const string ClientCertSubjectHeader = "X-Client-Cert-Subject";

    private readonly ILogger<MTlsIdentityProvider> _logger;

    public MTlsIdentityProvider(ILogger<MTlsIdentityProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogWarning(
            "MTlsIdentityProvider is using stub implementation. "
            + "Configure your TLS-terminating proxy to set the X-Client-Cert-Subject header, "
            + "or extend this provider for native mTLS verification.");
    }

    public string AuthMethod => "mtls";

    public Task<IdentityResult?> AuthenticateAsync(
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default)
    {
        string? certSubject = null;
        foreach (var kvp in headers)
        {
            if (string.Equals(kvp.Key, ClientCertSubjectHeader, StringComparison.OrdinalIgnoreCase))
            {
                certSubject = kvp.Value;
                break;
            }
        }

        if (string.IsNullOrEmpty(certSubject))
        {
            return Task.FromResult<IdentityResult?>(null);
        }

        // Parse "CN=agent-id, O=..." into subject
        var subject = ExtractCn(certSubject) ?? certSubject;
        if (string.IsNullOrEmpty(subject))
        {
            throw new AuthenticationException("mTLS: client cert subject is empty.");
        }

        return Task.FromResult<IdentityResult?>(new IdentityResult
        {
            Subject = $"agent/{subject}",
            AuthMethod = "mtls",
            Audience = subject,
            Claims = new Dictionary<string, string>
            {
                ["cert_subject"] = certSubject,
                ["auth_method"] = "mtls"
            }
        });
    }

    private static string? ExtractCn(string subject)
    {
        if (string.IsNullOrEmpty(subject))
        {
            return null;
        }

        // Parse "CN=value, O=value, ..."
        foreach (var part in subject.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Substring(3).Trim();
            }
        }

        return null;
    }
}
