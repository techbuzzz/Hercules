namespace Hercules.Security;

/// <summary>
///     Certificate management service (task_055).
///     Handles certificate renewal, validation, and lifecycle management.
/// </summary>
public interface ICertificateService
{
    /// <summary>
    ///     Gets the current agent certificate.
    /// </summary>
    Task<CertificateInfo?> GetCurrentCertificateAsync(CancellationToken ct = default);

    /// <summary>
    ///     Checks if the current certificate needs renewal.
    /// </summary>
    Task<RenewalCheckResult> CheckRenewalNeededAsync(CancellationToken ct = default);

    /// <summary>
    ///     Renews the agent certificate.
    /// </summary>
    Task<CertificateInfo> RenewCertificateAsync(string reason, CancellationToken ct = default);

    /// <summary>
    ///     Validates a certificate chain.
    /// </summary>
    Task<ValidationResult> ValidateCertificateAsync(byte[] certificateData, CancellationToken ct = default);

    /// <summary>
    ///     Exports certificate for external use (public cert only).
    /// </summary>
    Task<string> ExportCertificateAsync(string certificateId, CancellationToken ct = default);
}

/// <summary>
///     Certificate information.
/// </summary>
public sealed record CertificateInfo(
    string CertificateId,
    string Subject,
    string Issuer,
    DateTime IssuedAt,
    DateTime ExpiresAt,
    string Thumbprint,
    string PublicKeyAlgorithm,
    int KeySize,
    bool IsSelfSigned);

/// <summary>
///     Result of certificate renewal check.
/// </summary>
public sealed record RenewalCheckResult(
    bool NeedsRenewal,
    bool IsExpired,
    int DaysUntilExpiry,
    string? RecommendedAction);

/// <summary>
///     Certificate validation result.
/// </summary>
public sealed record ValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
