using Hercules.Mesh.Abstractions;
using Hercules.Mesh.InProcess;
using Xunit;

namespace Hercules.Agent.Tests.Mesh;

/// <summary>
///     Unit tests for <see cref="InProcessMeshStateStore"/> (task_066).
///     Covers: Get, Set, CompareAndSet, Delete, Exists, Increment, Watch, ScanKeys, IsHealthy, Dispose.
/// </summary>
public class InProcessMeshStateStoreTests : IDisposable
{
    private readonly InProcessMeshStateStore _store;

    public InProcessMeshStateStoreTests()
    {
        _store = new InProcessMeshStateStore();
    }

    [Fact]
    public void BackendKind_ReturnsInProcess()
    {
        Assert.Equal("in-process", _store.BackendKind);
    }

    [Fact]
    public async Task SetAsync_And_GetAsync_RoundTrips()
    {
        var key = $"key/{Guid.NewGuid():N}";
        var value = new StoredValue
        {
            Data = "hello world",
            Version = "v1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _store.SetAsync(key, value);

        var result = await _store.GetAsync(key);

        Assert.NotNull(result);
        Assert.Equal("hello world", result.Data);
        Assert.NotEmpty(result.Version);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenKeyNotFound()
    {
        var result = await _store.GetAsync($"nonexistent/{Guid.NewGuid():N}");
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_Overwrites_And_UpdatesVersion()
    {
        var key = $"key/{Guid.NewGuid():N}";

        await _store.SetAsync(key, new StoredValue
        {
            Data = "v1",
            Version = "v1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var first = await _store.GetAsync(key);

        await _store.SetAsync(key, new StoredValue
        {
            Data = "v2",
            Version = "v2",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var second = await _store.GetAsync(key);

        Assert.Equal("v1", first!.Data);
        Assert.Equal("v2", second!.Data);
        Assert.NotEqual(first.Version, second.Version);
    }

    [Fact]
    public async Task CompareAndSetAsync_Succeeds_WhenVersionMatches()
    {
        var key = $"key/{Guid.NewGuid():N}";

        await _store.SetAsync(key, new StoredValue
        {
            Data = "original",
            Version = "v0",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var current = await _store.GetAsync(key);
        Assert.NotNull(current);

        var success = await _store.CompareAndSetAsync(
            key,
            new StoredValue
            {
                Data = "updated",
                Version = "v1",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            expectedVersion: current.Version);

        Assert.True(success);
        Assert.Equal("updated", (await _store.GetAsync(key))!.Data);
    }

    [Fact]
    public async Task CompareAndSetAsync_Fails_WhenVersionMismatch()
    {
        var key = $"key/{Guid.NewGuid():N}";

        await _store.SetAsync(key, new StoredValue
        {
            Data = "original",
            Version = "v0",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var success = await _store.CompareAndSetAsync(
            key,
            new StoredValue
            {
                Data = "updated",
                Version = "v1",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            expectedVersion: "wrong-version");

        Assert.False(success);
        Assert.Equal("original", (await _store.GetAsync(key))!.Data);
    }

    [Fact]
    public async Task CompareAndSetAsync_CreatesKey_WhenExpectedVersionNull_AndKeyMissing()
    {
        var key = $"key/{Guid.NewGuid():N}";

        var success = await _store.CompareAndSetAsync(
            key,
            new StoredValue
            {
                Data = "created",
                Version = "v0",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            expectedVersion: null);

        Assert.True(success);
        Assert.Equal("created", (await _store.GetAsync(key))!.Data);
    }

    [Fact]
    public async Task CompareAndSetAsync_Fails_WhenExpectedVersionNull_AndKeyExists()
    {
        var key = $"key/{Guid.NewGuid():N}";

        await _store.SetAsync(key, new StoredValue
        {
            Data = "existing",
            Version = "v0",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var success = await _store.CompareAndSetAsync(
            key,
            new StoredValue
            {
                Data = "created",
                Version = "v1",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            expectedVersion: null);

        Assert.False(success);
        Assert.Equal("existing", (await _store.GetAsync(key))!.Data);
    }

    [Fact]
    public async Task DeleteAsync_RemovesKey()
    {
        var key = $"key/{Guid.NewGuid():N}";

        await _store.SetAsync(key, new StoredValue
        {
            Data = "to-delete",
            Version = "v0",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var deleted = await _store.DeleteAsync(key);
        var result = await _store.GetAsync(key);

        Assert.True(deleted);
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse_WhenKeyNotFound()
    {
        var deleted = await _store.DeleteAsync($"nonexistent/{Guid.NewGuid():N}");
        Assert.False(deleted);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrue_WhenKeyExists()
    {
        var key = $"key/{Guid.NewGuid():N}";

        await _store.SetAsync(key, new StoredValue
        {
            Data = "exists",
            Version = "v0",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        Assert.True(await _store.ExistsAsync(key));
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenKeyNotFound()
    {
        Assert.False(await _store.ExistsAsync($"nonexistent/{Guid.NewGuid():N}"));
    }

    [Fact]
    public async Task IncrementAsync_IncrementsValue()
    {
        var key = $"counter/{Guid.NewGuid():N}";

        await _store.SetAsync(key, new StoredValue
        {
            Data = "10",
            Version = "v0",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var result = await _store.IncrementAsync(key, 5);

        Assert.Equal(15, result);
        Assert.Equal("15", (await _store.GetAsync(key))!.Data);
    }

    [Fact]
    public async Task IncrementAsync_CreatesKey_WhenMissing()
    {
        var key = $"counter/{Guid.NewGuid():N}";

        var result = await _store.IncrementAsync(key, 1);

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task WatchAsync_NotifiesOnChange()
    {
        var key = $"watch/{Guid.NewGuid():N}";
        var notifications = new List<StoredValue>();

        var d = await _store.WatchAsync(key, async (v, _) =>
        {
            notifications.Add(v);
            await Task.CompletedTask;
        });

        await _store.SetAsync(key, new StoredValue
        {
            Data = "first",
            Version = "v1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await _store.SetAsync(key, new StoredValue
        {
            Data = "second",
            Version = "v2",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await Task.Delay(50);

        Assert.Equal(2, notifications.Count);
        Assert.Equal("first", notifications[0].Data);
        Assert.Equal("second", notifications[1].Data);

        d.Dispose();
    }

    [Fact]
    public async Task WatchAsync_Unsubscribe_StopsNotifications()
    {
        var key = $"watch/{Guid.NewGuid():N}";
        var count = 0;

        var d = await _store.WatchAsync(key, async (_, _) => { count++; await Task.CompletedTask; });

        await _store.SetAsync(key, new StoredValue
        {
            Data = "one",
            Version = "v1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await Task.Delay(30);
        Assert.Equal(1, count);

        d.Dispose();

        await _store.SetAsync(key, new StoredValue
        {
            Data = "two",
            Version = "v2",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await Task.Delay(30);
        Assert.Equal(1, count); // No more notifications
    }

    [Fact]
    public async Task ScanKeysAsync_ReturnsMatchingPrefix()
    {
        var prefix = $"scan/{Guid.NewGuid():N}";
        await _store.SetAsync($"{prefix}/a", new StoredValue { Data = "a", Version = "v0", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        await _store.SetAsync($"{prefix}/b", new StoredValue { Data = "b", Version = "v0", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        await _store.SetAsync($"{prefix}/c", new StoredValue { Data = "c", Version = "v0", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        await _store.SetAsync($"other/key", new StoredValue { Data = "other", Version = "v0", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });

        var keys = await _store.ScanKeysAsync(prefix);

        Assert.Equal(3, keys.Count);
        Assert.All(keys, k => Assert.StartsWith(prefix, k));
    }

    [Fact]
    public async Task ScanKeysAsync_RespectsLimit()
    {
        var prefix = $"scan/{Guid.NewGuid():N}";
        for (int i = 0; i < 10; i++)
        {
            await _store.SetAsync($"{prefix}/key{i}", new StoredValue { Data = $"{i}", Version = "v0", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        }

        var keys = await _store.ScanKeysAsync(prefix, limit: 3);

        Assert.Equal(3, keys.Count);
    }

    [Fact]
    public async Task IsHealthyAsync_ReturnsTrue_WhenNotDisposed()
    {
        Assert.True(await _store.IsHealthyAsync());
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var s1 = new InProcessMeshStateStore();
        s1.Dispose();
        s1.Dispose(); // No throw
    }

    public void Dispose()
    {
        _store.Dispose();
    }
}
