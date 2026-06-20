using System.Security.Cryptography;

namespace HerculesBus;

/// <summary>
///     Минимальный ULID-генератор (26 символов base32 Crockford).
///     ULID = 48-bit timestamp (ms) + 80-bit random. Лексикографически сортируется по времени.
///     Crockford alphabet: 0-9 A-Z без I, L, O, U.
///     Используется для ID сообщений (вместо GUID — для читаемости логов).
/// </summary>
public static class Ulid
{
    private const string CrockfordBase32 = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private static readonly char[] _alphabet = CrockfordBase32.ToCharArray();

    public static string NewId(DateTimeOffset? timestamp = null)
    {
        var ts = (long)(timestamp ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds();

        // 6 bytes timestamp + 10 bytes random = 16 bytes = 128 bits.
        // 128 bits / 5 bits per char = 25.6 chars → 26 chars (последние 2 бита = padding).
        Span<byte> bytes = stackalloc byte[16];
        for (int i = 5; i >= 0; i--)
        {
            bytes[5 - i] = (byte)(ts >> (8 * i));
        }
        RandomNumberGenerator.Fill(bytes[6..16]);

        Span<char> output = stackalloc char[26];
        // Берём 5 бит за раз из битового потока
        ulong acc = 0;
        int bitsInAcc = 0;
        int oi = 0;
        for (int bi = 0; bi < 16; bi++)
        {
            acc = (acc << 8) | bytes[bi];
            bitsInAcc += 8;
            while (bitsInAcc >= 5)
            {
                bitsInAcc -= 5;
                output[oi++] = _alphabet[(int)((acc >> bitsInAcc) & 0x1F)];
            }
        }
        // Flush оставшиеся биты (должно быть ≤ 4)
        if (bitsInAcc > 0)
        {
            output[oi++] = _alphabet[(int)((acc << (5 - bitsInAcc)) & 0x1F)];
        }

        return new string(output[..26]);
    }
}
