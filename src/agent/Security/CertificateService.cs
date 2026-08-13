using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Hercules.Audit;
using Hercules.Config;
using Microsoft.Extensions.Logging;

namespace Hercules.Security;

/// <summary>
///     Certificate management service implementation (task_055).
///     Handles certificate generation, renewal, and validation.
/// </summary>
public sealed class CertificateService : ICertificateService
{
    private readonly SecurityOpsConfig _config;
    private readonly IAuditService _audit;
    private readonly ILogger<CertificateService> _logger;
    private readonly string _certStorePath;
    private readonly object _lock = new();
    private volatile CertificateInfo? _currentCert;

    public CertificateService(
        SecurityOpsConfig config,
        IAuditService audit,
        ILogger<CertificateService> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var dataRoot = Path.Combine(AppContext.BaseDirectory, _config.CertificateStoragePath);
        Directory.CreateDirectory(dataRoot);
        _certStorePath = Path.Combine(dataRoot, "certificates.json");
    }

    public Task<CertificateInfo?> GetCurrentCertificateAsync(CancellationToken ct = default)
    {
        var cert = GetOrCreateCertificate();
        return Task.FromResult<CertificateInfo?>(cert);
    }

    public Task<RenewalCheckResult> CheckRenewalNeededAsync(CancellationToken ct = default)
    {
        var cert = GetOrCreateCertificate();
        var now = DateTime.UtcNow;
        var daysUntilExpiry = (int)(cert.ExpiresAt - now).TotalDays;
        var isExpired = cert.ExpiresAt < now;

        bool needsRenewal;
        string? recommendedAction;

        if (isExpired)
        {
            needsRenewal = true;
            recommendedAction = "Certificate has expired. Immediate renewal required.";
        }
        else if (daysUntilExpiry <= _config.CertificateRenewalThresholdDays)
        {
            needsRenewal = true;
            recommendedAction = $"Certificate expires in {daysUntilExpiry} days. Renewal recommended.";
        }
        else
        {
            needsRenewal = false;
            recommendedAction = null;
        }

        return Task.FromResult(new RenewalCheckResult(
            needsRenewal,
            isExpired,
            daysUntilExpiry,
            recommendedAction));
    }

    public async Task<CertificateInfo> RenewCertificateAsync(string reason, CancellationToken ct = default)
    {
        CertificateInfo newCert;
        lock (_lock)
        {
            var current = GetOrCreateCertificate();

            // Generate new self-signed certificate
            newCert = GenerateSelfSignedCertificate();

            // Save to store
            var store = LoadCertificateStore();
            store.Certificates.Add(new StoredCertificate
            {
                CertificateId = newCert.CertificateId,
                Subject = newCert.Subject,
                IssuedAt = newCert.IssuedAt,
                ExpiresAt = newCert.ExpiresAt,
                Thumbprint = newCert.Thumbprint,
                IsActive = true,
                RenewedFrom = current.CertificateId,
                RenewalReason = reason
            });

            // Mark previous as inactive
            var prev = store.Certificates.FirstOrDefault(c => c.CertificateId == current.CertificateId);
            if (prev != null)
            {
                prev.IsActive = false;
                prev.ReplacedAt = DateTime.UtcNow;
                prev.ReplacementReason = reason;
            }

            SaveCertificateStore(store);
            _currentCert = newCert;
        }

        await _audit.LogAsync(
            actor: "system",
            action: "certificate_renewed",
            target: newCert.CertificateId,
            details: $"Reason: {reason}, previous: {newCert.CertificateId}",
            ct: ct);

        _logger.LogInformation(
            "Certificate renewed. New thumbprint: {Thumbprint}. Reason: {Reason}",
            newCert.Thumbprint, reason);

        return newCert;
    }

    public Task<ValidationResult> ValidateCertificateAsync(byte[] certificateData, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        try
        {
            using var cert = new X509Certificate2(certificateData);

            // Check expiration
            if (cert.NotAfter < DateTime.UtcNow)
            {
                errors.Add("Certificate has expired.");
            }

            // Check validity period
            if (cert.NotBefore > DateTime.UtcNow)
            {
                errors.Add("Certificate is not yet valid.");
            }

            // Check key size
            if (cert.PublicKey.GetRSAPublicKey() is { } rsa && rsa.KeySize < 2048)
            {
                warnings.Add($"RSA key size ({rsa.KeySize}) is below recommended 2048 bits.");
            }

            // Check signature algorithm
            if (cert.SignatureAlgorithm.Value?.Contains("MD5", StringComparison.OrdinalIgnoreCase) == true)
            {
                errors.Add("MD5 signature algorithm is not secure.");
            }

            return Task.FromResult(new ValidationResult(
                errors.Count == 0,
                errors,
                warnings));
        }
        catch (CryptographicException ex)
        {
            return Task.FromResult(new ValidationResult(
                false,
                new List<string> { $"Certificate parsing failed: {ex.Message}" },
                warnings));
        }
    }

    public Task<string> ExportCertificateAsync(string certificateId, CancellationToken ct = default)
    {
        var store = LoadCertificateStore();
        var cert = store.Certificates.FirstOrDefault(c => c.CertificateId == certificateId);

        if (cert == null)
        {
            throw new InvalidOperationException($"Certificate {certificateId} not found.");
        }

        // Return certificate metadata (not the actual cert for security)
        var export = new
        {
            cert.CertificateId,
            cert.Subject,
            cert.IssuedAt,
            cert.ExpiresAt,
            cert.Thumbprint,
            ExportedAt = DateTime.UtcNow
        };

        return Task.FromResult(JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true }));
    }

    private CertificateInfo GetOrCreateCertificate()
    {
        if (_currentCert != null)
        {
            return _currentCert;
        }

        lock (_lock)
        {
            if (_currentCert != null)
            {
                return _currentCert;
            }

            var store = LoadCertificateStore();
            var active = store.Certificates.FirstOrDefault(c => c.IsActive);

            if (active != null)
            {
                _currentCert = new CertificateInfo(
                    active.CertificateId,
                    active.Subject,
                    active.Issuer ?? active.Subject,
                    active.IssuedAt,
                    active.ExpiresAt,
                    active.Thumbprint,
                    "RSA",
                    2048,
                    active.Issuer == null || active.Issuer == active.Subject);
                return _currentCert;
            }

            // Generate new certificate
            var newCert = GenerateSelfSignedCertificate();
            store.Certificates.Add(new StoredCertificate
            {
                CertificateId = newCert.CertificateId,
                Subject = newCert.Subject,
                Issuer = newCert.Issuer,
                IssuedAt = newCert.IssuedAt,
                ExpiresAt = newCert.ExpiresAt,
                Thumbprint = newCert.Thumbprint,
                IsActive = true
            });

            SaveCertificateStore(store);
            _currentCert = newCert;
            return newCert;
        }
    }

    private CertificateInfo GenerateSelfSignedCertificate()
    {
        var distinguishedName = new X500DistinguishedName($"CN={_config.DefaultAgentId}, O=Hercules Fleet, C=US");
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(distinguishedName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // Add key usage
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));

        var notBefore = DateTime.UtcNow;
        var notAfter = notBefore.AddDays(_config.CertificateValidityDays);

        using var cert = request.CreateSelfSigned(notBefore, notAfter);
        var thumbprint = cert.Thumbprint;

        return new CertificateInfo(
            $"cert_{Guid.NewGuid():N}",
            cert.Subject,
            cert.Issuer,
            notBefore,
            notAfter,
            thumbprint,
            "RSA",
            2048,
            true);
    }

    private CertificateStore LoadCertificateStore()
    {
        if (!File.Exists(_certStorePath))
        {
            return new CertificateStore();
        }

        var json = File.ReadAllText(_certStorePath);
        return JsonSerializer.Deserialize<CertificateStore>(json) ?? new CertificateStore();
    }

    private void SaveCertificateStore(CertificateStore store)
    {
        var json = JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_certStorePath, json);
    }

    private sealed class CertificateStore
    {
        public List<StoredCertificate> Certificates { get; set; } = new();
    }

    private sealed class StoredCertificate
    {
        public string CertificateId { get; set; } = "";
        public string Subject { get; set; } = "";
        public string? Issuer { get; set; }
        public DateTime IssuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public string Thumbprint { get; set; } = "";
        public bool IsActive { get; set; }
        public string? RenewedFrom { get; set; }
        public string? RenewalReason { get; set; }
        public DateTime? ReplacedAt { get; set; }
        public string? ReplacementReason { get; set; }
    }
}
