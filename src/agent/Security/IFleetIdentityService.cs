namespace Hercules.Security;

/// <summary>
///     Fleet-wide identity management service (task_055).
///     Provides identity rotation, credential revocation, and fleet identity lifecycle management.
/// </summary>
public interface IFleetIdentityService
{
    /// <summary>
    ///     Gets the current agent identity.
    /// </summary>
    Task<FleetIdentity> GetCurrentIdentityAsync(CancellationToken ct = default);

    /// <summary>
    ///     Rotates the agent identity, generating new credentials.
    /// </summary>
    Task<FleetIdentity> RotateIdentityAsync(RotationReason reason, CancellationToken ct = default);

    /// <summary>
    ///     Revokes specific credentials by their ID.
    /// </summary>
    Task RevokeCredentialAsync(string credentialId, string reason, CancellationToken ct = default);

    /// <summary>
    ///     Gets all active credentials for this agent.
    /// </summary>
    Task<IReadOnlyList<CredentialInfo>> GetActiveCredentialsAsync(CancellationToken ct = default);

    /// <summary>
    ///     Exports the current identity for fleet sync (without secrets).
    /// </summary>
    Task<FleetIdentityExport> ExportIdentityAsync(CancellationToken ct = default);
}

/// <summary>
///     Agent identity within the fleet.
/// </summary>
public sealed record FleetIdentity(
    string AgentId,
    string CurrentCredentialId,
    DateTime IssuedAt,
    DateTime ExpiresAt,
    string PublicKeyFingerprint,
    IReadOnlyList<string> Roles);

/// <summary>
///     Exported identity for fleet sync (without secrets).
/// </summary>
public sealed record FleetIdentityExport(
    string AgentId,
    string CredentialId,
    DateTime IssuedAt,
    DateTime ExpiresAt,
    string PublicKeyFingerprint,
    IReadOnlyList<string> Roles,
    DateTime ExportedAt);

/// <summary>
///     Credential information (without secret).
/// </summary>
public sealed record CredentialInfo(
    string CredentialId,
    string Type,
    DateTime IssuedAt,
    DateTime ExpiresAt,
    bool IsActive,
    string? RevokedReason);

/// <summary>
///     Reason for identity rotation.
/// </summary>
public enum RotationReason
{
    Scheduled,
    SecurityIncident,
    CredentialExpiry,
    PolicyChange,
    Manual
}
