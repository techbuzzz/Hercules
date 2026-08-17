using System.Security.Cryptography;
using System.Text.Json;
using Hercules.Audit;
using Hercules.Config;
using Microsoft.Extensions.Logging;

namespace Hercules.Security;

/// <summary>
///     Fleet-wide identity management service implementation (task_055).
///     Manages agent identity, credential rotation, and revocation.
/// </summary>
public sealed class FleetIdentityService : IFleetIdentityService
{
    private readonly SecurityOpsConfig _config;
    private readonly IAuditService _audit;
    private readonly ILogger<FleetIdentityService> _logger;
    private readonly string _identityFilePath;
    private readonly object _lock = new();
    private volatile AgentIdentity? _currentIdentity;

    public FleetIdentityService(
        SecurityOpsConfig config,
        IAuditService audit,
        ILogger<FleetIdentityService> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Initialize identity storage path
        var dataRoot = Hercules.BuiltIn.ResolvePathUnderDataRoot(
            Hercules.BuiltIn.ResolveDataRoot(),
            _config.IdentityStoragePath);
        Directory.CreateDirectory(dataRoot);
        _identityFilePath = Path.Combine(dataRoot, Hercules.BuiltIn.FleetIdentityFileName);
    }

    public Task<FleetIdentity> GetCurrentIdentityAsync(CancellationToken ct = default)
    {
        var identity = GetOrCreateIdentity();
        return Task.FromResult(new FleetIdentity(
            identity.AgentId,
            identity.CurrentCredentialId,
            identity.IssuedAt,
            identity.ExpiresAt,
            identity.PublicKeyFingerprint,
            identity.Roles));
    }

    public async Task<FleetIdentity> RotateIdentityAsync(RotationReason reason, CancellationToken ct = default)
    {
        AgentIdentity newIdentity;
        lock (_lock)
        {
            var current = GetOrCreateIdentity();

            // Revoke current credential
            current.IsActive = false;
            current.RevokedAt = DateTime.UtcNow;
            current.RevokedReason = reason.ToString();

            // Generate new identity
            newIdentity = GenerateNewIdentity(current.AgentId, current.Roles);
            newIdentity.PreviousCredentialId = current.CurrentCredentialId;
            newIdentity.RotationHistory.Add(new RotationRecord
            {
                CredentialId = current.CurrentCredentialId,
                RotatedAt = DateTime.UtcNow,
                Reason = reason.ToString()
            });

            _currentIdentity = newIdentity;
            SaveIdentityLocked(newIdentity);
        }

        await _audit.LogAsync(
            actor: "system",
            action: "identity_rotated",
            target: newIdentity.AgentId,
            details: $"Reason: {reason}, new credential: {newIdentity.CurrentCredentialId}",
            ct: ct);

        _logger.LogInformation(
            "Identity rotated for agent {AgentId}. Reason: {Reason}. New credential: {CredentialId}",
            newIdentity.AgentId, reason, newIdentity.CurrentCredentialId);

        return new FleetIdentity(
            newIdentity.AgentId,
            newIdentity.CurrentCredentialId,
            newIdentity.IssuedAt,
            newIdentity.ExpiresAt,
            newIdentity.PublicKeyFingerprint,
            newIdentity.Roles);
    }

    public async Task RevokeCredentialAsync(string credentialId, string reason, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var identity = GetOrCreateIdentity();

            if (identity.CurrentCredentialId == credentialId)
            {
                throw new InvalidOperationException("Cannot revoke current credential. Rotate first.");
            }

            if (identity.RotationHistory.Any(r => r.CredentialId == credentialId))
            {
                var record = identity.RotationHistory.First(r => r.CredentialId == credentialId);
                record.Revoked = true;
                record.RevokedReason = reason;
                SaveIdentityLocked(identity);
            }
        }

        await _audit.LogAsync(
            actor: "system",
            action: "credential_revoked",
            target: credentialId,
            details: reason,
            ct: ct);

        _logger.LogInformation("Credential {CredentialId} revoked. Reason: {Reason}", credentialId, reason);
    }

    public Task<IReadOnlyList<CredentialInfo>> GetActiveCredentialsAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            var identity = GetOrCreateIdentity();
            var credentials = new List<CredentialInfo>
            {
                new CredentialInfo(
                    identity.CurrentCredentialId,
                    "primary",
                    identity.IssuedAt,
                    identity.ExpiresAt,
                    identity.IsActive,
                    null)
            };

            foreach (var record in identity.RotationHistory)
            {
                credentials.Add(new CredentialInfo(
                    record.CredentialId,
                    "rotated",
                    record.RotatedAt,
                    record.RotatedAt.AddDays(_config.IdentityRotationDays),
                    !record.Revoked,
                    record.RevokedReason));
            }

            return Task.FromResult<IReadOnlyList<CredentialInfo>>(credentials);
        }
    }

    public Task<FleetIdentityExport> ExportIdentityAsync(CancellationToken ct = default)
    {
        var identity = GetOrCreateIdentity();
        var export = new FleetIdentityExport(
            identity.AgentId,
            identity.CurrentCredentialId,
            identity.IssuedAt,
            identity.ExpiresAt,
            identity.PublicKeyFingerprint,
            identity.Roles,
            DateTime.UtcNow);

        return Task.FromResult(export);
    }

    private AgentIdentity GetOrCreateIdentity()
    {
        if (_currentIdentity != null)
        {
            return _currentIdentity;
        }

        lock (_lock)
        {
            if (_currentIdentity != null)
            {
                return _currentIdentity;
            }

            if (File.Exists(_identityFilePath))
            {
                var json = File.ReadAllText(_identityFilePath);
                var identity = JsonSerializer.Deserialize<AgentIdentity>(json);
                if (identity != null)
                {
                    _currentIdentity = identity;
                    return identity;
                }
            }

            // Generate new identity
            var newId = GenerateNewIdentity(_config.DefaultAgentId, _config.DefaultRoles);
            _currentIdentity = newId;
            SaveIdentityLocked(newId);
            return newId;
        }
    }

    private AgentIdentity GenerateNewIdentity(string agentId, IReadOnlyList<string> roles)
    {
        // Generate RSA key pair for signing
        using var rsa = RSA.Create(2048);
        var publicKeyBytes = rsa.ExportSubjectPublicKeyInfo();
        var fingerprint = ComputeFingerprint(publicKeyBytes);

        var credentialId = $"cred_{Guid.NewGuid():N}";
        var issuedAt = DateTime.UtcNow;

        return new AgentIdentity
        {
            AgentId = agentId,
            CurrentCredentialId = credentialId,
            IssuedAt = issuedAt,
            ExpiresAt = issuedAt.AddDays(_config.IdentityRotationDays),
            PublicKeyFingerprint = fingerprint,
            Roles = new List<string>(roles),
            PrivateKeyPem = rsa.ExportRSAPrivateKeyPem(),
            IsActive = true
        };
    }

    private static string ComputeFingerprint(byte[] publicKeyBytes)
    {
        var hash = SHA256.HashData(publicKeyBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void SaveIdentityLocked(AgentIdentity identity)
    {
        var json = JsonSerializer.Serialize(identity, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_identityFilePath, json);
    }

    private sealed class AgentIdentity
    {
        public string AgentId { get; set; } = "";
        public string CurrentCredentialId { get; set; } = "";
        public string? PreviousCredentialId { get; set; }
        public DateTime IssuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public string PublicKeyFingerprint { get; set; } = "";
        public List<string> Roles { get; set; } = new();
        public string PrivateKeyPem { get; set; } = "";
        public bool IsActive { get; set; }
        public DateTime? RevokedAt { get; set; }
        public string? RevokedReason { get; set; }
        public List<RotationRecord> RotationHistory { get; set; } = new();
    }

    private sealed class RotationRecord
    {
        public string CredentialId { get; set; } = "";
        public DateTime RotatedAt { get; set; }
        public string Reason { get; set; } = "";
        public bool Revoked { get; set; }
        public string? RevokedReason { get; set; }
    }
}
