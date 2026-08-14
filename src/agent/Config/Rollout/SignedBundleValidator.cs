using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Hercules.Config.Rollout;

/// <summary>
///     HMAC-SHA256 валидатор подписей config/policy бандлов (task_058).
///     Реализация аналогична PackageSigningService: ключ хранится локально,
///     trusted signers проверяются по fingerprint.
/// </summary>
public sealed class SignedBundleValidator : ISignedBundleValidator
{
    private readonly ConfigRolloutConfig _config;
    private readonly ILogger<SignedBundleValidator> _logger;
    private readonly string _signingKeyPath;
    private readonly string _signersPath;
    private readonly byte[] _signingKey;
    private readonly object _lock = new();
    private List<TrustedSignerRecord> _trustedSigners = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public SignedBundleValidator(
        ConfigRolloutConfig config,
        ILogger<SignedBundleValidator> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var dataRoot = Path.Combine(AppContext.BaseDirectory, _config.BundlesPath);
        Directory.CreateDirectory(dataRoot);

        _signingKeyPath = Path.Combine(dataRoot, "rollout-signing-key.key");
        _signersPath = Path.Combine(dataRoot, "trusted-signers.json");

        // Generate or load HMAC key
        if (File.Exists(_signingKeyPath))
        {
            _signingKey = File.ReadAllBytes(_signingKeyPath);
        }
        else
        {
            _signingKey = RandomNumberGenerator.GetBytes(32);
            File.WriteAllBytes(_signingKeyPath, _signingKey);
            _logger.LogInformation("Generated new rollout signing key at {Path}", _signingKeyPath);
        }

        LoadTrustedSigners();
    }

    public Task<BundleValidationResult> ValidateAsync(ConfigBundle bundle, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrEmpty(bundle.Signature))
        {
            if (_config.RequireBundleSignature)
            {
                errors.Add("Bundle has no signature and signature is required.");
                return Task.FromResult(BundleValidationResult.Failure(errors));
            }

            warnings.Add("Bundle is not signed.");
            return Task.FromResult(BundleValidationResult.Success(null, null, warnings));
        }

        // Verify HMAC signature over payload
        try
        {
            var payloadHash = ComputePayloadHash(bundle.Payload);
            var expectedSig = ComputeHmac(payloadHash);
            var providedSig = Convert.FromBase64String(bundle.Signature);

            if (!CryptographicOperations.FixedTimeEquals(expectedSig, providedSig))
            {
                errors.Add("Bundle signature verification failed: payload hash mismatch.");
                return Task.FromResult(BundleValidationResult.Failure(errors));
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Signature verification error: {ex.Message}");
            return Task.FromResult(BundleValidationResult.Failure(errors));
        }

        // Check expiry
        if (bundle.ExpiresAt.HasValue && bundle.ExpiresAt.Value < DateTime.UtcNow)
        {
            errors.Add($"Bundle expired at {bundle.ExpiresAt:O}.");
            return Task.FromResult(BundleValidationResult.Failure(errors));
        }

        // Check version compatibility
        if (!string.IsNullOrEmpty(bundle.MinHerculesVersion) &&
            !Version.TryParse(bundle.MinHerculesVersion, out var minVer))
        {
            warnings.Add($"Invalid minHerculesVersion format: {bundle.MinHerculesVersion}.");
        }

        if (!string.IsNullOrEmpty(bundle.MaxHerculesVersion) &&
            !Version.TryParse(bundle.MaxHerculesVersion, out var maxVer))
        {
            warnings.Add($"Invalid maxHerculesVersion format: {bundle.MaxHerculesVersion}.");
        }

        // Check trusted signer
        bool isTrusted = false;
        if (!string.IsNullOrEmpty(bundle.SignerId) && !string.IsNullOrEmpty(bundle.PublicKeyFingerprint))
        {
            var signer = _trustedSigners.FirstOrDefault(s =>
                s.SignerId == bundle.SignerId &&
                s.IsActive &&
                s.Fingerprint.Equals(bundle.PublicKeyFingerprint, StringComparison.OrdinalIgnoreCase));

            isTrusted = signer != null;

            if (!isTrusted && _config.RequireTrustedSigner)
            {
                warnings.Add($"Signer '{bundle.SignerId}' is not in the trusted signers list.");
            }
        }

        _logger.LogDebug(
            "Bundle {BundleId} v{Version} validated: valid={IsValid}, trusted={IsTrusted}, signer={SignerId}",
            bundle.Id, bundle.Version, true, isTrusted, bundle.SignerId);

        return Task.FromResult(new BundleValidationResult(
            true, isTrusted, bundle.SignerId, bundle.SignedAt, errors, warnings));
    }

    public Task<ConfigBundle> SignBundleAsync(ConfigBundle bundle, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var payloadHash = ComputePayloadHash(bundle.Payload);
        var signature = ComputeHmac(payloadHash);

        lock (_lock)
        {
            bundle.Signature = Convert.ToBase64String(signature);
            bundle.SignerId = _config.DefaultSignerId;
            bundle.SignedAt = DateTime.UtcNow;
            bundle.Algorithm = "HMAC-SHA256";
            bundle.PublicKeyFingerprint = ComputeFingerprint(_signingKey);
        }

        _logger.LogInformation(
            "Signed bundle {BundleId} v{Version} as {SignerId}",
            bundle.Id, bundle.Version, bundle.SignerId);

        return Task.FromResult(bundle);
    }

    private byte[] ComputePayloadHash(string payload)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(payload);
        return SHA256.HashData(bytes);
    }

    private byte[] ComputeHmac(byte[] data)
    {
        return HMACSHA256.HashData(_signingKey, data);
    }

    private static string ComputeFingerprint(byte[] key)
    {
        var hash = SHA256.HashData(key);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void LoadTrustedSigners()
    {
        if (File.Exists(_signersPath))
        {
            try
            {
                var json = File.ReadAllText(_signersPath);
                _trustedSigners = JsonSerializer.Deserialize<List<TrustedSignerRecord>>(json) ?? new();
            }
            catch
            {
                _trustedSigners = new();
            }
        }
    }

    public void AddTrustedSigner(TrustedSignerRecord signer)
    {
        lock (_lock)
        {
            if (_trustedSigners.Any(s => s.SignerId == signer.SignerId))
                throw new InvalidOperationException($"Signer '{signer.SignerId}' is already trusted.");

            _trustedSigners.Add(signer);
            File.WriteAllText(_signersPath, JsonSerializer.Serialize(_trustedSigners, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    public sealed class TrustedSignerRecord
    {
        public string SignerId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Fingerprint { get; set; } = "";
        public bool IsActive { get; set; } = true;
    }
}
