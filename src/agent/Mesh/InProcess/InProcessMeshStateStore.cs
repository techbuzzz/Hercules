using System.Collections.Concurrent;
using System.Threading.Channels;
using Hercules.Mesh.Abstractions;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.InProcess;

/// <summary>
///     In-process shared state store на базе <see cref="ConcurrentDictionary{TKey, TValue}"/>.
///     Version-based optimistic locking (increment on write).
///     TTL эмулируется через cleanup task.
///     Watch — через <see cref="Channel{T}"/> per key prefix.
///     Потокобезопасен. Подходит для single-host mesh и разработки.
///     Спецификация: task_066.
/// </summary>
public sealed class InProcessMeshStateStore : IMeshStateStore
{
    private readonly ConcurrentDictionary<string, StoredValueEntry> _store = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Channel<StoredValue>> _watchChannels = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<Channel<StoredValue>>> _prefixWatchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _cleanupTimer;
    private readonly ILogger<InProcessMeshStateStore>? _logger;
    private bool _disposed;

    public string BackendKind => "in-process";

    public InProcessMeshStateStore(ILogger<InProcessMeshStateStore>? logger = null)
    {
        _logger = logger;
        // Cleanup expired keys every 30 seconds
        _cleanupTimer = new Timer(_ => CleanupExpired(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    /// <inheritdoc />
    public Task<StoredValue?> GetAsync(string key, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (_store.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value < DateTimeOffset.UtcNow)
            {
                _store.TryRemove(key, out _);
                return Task.FromResult<StoredValue?>(null);
            }
            return Task.FromResult<StoredValue?>(entry.Value);
        }

        return Task.FromResult<StoredValue?>(null);
    }

    /// <inheritdoc />
    public Task SetAsync(string key, StoredValue value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var now = DateTimeOffset.UtcNow;
        var version = Guid.NewGuid().ToString("N");
        var expiresAt = ttl.HasValue ? now.Add(ttl.Value) : (DateTimeOffset?)null;

        var entry = new StoredValueEntry
        {
            Value = value with
            {
                Version = version,
                CreatedAt = _store.TryGetValue(key, out var existing)
                    ? existing.Value.CreatedAt
                    : now,
                UpdatedAt = now,
                ExpiresAt = expiresAt
            },
            ExpiresAt = expiresAt
        };

        _store[key] = entry;
        NotifyWatchers(key, entry.Value);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> CompareAndSetAsync(
        string key,
        StoredValue value,
        string? expectedVersion,
        TimeSpan? ttl = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var now = DateTimeOffset.UtcNow;
        var version = Guid.NewGuid().ToString("N");
        var expiresAt = ttl.HasValue ? now.Add(ttl.Value) : (DateTimeOffset?)null;

        var newEntry = new StoredValueEntry
        {
            Value = value with
            {
                Version = version,
                CreatedAt = _store.TryGetValue(key, out var existing)
                    ? existing.Value.CreatedAt
                    : now,
                UpdatedAt = now,
                ExpiresAt = expiresAt
            },
            ExpiresAt = expiresAt
        };

        if (expectedVersion is null)
        {
            // Create-if-not-exists
            if (!_store.TryAdd(key, newEntry))
                return Task.FromResult(false);
            NotifyWatchers(key, newEntry.Value);
            return Task.FromResult(true);
        }

        // Compare current version
        if (!_store.TryGetValue(key, out var current) ||
            current.Value.Version != expectedVersion)
        {
            return Task.FromResult(false);
        }

        // Atomic update
        _store[key] = newEntry;
        NotifyWatchers(key, newEntry.Value);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var removed = _store.TryRemove(key, out _);
        if (removed)
        {
            NotifyWatchers(key, null);
        }
        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (_store.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value < DateTimeOffset.UtcNow)
            {
                _store.TryRemove(key, out _);
                return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<long> IncrementAsync(string key, long delta = 1, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var now = DateTimeOffset.UtcNow;
        var expiresAt = ttl.HasValue ? now.Add(ttl.Value) : (DateTimeOffset?)null;

        var newValue = _store.AddOrUpdate(
            key,
            // Not found — create with delta
            _ => new StoredValueEntry
            {
                Value = new StoredValue
                {
                    Data = delta.ToString(),
                    Version = Guid.NewGuid().ToString("N"),
                    CreatedAt = now,
                    UpdatedAt = now,
                    ExpiresAt = expiresAt
                },
                ExpiresAt = expiresAt
            },
            // Found — increment; refresh TTL on each call (sliding window semantics).
            (_, existing) =>
            {
                if (!long.TryParse(existing.Value.Data, out var current))
                    current = 0;
                return new StoredValueEntry
                {
                    Value = existing.Value with
                    {
                        Data = (current + delta).ToString(),
                        Version = Guid.NewGuid().ToString("N"),
                        UpdatedAt = now,
                        ExpiresAt = expiresAt ?? existing.Value.ExpiresAt
                    },
                    ExpiresAt = expiresAt ?? existing.ExpiresAt
                };
            });

        NotifyWatchers(key, newValue.Value);
        return Task.FromResult(long.Parse(newValue.Value.Data));
    }

    /// <inheritdoc />
    public Task<IDisposable> WatchAsync(
        string key,
        Func<StoredValue, CancellationToken, Task> handler,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var isPrefix = key.EndsWith("*");
        if (isPrefix)
        {
            // Prefix watcher
            var prefix = key[..^1];
            var channel = Channel.CreateUnbounded<StoredValue>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

            var watchers = _prefixWatchers.GetOrAdd(prefix, _ => new List<Channel<StoredValue>>());
            lock (watchers)
            {
                watchers.Add(channel);
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var value in channel.Reader.ReadAllAsync(ct))
                    {
                        await handler(value, ct);
                    }
                }
                catch (OperationCanceledException) { }
            }, ct);

            return Task.FromResult<IDisposable>(new PrefixWatcher(watchers, channel));
        }

        // Exact key watcher
        var exactChannel = _watchChannels.GetOrAdd(
            key,
            static _ => Channel.CreateUnbounded<StoredValue>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }));

        _ = Task.Run(async () =>
        {
            await foreach (var value in exactChannel.Reader.ReadAllAsync(ct))
            {
                await handler(value, ct);
            }
        }, ct);

        return Task.FromResult<IDisposable>(new ExactWatcher(_watchChannels, key));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ScanKeysAsync(
        string prefix,
        int limit = 100,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var result = new List<string>();
        var now = DateTimeOffset.UtcNow;

        foreach (var kvp in _store)
        {
            if (result.Count >= limit)
                break;

            if (kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                if (kvp.Value.ExpiresAt.HasValue && kvp.Value.ExpiresAt.Value < now)
                {
                    _store.TryRemove(kvp.Key, out _);
                    continue;
                }
                result.Add(kvp.Key);
            }
        }

        return Task.FromResult<IReadOnlyList<string>>(result);
    }

    /// <inheritdoc />
    public ValueTask<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        return new ValueTask<bool>(!_disposed);
    }

    private void NotifyWatchers(string key, StoredValue? value)
    {
        // Notify exact key watchers
        if (_watchChannels.TryGetValue(key, out var channel))
        {
            channel.Writer.TryWrite(value ?? new StoredValue
            {
                Data = "",
                Version = "",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        // Notify prefix watchers
        foreach (var kvp in _prefixWatchers)
        {
            if (key.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var prefixChannel in kvp.Value)
                {
                    prefixChannel.Writer.TryWrite(value ?? new StoredValue
                    {
                        Data = "",
                        Version = "",
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow
                    });
                }
            }
        }
    }

    private void CleanupExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _store)
        {
            if (kvp.Value.ExpiresAt.HasValue && kvp.Value.ExpiresAt.Value < now)
            {
                if (_store.TryRemove(kvp.Key, out _))
                {
                    _logger?.LogDebug("Cleaned up expired key {Key}", kvp.Key);
                }
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(InProcessMeshStateStore));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true) == true)
            return;

        _cleanupTimer.Dispose();
        _watchChannels.Clear();
        _prefixWatchers.Clear();
        _store.Clear();
    }

    private sealed class StoredValueEntry
    {
        public required StoredValue Value { get; init; }
        public DateTimeOffset? ExpiresAt { get; init; }
    }

    private sealed class ExactWatcher : IDisposable
    {
        private readonly ConcurrentDictionary<string, Channel<StoredValue>> _channels;
        private readonly string _key;
        private bool _disposed;

        public ExactWatcher(ConcurrentDictionary<string, Channel<StoredValue>> channels, string key)
        {
            _channels = channels;
            _key = key;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, true) == false)
            {
                _channels.TryRemove(_key, out _);
            }
        }
    }

    private sealed class PrefixWatcher : IDisposable
    {
        private readonly List<Channel<StoredValue>> _watchers;
        private readonly Channel<StoredValue> _channel;
        private bool _disposed;

        public PrefixWatcher(List<Channel<StoredValue>> watchers, Channel<StoredValue> channel)
        {
            _watchers = watchers;
            _channel = channel;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, true) == false)
            {
                lock (_watchers)
                {
                    _watchers.Remove(_channel);
                }
            }
        }
    }
}
