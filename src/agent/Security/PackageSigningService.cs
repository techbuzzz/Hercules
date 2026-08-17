using System.Security.Cryptography;
using System.Text.Json;
using Hercules.Audit;
using Hercules.Config;
using Microsoft.Extensions.Logging;

namespace Hercules.Security;

/// <summary>
///     Package signing and verification service implementation (task_055).
///     Verifies signed packages using HMAC-SHA256 signatures.
/// </summary>
public sealed class PackageSigningService : IPackageSigningService
{
    private readonly SecurityOpsConfig _config;
    private readonly IAuditService _audit;
    private readonly ILogger<PackageSigningService> _logger;
    private readonly string _signersFilePath;
    private readonly byte[] _signingKey;
    private readonly object _lock = new();
    private List<TrustedSigner> _trustedSigners = new();

    public PackageSigningService(
        SecurityOpsConfig config,
        IAuditService audit,
        ILogger<PackageSigningService> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var dataRoot = Hercules.BuiltIn.ResolvePathUnderDataRoot(
            Hercules.BuiltIn.ResolveDataRoot(),
            _config.PackageSigningPath);
        Directory.CreateDirectory(dataRoot);
        _signersFilePath = Path.Combine(dataRoot, Hercules.BuiltIn.TrustedSignersFileName);

        // Generate or load signing key
        var keyPath = Path.Combine(dataRoot, Hercules.BuiltIn.SigningKeyFileName);
        if (File.Exists(keyPath))
        {
            _signingKey = File.ReadAllBytes(keyPath);
        }
        else
        {
            _signingKey = RandomNumberGenerator.GetBytes(32);
            File.WriteAllBytes(keyPath, _signingKey);
        }

        LoadTrustedSigners();
    }

    public Task<SignatureVerificationResult> VerifyPackageAsync(
        string packagePath,
        CancellationToken ct = default)
    {
        using var stream = File.OpenRead(packagePath);
        return VerifyPackageAsync(stream, Path.GetFileName(packagePath), ct);
    }

    public Task<SignatureVerificationResult> VerifyPackageAsync(
        Stream packageStream,
        string packageName,
        CancellationToken ct = default)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        try
        {
            // Read signature file alongside the package
            var signaturePath = packageStream is FileStream fs
                ? Path.ChangeExtension(fs.Name, ".sig")
                : null;

            if (signaturePath == null || !File.Exists(signaturePath))
            {
                if (_config.RequirePackageSignature)
                {
                    errors.Add("Signature file not found and signatures are required.");
                    return Task.FromResult(new SignatureVerificationResult(
                        false, false, null, null, errors, warnings));
                }

                warnings.Add("Package is not signed.");
                return Task.FromResult(new SignatureVerificationResult(
                    true, true, null, null, errors, warnings));
            }

            // Read signature
            var signatureBytes = File.ReadAllBytes(signaturePath);
            var signatureInfo = ParseSignature(signatureBytes);

            // Read package content for verification
            packageStream.Position = 0;
            using var ms = new MemoryStream();
            packageStream.CopyTo(ms);
            var contentBytes = ms.ToArray();
            var contentHash = SHA256.HashData(contentBytes);

            // Verify signature using HMAC-SHA256
            bool isValid;
            try
            {
                var expectedSignature = ComputeHmacSignature(contentHash);
                isValid = CryptographicOperations.FixedTimeEquals(
                    Convert.FromBase64String(signatureInfo.Signature),
                    expectedSignature);
            }
            catch
            {
                isValid = false;
                errors.Add("Signature verification failed.");
            }

            if (!isValid)
            {
                return Task.FromResult(new SignatureVerificationResult(
                    false, false, signatureInfo.SignerId, signatureInfo.SignedAt, errors, warnings));
            }

            // Check if signer is trusted
            var signer = _trustedSigners.FirstOrDefault(s =>
                s.SignerId == signatureInfo.SignerId && s.IsActive);

            bool isTrusted = signer != null;
            if (!isTrusted && _config.RequireTrustedSigner)
            {
                warnings.Add($"Signer '{signatureInfo.SignerId}' is not in the trusted signers list.");
            }

            return Task.FromResult(new SignatureVerificationResult(
                true, isTrusted, signatureInfo.SignerId, signatureInfo.SignedAt, errors, warnings));
        }
        catch (Exception ex)
        {
            errors.Add($"Package verification error: {ex.Message}");
            return Task.FromResult(new SignatureVerificationResult(
                false, false, null, null, errors, warnings));
        }
    }

    public async Task<string> SignPackageAsync(
        string packagePath,
        string outputPath,
        CancellationToken ct = default)
    {
        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException("Package not found.", packagePath);
        }

        var contentBytes = await File.ReadAllBytesAsync(packagePath, ct);
        var contentHash = SHA256.HashData(contentBytes);

        // Sign using HMAC-SHA256
        var signature = ComputeHmacSignature(contentHash);

        var signerId = _config.DefaultAgentId;
        var signatureInfo = new SignatureData
        {
            SignerId = signerId,
            SignedAt = DateTime.UtcNow,
            Signature = Convert.ToBase64String(signature),
            PublicKeyFingerprint = ComputeFingerprint(_signingKey),
            Algorithm = "HMAC-SHA256"
        };

        var signaturePath = Path.ChangeExtension(outputPath, ".sig");
        var signatureBytes = SerializeSignature(signatureInfo);
        await File.WriteAllBytesAsync(signaturePath, signatureBytes, ct);

        await _audit.LogAsync(
            actor: "system",
            action: "package_signed",
            target: Path.GetFileName(packagePath),
            details: $"Signer: {signerId}",
            ct: ct);

        _logger.LogInformation("Package signed: {Package} -> {Signature}", packagePath, signaturePath);

        return signaturePath;
    }

    public Task<IReadOnlyList<TrustedSigner>> GetTrustedSignersAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<TrustedSigner>>(_trustedSigners.AsReadOnly());
    }

    public async Task AddTrustedSignerAsync(TrustedSigner signer, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_trustedSigners.Any(s => s.SignerId == signer.SignerId))
            {
                throw new InvalidOperationException($"Signer '{signer.SignerId}' is already trusted.");
            }

            _trustedSigners.Add(signer);
            SaveTrustedSigners();
        }

        await _audit.LogAsync(
            actor: "system",
            action: "trusted_signer_added",
            target: signer.SignerId,
            details: $"Name: {signer.Name}",
            ct: ct);

        _logger.LogInformation("Trusted signer added: {SignerId}", signer.SignerId);
    }

    private void LoadTrustedSigners()
    {
        if (File.Exists(_signersFilePath))
        {
            var json = File.ReadAllText(_signersFilePath);
            _trustedSigners = JsonSerializer.Deserialize<List<TrustedSigner>>(json) ?? new();
        }
    }

    private void SaveTrustedSigners()
    {
        var json = JsonSerializer.Serialize(_trustedSigners, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_signersFilePath, json);
    }

    private byte[] ComputeHmacSignature(byte[] data)
    {
        return HMACSHA256.HashData(_signingKey, data);
    }

    private static string ComputeFingerprint(byte[] key)
    {
        var hash = SHA256.HashData(key);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static SignatureData ParseSignature(byte[] signatureBytes)
    {
        // Parse JSON signature file
        var json = System.Text.Encoding.UTF8.GetString(signatureBytes);
        return JsonSerializer.Deserialize<SignatureData>(json) ?? throw new InvalidOperationException("Invalid signature format");
    }

    private static byte[] SerializeSignature(SignatureData data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        return System.Text.Encoding.UTF8.GetBytes(json);
    }

    private sealed class SignatureData
    {
        public string SignerId { get; set; } = "";
        public DateTime SignedAt { get; set; }
        public string Signature { get; set; } = "";
        public string PublicKeyFingerprint { get; set; } = "";
        public string Algorithm { get; set; } = "";
    }
}
