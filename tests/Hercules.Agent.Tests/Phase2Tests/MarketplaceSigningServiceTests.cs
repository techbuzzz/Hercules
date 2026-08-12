using Hercules.Config;
using Hercules.Skills.Marketplace;
using Xunit;

namespace Hercules.Agent.Tests.Phase2Tests;

public class MarketplaceSigningServiceTests
{
    // Тестовый ключ (base64-encoded 32 байта HMAC ключа)
    private static readonly byte[] TestKey = Convert.FromBase64String("YWJjZGVmZ2hpamtsbW5vcHFyc3R1dnd4eXoxMjM0NTY=");

    private MarketplaceSigningService CreateWithKey()
    {
        var cfg = new MarketplaceConfig { SigningKey = Convert.ToBase64String(TestKey) };
        return new MarketplaceSigningService(cfg);
    }

    private MarketplaceSigningService CreateWithoutKey()
    {
        return new MarketplaceSigningService(new MarketplaceConfig());
    }

    [Fact]
    public void ComputeHash_SameBytes_ProducesSameHash()
    {
        var svc = CreateWithoutKey();
        var bytes = new byte[] { 0x01, 0x02, 0x03 };

        var hash1 = svc.ComputeHash(bytes);
        var hash2 = svc.ComputeHash(bytes);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_DifferentBytes_ProducesDifferentHash()
    {
        var svc = CreateWithoutKey();
        var hash1 = svc.ComputeHash(new byte[] { 0x01 });
        var hash2 = svc.ComputeHash(new byte[] { 0x02 });

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_EmptyBytes_ProducesHash()
    {
        var svc = CreateWithoutKey();
        var hash = svc.ComputeHash(Array.Empty<byte>());

        Assert.NotEmpty(hash);
        Assert.Equal(64, hash.Length); // SHA256 hex = 64 chars
    }

    [Fact]
    public void VerifyHash_ValidHash_ReturnsTrue()
    {
        var svc = CreateWithoutKey();
        var bytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var hash = svc.ComputeHash(bytes);

        Assert.True(svc.VerifyHash(bytes, hash));
    }

    [Fact]
    public void VerifyHash_MismatchedHash_ReturnsFalse()
    {
        var svc = CreateWithoutKey();
        var bytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var wrongHash = "0000000000000000000000000000000000000000000000000000000000000000";

        Assert.False(svc.VerifyHash(bytes, wrongHash));
    }

    [Fact]
    public void VerifyHash_EmptyExpectedHash_ReturnsFalse()
    {
        var svc = CreateWithoutKey();
        var bytes = new byte[] { 0x01 };

        Assert.False(svc.VerifyHash(bytes, ""));
        Assert.False(svc.VerifyHash(bytes, null!));
    }

    [Fact]
    public void ComputeSignature_WithKey_ProducesBase64String()
    {
        var svc = CreateWithKey();
        var hash = svc.ComputeHash(new byte[] { 0x01 });

        var sig = svc.ComputeSignature(hash);

        Assert.NotEmpty(sig);
        // Base64-encoded HMAC-SHA256 = 44 chars for 32-byte key
        Assert.Equal(44, sig.Length);
    }

    [Fact]
    public void ComputeSignature_WithoutKey_Throws()
    {
        var svc = CreateWithoutKey();
        var hash = svc.ComputeHash(new byte[] { 0x01 });

        Assert.Throws<InvalidOperationException>(() => svc.ComputeSignature(hash));
    }

    [Fact]
    public void VerifySignature_ValidSignature_ReturnsTrue()
    {
        var svc = CreateWithKey();
        var bytes = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
        var hash = svc.ComputeHash(bytes);
        var sig = svc.ComputeSignature(hash);

        Assert.True(svc.VerifySignature(hash, sig));
    }

    [Fact]
    public void VerifySignature_WrongSignature_ReturnsFalse()
    {
        var svc = CreateWithKey();
        var hash = svc.ComputeHash(new byte[] { 0xCA, 0xFE });
        var wrongSig = Convert.ToBase64String(new byte[32]);

        Assert.False(svc.VerifySignature(hash, wrongSig));
    }

    [Fact]
    public void VerifySignature_WithoutSigningService_ReturnsFalse()
    {
        var svc = CreateWithoutKey();
        var hash = svc.ComputeHash(new byte[] { 0x01 });

        Assert.False(svc.VerifySignature(hash, "somesig"));
    }

    [Fact]
    public void VerifySignature_EmptySignature_ReturnsFalse()
    {
        var svc = CreateWithKey();
        var hash = svc.ComputeHash(new byte[] { 0x01 });

        Assert.False(svc.VerifySignature(hash, ""));
        Assert.False(svc.VerifySignature(hash, null!));
    }

    [Fact]
    public void VerifyHash_CaseInsensitiveComparison_ReturnsTrue()
    {
        var svc = CreateWithoutKey();
        var bytes = new byte[] { 0xFF };
        var hash = svc.ComputeHash(bytes);
        var upperHash = hash.ToUpperInvariant();

        Assert.True(svc.VerifyHash(bytes, upperHash));
    }
}
