using Hercules.Audit;
using Xunit;

namespace Hercules.Agent.Tests.Audit;

/// <summary>
///     Тесты PayloadHashService: ComputeHash, Verify.
/// </summary>
public class PayloadHashServiceTests
{
    private readonly PayloadHashService _svc = new();

    [Fact]
    public void ComputeHash_ReturnsConsistentHash()
    {
        var hash1 = _svc.ComputeHash("hello world");
        var hash2 = _svc.ComputeHash("hello world");
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_Returns16Chars()
    {
        var hash = _svc.ComputeHash("test payload");
        Assert.Equal(16, hash.Length);
        Assert.Matches("^[0-9A-Fa-f]{16}$", hash);
    }

    [Fact]
    public void ComputeHash_DifferentInputs_ReturnsDifferentHashes()
    {
        var h1 = _svc.ComputeHash("payload-a");
        var h2 = _svc.ComputeHash("payload-b");
        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void ComputeHash_EmptyString_ReturnsEmpty()
    {
        var hash = _svc.ComputeHash("");
        Assert.Equal("", hash);
    }

    [Fact]
    public void ComputeHash_Null_ReturnsEmpty()
    {
        var hash = _svc.ComputeHash(null!);
        Assert.Equal("", hash);
    }

    [Fact]
    public void Verify_CorrectPayload_ReturnsTrue()
    {
        var hash = _svc.ComputeHash("verify me");
        Assert.True(_svc.Verify("verify me", hash));
    }

    [Fact]
    public void Verify_WrongPayload_ReturnsFalse()
    {
        var hash = _svc.ComputeHash("original");
        Assert.False(_svc.Verify("tampered", hash));
    }

    [Fact]
    public void Verify_EmptyHash_ReturnsFalse()
    {
        Assert.False(_svc.Verify("any", ""));
        Assert.False(_svc.Verify("any", null!));
    }

    [Fact]
    public void ComputeHash_IsCaseInsensitiveInVerification()
    {
        var hash = _svc.ComputeHash("test");
        // Hash is uppercase hex, verify should be case-insensitive
        Assert.True(_svc.Verify("test", hash.ToLowerInvariant()));
        Assert.True(_svc.Verify("test", hash.ToUpperInvariant()));
    }
}
