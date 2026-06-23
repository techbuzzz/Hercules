using System.Security.Cryptography;

namespace HerculesBus.Core;

/// <summary>
///     ULID (Universally Unique Lexicographically Sortable Identifier).
///     26 символов Crockford base32 = 48-bit timestamp (ms) + 80-bit random.
///     Лексикографически сортируется по времени создания — удобно для логов и пагинации.
///     Crockford alphabet: 0-9 A-Z без I, L, O, U (исключены для устранения путаницы).
///     Reference: https://github.com/ulid/spec
/// </summary>
public static class Ulid
{
    private const string CrockfordBase32 = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private static readonly char[] _alphabet = CrockfordBase32.ToCharArray();

    /// <summary>Сгенерировать новый ULID для текущего UTC времени.</summary>
    public static string NewId() => NewId(DateTimeOffset.UtcNow);

    /// <summary>Сгенерировать ULID для конкретного timestamp (полезно для тестов и replay).</summary>
    public static string NewId(DateTimeOffset timestamp)
    {
        var tsMs = (long)timestamp.ToUnixTimeMilliseconds();

        // 6 bytes timestamp (48 бит) + 10 bytes random (80 бит) = 16 bytes = 128 бит.
        Span<byte> bytes = stackalloc byte[16];
        for (int i = 5; i >= 0; i--)
        {
            bytes[5 - i] = (byte)(tsMs >> (8 * i));
        }
        RandomNumberGenerator.Fill(bytes[6..16]);

        return EncodeBase32(bytes);
    }

    /// <summary>Извлечь timestamp из ULID (первые 10 символов).</summary>
    public static DateTimeOffset ExtractTimestamp(string ulid)
    {
        if (string.IsNullOrEmpty(ulid) || ulid.Length != 26)
            throw new ArgumentException("ULID must be 26 chars", nameof(ulid));

        // Decode первые 10 chars → 50 бит, но timestamp 48 бит, последние 2 бита = 0 padding
        ulong hi = 0;
        for (int i = 0; i < 10; i++)
        {
            int val = AlphabetIndex(ulid[i]);
            if (val < 0) throw new ArgumentException($"Invalid ULID char at {i}: {ulid[i]}");
            hi = (hi << 5) | (uint)val;
        }
        // hi содержит 50 бит = 48 бит timestamp + 2 бита padding (= 0)
        var tsMs = (long)(hi >> 2);
        return DateTimeOffset.FromUnixTimeMilliseconds(tsMs);
    }

    private static string EncodeBase32(ReadOnlySpan<byte> bytes)
    {
        // 128 бит / 5 бит на char = 25.6 chars → 26 chars (последние 2 бита padding).
        Span<char> output = stackalloc char[26];
        ulong acc = 0;
        int bitsInAcc = 0;
        int oi = 0;
        for (int bi = 0; bi < bytes.Length; bi++)
        {
            acc = (acc << 8) | bytes[bi];
            bitsInAcc += 8;
            while (bitsInAcc >= 5)
            {
                bitsInAcc -= 5;
                output[oi++] = _alphabet[(int)((acc >> bitsInAcc) & 0x1F)];
            }
        }
        // Flush оставшиеся биты (< 5, padding)
        if (bitsInAcc > 0)
        {
            output[oi++] = _alphabet[(int)((acc << (5 - bitsInAcc)) & 0x1F)];
        }

        return new string(output[..26]);
    }

    private static int AlphabetIndex(char c)
    {
        for (int i = 0; i < _alphabet.Length; i++)
            if (_alphabet[i] == c) return i;
        return -1;
    }
}
