using Hercules.Backup;
using Xunit;

namespace Hercules.Agent.Tests.Backup;

public class EncryptionServiceTests
{
    private readonly EncryptionService _svc = new(Microsoft.Extensions.Logging.Abstractions.NullLogger<EncryptionService>.Instance);

    [Fact]
    public void DeriveKey_Produces_Key_And_Nonce_Of_Correct_Length()
    {
        var (key, nonce, salt) = _svc.DeriveKey("test-passphrase");
        Assert.Equal(32, key.Length);
        Assert.Equal(12, nonce.Length);
        Assert.Equal(16, salt.Length);
    }

    [Fact]
    public void DeriveKey_Produces_Different_Salt_Each_Call()
    {
        var (_, _, salt1) = _svc.DeriveKey("test-passphrase");
        var (_, _, salt2) = _svc.DeriveKey("test-passphrase");
        Assert.NotEqual(salt1, salt2);
    }

    [Fact]
    public void DeriveKeyWithSalt_Reproduces_Same_Key_From_Same_Salt()
    {
        var (_, _, salt) = _svc.DeriveKey("test-passphrase");
        _svc.DeriveKeyWithSalt("test-passphrase", salt, out var key1, out var nonce1);
        _svc.DeriveKeyWithSalt("test-passphrase", salt, out var key2, out var nonce2);
        Assert.Equal(key1, key2);
        Assert.Equal(nonce1, nonce2);
    }

    [Fact]
    public void DeriveKeyWithSalt_Produces_Different_Key_For_Different_Passphrase()
    {
        var (_, _, salt) = _svc.DeriveKey("passphrase-1");
        _svc.DeriveKeyWithSalt("passphrase-1", salt, out var key1, out _);
        _svc.DeriveKeyWithSalt("passphrase-2", salt, out var key2, out _);
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void Encrypt_Decrypt_Roundtrip_Preserves_Data()
    {
        var plaintext = "Hello, backup world!"u8.ToArray();
        var (key, nonce, _) = _svc.DeriveKey("roundtrip-test");
        var encrypted = _svc.Encrypt(plaintext, key, nonce);
        var decrypted = _svc.Decrypt(encrypted, key);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_With_Same_Key_Same_Nonce_Is_Deterministic()
    {
        // GCM with a fixed nonce is deterministic (ciphertext is same on repeated encryption)
        var plaintext = "Same message"u8.ToArray();
        var (key, nonce, _) = _svc.DeriveKey("deterministic-test");
        var enc1 = _svc.Encrypt(plaintext, key, nonce);
        var enc2 = _svc.Encrypt(plaintext, key, nonce);
        Assert.Equal(enc1, enc2);
    }

    [Fact]
    public void Encrypt_With_Different_Nonce_Produces_Different_Ciphertext()
    {
        var plaintext = "Same message"u8.ToArray();
        var (key, nonce1, _) = _svc.DeriveKey("nonce-different-test");
        var (_, nonce2, _) = _svc.DeriveKey("nonce-different-test"); // different salt → different nonce
        var enc1 = _svc.Encrypt(plaintext, key, nonce1);
        var enc2 = _svc.Encrypt(plaintext, key, nonce2);
        Assert.NotEqual(enc1, enc2); // different nonce → different ciphertext
    }

    [Fact]
    public void Decrypt_With_Wrong_Key_Throws()
    {
        var plaintext = "Secret data"u8.ToArray();
        var (key, nonce, _) = _svc.DeriveKey("correct-pass");
        var (wrongKey, _, _) = _svc.DeriveKey("wrong-pass");
        var encrypted = _svc.Encrypt(plaintext, key, nonce);
        var ex = Record.Exception(() => _svc.Decrypt(encrypted, wrongKey));
        Assert.NotNull(ex);
        // Both wrong-key and tampered ciphertext produce AuthenticationTagMismatchException (subclass of CryptographicException)
        Assert.IsType<System.Security.Cryptography.AuthenticationTagMismatchException>(ex);
    }

    [Fact]
    public void Decrypt_With_Tampered_Ciphertext_Throws()
    {
        var plaintext = "Original"u8.ToArray();
        var (key, nonce, _) = _svc.DeriveKey("tamper-test");
        var encrypted = _svc.Encrypt(plaintext, key, nonce);
        encrypted[^1] ^= 0xFF; // tamper last byte
        var ex = Record.Exception(() => _svc.Decrypt(encrypted, key));
        Assert.NotNull(ex);
        Assert.IsType<System.Security.Cryptography.AuthenticationTagMismatchException>(ex);
    }

    [Fact]
    public void Decrypt_With_Too_Short_Data_Throws()
    {
        var (key, _, _) = _svc.DeriveKey("short-test");
        Assert.Throws<System.Security.Cryptography.CryptographicException>(
            () => _svc.Decrypt(new byte[5], key));
    }

    [Fact]
    public void ComputeKeyFingerprint_Returns_16_Hex_Chars()
    {
        var (key, _, _) = _svc.DeriveKey("fingerprint-test");
        var fp = EncryptionService.ComputeKeyFingerprint(key);
        Assert.Equal(16, fp.Length);
        Assert.True(fp.All(c => "0123456789abcdef".Contains(c)));
    }

    [Fact]
    public void ComputeKeyFingerprint_Same_For_Same_Key()
    {
        var (key, _, _) = _svc.DeriveKey("fp-same");
        var fp1 = EncryptionService.ComputeKeyFingerprint(key);
        var fp2 = EncryptionService.ComputeKeyFingerprint(key);
        Assert.Equal(fp1, fp2);
    }

    [Fact]
    public void ComputeHash_Returns_64_Hex_Chars()
    {
        var hash = EncryptionService.ComputeHash("test data"u8.ToArray());
        Assert.Equal(64, hash.Length);
        Assert.True(hash.All(c => "0123456789abcdef".Contains(c)));
    }

    [Fact]
    public void FullBackupIntegration_Creates_Valid_Encrypted_Blob()
    {
        // Simulate the backup blob creation and decryption
        var testData = "This is test backup data content."u8.ToArray();
        var passphrase = "integration-test-passphrase";

        // Derive key
        var (key, nonce, salt) = _svc.DeriveKey(passphrase);

        // Encrypt
        var encrypted = _svc.Encrypt(testData, key, nonce);

        // Simulate file storage: salt + encrypted
        var fileBytes = new byte[salt.Length + encrypted.Length];
        salt.CopyTo(fileBytes, 0);
        encrypted.CopyTo(fileBytes, salt.Length);

        // Decrypt
        var fileSalt = fileBytes.AsSpan(0, 16).ToArray();
        var filePayload = fileBytes.AsSpan(16).ToArray();
        _svc.DeriveKeyWithSalt(passphrase, fileSalt, out var derivedKey, out var derivedNonce);
        var decrypted = _svc.Decrypt(filePayload, derivedKey);

        Assert.Equal(testData, decrypted);
    }
}
