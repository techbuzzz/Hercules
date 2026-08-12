using System.Security.Cryptography;
using System.Text;

namespace Hercules.Audit;

/// <summary>
///     Вычисление детерминистического SHA256-хеша payload для integrity verification.
///     Использует first 16 hex chars — достаточно для обнаружения изменений без хранения полного hash.
/// </summary>
public sealed class PayloadHashService
{
    /// <summary>
    ///     Compute a short SHA256 hex hash (first 16 chars) of the given payload.
    ///     Deterministic — no random salt, no secrets.
    /// </summary>
    public string ComputeHash(string payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return "";
        }

        var bytes = Encoding.UTF8.GetBytes(payload);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes)[..16];
    }

    /// <summary>
    ///     Verify that a payload matches an expected hash.
    /// </summary>
    public bool Verify(string payload, string expectedHash)
    {
        if (string.IsNullOrEmpty(expectedHash))
        {
            return false;
        }

        return ComputeHash(payload).Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
    }
}
