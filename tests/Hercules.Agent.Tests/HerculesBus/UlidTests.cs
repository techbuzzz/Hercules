using HerculesBus;
using HerculesBus.Core;
using Xunit;

namespace Hercules.Agent.Tests.BusTests;

public class UlidTests
{
    [Fact]
    public void NewId_Has_26_Chars()
    {
        var id = Ulid.NewId();
        Assert.Equal(26, id.Length);
    }

    [Fact]
    public void NewId_Is_Lexicographically_Sortable_By_Time()
    {
        var id1 = Ulid.NewId();
        Thread.Sleep(10);
        var id2 = Ulid.NewId();

        Assert.True(string.Compare(id1, id2, StringComparison.Ordinal) < 0,
            $"Expected {id1} < {id2}");
    }

    [Fact]
    public void NewId_Has_Crockford_Base32_Alphabet()
    {
        const string valid = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        for (int i = 0; i < 1000; i++)
        {
            var id = Ulid.NewId();
            foreach (var c in id)
                Assert.Contains(c, valid);
        }
    }

    [Fact]
    public void NewId_Has_High_Entropy_In_Last_Bytes()
    {
        // Генерируем много ID, проверяем что они все уникальны (нет коллизий при 80 бит энтропии)
        var ids = new HashSet<string>();
        for (int i = 0; i < 10_000; i++)
            ids.Add(Ulid.NewId());

        Assert.Equal(10_000, ids.Count);
    }

    [Fact]
    public void NewId_With_Specific_Timestamp_Encodes_Timestamp_In_Prefix()
    {
        // ULID: 48 бит timestamp (10 base32 chars) + 80 бит random (16 chars).
        var ts = DateTimeOffset.FromUnixTimeMilliseconds(1704067200000);
        var id1 = Ulid.NewId(ts);
        var id2 = Ulid.NewId(ts);

        Assert.Equal(26, id1.Length);
        // Первые ~8 chars = timestamp (48 бит / 5 = 9.6 chars). Берём 8 для запаса.
        Assert.Equal(id1[..8], id2[..8]);
        Assert.NotEqual(id1[10..], id2[10..]); // random часть разная
        Assert.NotEqual(id1, id2);
    }
}
