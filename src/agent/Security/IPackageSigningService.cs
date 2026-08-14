namespace Hercules.Security;

/// <summary>
///     Package signing and verification service (task_055).
///     Verifies signed packages and skill bundles.
/// </summary>
public interface IPackageSigningService
{
    /// <summary>
    ///     Verifies a signed package.
    /// </summary>
    Task<SignatureVerificationResult> VerifyPackageAsync(
        string packagePath,
        CancellationToken ct = default);

    /// <summary>
    ///     Verifies a signed package from stream.
    /// </summary>
    Task<SignatureVerificationResult> VerifyPackageAsync(
        Stream packageStream,
        string packageName,
        CancellationToken ct = default);

    /// <summary>
    ///     Signs a package with the agent's certificate.
    /// </summary>
    Task<string> SignPackageAsync(
        string packagePath,
        string outputPath,
        CancellationToken ct = default);

    /// <summary>
    ///     Gets the trusted signers list.
    /// </summary>
    Task<IReadOnlyList<TrustedSigner>> GetTrustedSignersAsync(CancellationToken ct = default);

    /// <summary>
    ///     Adds a signer to the trusted list.
    /// </summary>
    Task AddTrustedSignerAsync(TrustedSigner signer, CancellationToken ct = default);
}

/// <summary>
///     Result of signature verification.
/// </summary>
public sealed record SignatureVerificationResult(
    bool IsValid,
    bool IsTrusted,
    string? SignerId,
    DateTime? SignedAt,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

/// <summary>
///     Trusted signer information.
/// </summary>
public sealed record TrustedSigner(
    string SignerId,
    string Name,
    string PublicKeyFingerprint,
    DateTime AddedAt,
    string? AddedBy,
    bool IsActive);
