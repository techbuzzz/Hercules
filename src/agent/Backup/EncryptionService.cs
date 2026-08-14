using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Hercules.Backup;

/// <summary>
///     Provides AES-256-GCM encryption and decryption for backup archives (task_063).
///     Key derivation uses HKDF-SHA256 from a user-supplied passphrase.
/// </summary>
public sealed class EncryptionService
{
    private const int KeySizeBytes = 32; // AES-256
    private const int NonceSizeBytes = 12; // GCM recommended
    private const int TagSizeBytes = 16; // GCM auth tag
    private const int SaltSizeBytes = 16;
    private const int Iterations = 100_000; // PBKDF2 iterations

    private readonly ILogger<EncryptionService> _logger;

    public EncryptionService(ILogger<EncryptionService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Derives a 32-byte AES key and 12-byte nonce from a passphrase using HKDF-SHA256
    ///     with a random salt. Returns (key, nonce, salt).
    /// </summary>
    public (byte[] Key, byte[] Nonce, byte[] Salt) DeriveKey(string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        DeriveKeyParts(passphrase, salt, out var key, out var nonce);
        return (key, nonce, salt);
    }

    /// <summary>
    ///     Derives key/nonce from passphrase + existing salt (for decryption).
    /// </summary>
    public void DeriveKeyWithSalt(string passphrase, byte[] salt, out byte[] key, out byte[] nonce)
    {
        DeriveKeyParts(passphrase, salt, out key, out nonce);
    }

    private void DeriveKeyParts(string passphrase, byte[] salt, out byte[] key, out byte[] nonce)
    {
        key = Rfc2898DeriveBytes.Pbkdf2(
            passphrase,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySizeBytes);

        // Derive nonce separately using a different salt (passphrase + "nonce" suffix)
        var nonceSalt = salt.Concat("nonce"u8.ToArray()).ToArray();
        nonce = Rfc2898DeriveBytes.Pbkdf2(
            passphrase,
            nonceSalt,
            Iterations,
            HashAlgorithmName.SHA256,
            NonceSizeBytes);
    }

    /// <summary>
    ///     Encrypts data using AES-256-GCM.
    ///     Output format: [salt(16)][nonce(12)][ciphertext][tag(16)].
    /// </summary>
    public byte[] Encrypt(byte[] plaintext, byte[] key, byte[] nonce)
    {
        using var aes = new AesGcm(key, TagSizeBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSizeBytes];
        var plaintextSpan = plaintext.AsSpan();
        var ciphertextSpan = ciphertext.AsSpan();
        var tagSpan = tag.AsSpan();

        aes.Encrypt(nonce, plaintextSpan, ciphertextSpan, tagSpan);

        // Prepend nonce to ciphertext (salt is stored separately by caller)
        var result = new byte[nonce.Length + ciphertext.Length + tag.Length];
        nonce.CopyTo(result, 0);
        ciphertext.CopyTo(result, nonce.Length);
        tag.CopyTo(result, nonce.Length + ciphertext.Length);
        return result;
    }

    /// <summary>
    ///     Decrypts data produced by Encrypt.
    ///     Input format: [nonce(12)][ciphertext][tag(16)].
    /// </summary>
    public byte[] Decrypt(byte[] encryptedData, byte[] key)
    {
        if (encryptedData.Length < NonceSizeBytes + TagSizeBytes)
            throw new CryptographicException("Encrypted data is too short.");

        var nonce = encryptedData.AsSpan(0, NonceSizeBytes).ToArray();
        var ciphertextWithTag = encryptedData.AsSpan(NonceSizeBytes).ToArray();
        var ciphertextLength = ciphertextWithTag.Length - TagSizeBytes;

        using var aes = new AesGcm(key, TagSizeBytes);
        var plaintext = new byte[ciphertextLength];
        var ciphertextSpan = ciphertextWithTag.AsSpan(0, ciphertextLength);
        var tagSpan = ciphertextWithTag.AsSpan(ciphertextLength);
        var plaintextSpan = plaintext.AsSpan();

        aes.Decrypt(nonce, ciphertextSpan, tagSpan, plaintextSpan);
        return plaintext;
    }

    /// <summary>
    ///     Computes SHA-256 fingerprint of the key (first 16 hex chars).
    /// </summary>
    public static string ComputeKeyFingerprint(byte[] key)
    {
        var hash = SHA256.HashData(key);
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    /// <summary>
    ///     Computes SHA-256 hash of data.
    /// </summary>
    public static string ComputeHash(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    ///     Computes SHA-256 hash of a file stream (does NOT dispose the stream).
    /// </summary>
    public static async Task<string> ComputeFileHashAsync(Stream stream, CancellationToken ct = default)
    {
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
