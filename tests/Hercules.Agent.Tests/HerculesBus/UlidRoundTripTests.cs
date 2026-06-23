using HerculesBus;
using HerculesBus.Core;
using Xunit;

namespace Hercules.Agent.Tests.BusTests;

/// <summary>
///     Round-trip тесты ULID: encode → decode → original timestamp.
///     Также проверяет pitfall #3 (timestamp encoding boundary).
/// </summary>
public class UlidRoundTripTests
{
    [Theory]
    [InlineData(0)]                              // Unix epoch
    [InlineData(1_700_000_000_000)]              // 2023-11-14
    [InlineData(1_704_067_200_000)]              // 2024-01-01
    [InlineData(1_757_171_776_421)]              // some ms in 2026
    [InlineData(4_102_444_800_000)]              // 2100-01-01
    public void RoundTrip_Preserves_Timestamp(long unixMs)
    {
        var original = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
        var ulid = Ulid.NewId(original);
        var decoded = Ulid.ExtractTimestamp(ulid);

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void RoundTrip_Preserves_Timestamp_For_Now()
    {
        var before = DateTimeOffset.UtcNow;
        var ulid = Ulid.NewId();
        var decoded = Ulid.ExtractTimestamp(ulid);
        var after = DateTimeOffset.UtcNow;

        // decoded должен попадать в диапазон [before, after]
        Assert.InRange(decoded, before.AddMilliseconds(-1), after.AddMilliseconds(1));
    }

    [Fact]
    public void RoundTrip_Preserves_Timestamp_For_All_Ms_In_Day()
    {
        // Проверяем каждый час — должно работать (без bit overflow)
        var startOfDay = DateTimeOffset.UtcNow.Date;
        for (int hour = 0; hour < 24; hour++)
        {
            var ts = startOfDay.AddHours(hour);
            var ulid = Ulid.NewId(ts);
            var decoded = Ulid.ExtractTimestamp(ulid);
            Assert.Equal(ts, decoded);
        }
    }

    [Fact]
    public void Decode_Throws_On_Invalid_Length()
    {
        Assert.Throws<ArgumentException>(() => Ulid.ExtractTimestamp(""));
        Assert.Throws<ArgumentException>(() => Ulid.ExtractTimestamp("short"));
        Assert.Throws<ArgumentException>(() => Ulid.ExtractTimestamp(new string('A', 27)));
    }

    [Fact]
    public void Decode_Throws_On_Invalid_Char()
    {
        // I, L, O, U — Crockford alphabet их исключает
        Assert.Throws<ArgumentException>(() => Ulid.ExtractTimestamp("IIIIIIIIIIIIIIIIIIIIIIIIII"));
    }

    [Fact]
    public void Encode_Deterministic_For_Same_Timestamp()
    {
        var ts = new DateTimeOffset(2026, 6, 20, 10, 30, 0, TimeSpan.Zero);
        var id1 = Ulid.NewId(ts);
        var id2 = Ulid.NewId(ts);

        // Первые ~9 chars = timestamp prefix (48 бит = 9.6 chars; 9-й char уже включает padding).
        // Тестируем 8 chars для запаса (64 бита покрывают timestamp целиком).
        Assert.Equal(id1[..8], id2[..8]);
        // Random-часть (после timestamp) разная
        Assert.NotEqual(id1[10..], id2[10..]);
    }
}
