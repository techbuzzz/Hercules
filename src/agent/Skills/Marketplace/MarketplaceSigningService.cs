using System.Security.Cryptography;
using System.Text;
using Hercules.Config;

namespace Hercules.Skills.Marketplace;

/// <summary>
///     Интерфейс сервиса подписи и верификации пакетов навыков.
/// </summary>
public interface IMarketplaceSigningService
{
    /// <summary>SHA256 hash файла пакета (hex string).</summary>
    string ComputeHash(byte[] packageBytes);

    /// <summary>HMAC-SHA256 подпись хеша пакета (base64 string).</summary>
    string ComputeSignature(string hashHex);

    /// <summary>Проверить SHA256 hash.</summary>
    bool VerifyHash(byte[] packageBytes, string expectedHashHex);

    /// <summary>Проверить HMAC-SHA256 подпись.</summary>
    bool VerifySignature(string hashHex, string signatureBase64);
}

/// <summary>
///     Сервис подписи и верификации пакетов навыков.
///     Использует HMAC-SHA256 для подписи и SHA256 для integrity.
/// </summary>
public sealed class MarketplaceSigningService : IMarketplaceSigningService
{
    private readonly byte[]? _signingKeyBytes;

    public MarketplaceSigningService(MarketplaceConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.SigningKey))
        {
            _signingKeyBytes = Convert.FromBase64String(config.SigningKey);
        }
    }

    /// <inheritdoc />
    public string ComputeHash(byte[] packageBytes)
    {
        var hash = SHA256.HashData(packageBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <inheritdoc />
    public string ComputeSignature(string hashHex)
    {
        if (_signingKeyBytes is null)
        {
            throw new InvalidOperationException("Signing key is not configured.");
        }

        var hashBytes = Convert.FromHexString(hashHex);
        using var hmac = new HMACSHA256(_signingKeyBytes);
        var signature = hmac.ComputeHash(hashBytes);
        return Convert.ToBase64String(signature);
    }

    /// <inheritdoc />
    public bool VerifyHash(byte[] packageBytes, string expectedHashHex)
    {
        if (string.IsNullOrWhiteSpace(expectedHashHex))
        {
            return false;
        }

        var actualHash = ComputeHash(packageBytes);
        return string.Equals(actualHash, expectedHashHex.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool VerifySignature(string hashHex, string signatureBase64)
    {
        if (_signingKeyBytes is null || string.IsNullOrWhiteSpace(signatureBase64))
        {
            return false;
        }

        try
        {
            var hashBytes = Convert.FromHexString(hashHex);
            using var hmac = new HMACSHA256(_signingKeyBytes);
            var expectedSignature = hmac.ComputeHash(hashBytes);
            var actualSignature = Convert.FromBase64String(signatureBase64);
            return CryptographicOperations.FixedTimeEquals(expectedSignature, actualSignature);
        }
        catch
        {
            return false;
        }
    }
}
