using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Hercules.Mesh.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using RedisMeshConfig = Hercules.Mesh.Backends.Redis.RedisMeshConfig;

/// <summary>
///     Distributed state store using Redis.
///     Key-value with optimistic locking (WATCH/MULTI/EXEC), TTL, INCR, WATCH for notifications.
///     Falls back gracefully when Redis is unavailable.
///     Spec: task_067.
/// </summary>
public sealed class RedisMeshStateStore : IMeshStateStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisMeshConfig _config;
    private readonly ILogger<RedisMeshStateStore> _log;
    private readonly JsonSerializerOptions _json;
    private readonly ConcurrentDictionary<string, Channel<StoredValue>> _watchChannels = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<Channel<StoredValue>>> _prefixWatchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Timer> _watchTimers = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public string BackendKind => "redis";

    public RedisMeshStateStore(
        IConnectionMultiplexer redis,
        RedisMeshConfig config,
        ILogger<RedisMeshStateStore> log)
    {
        _redis = redis;
        _config = config;
        _log = log;
        _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <inheritdoc />
    public async Task<StoredValue?> GetAsync(string key, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(key);

        try
        {
            var db = _redis.GetDatabase();
            var prefixedKey = _config.KeyPrefix + key;
            var hash = await db.HashGetAllAsync(prefixedKey).WaitAsync(ct).ConfigureAwait(false);

            if (hash.Length == 0)
                return null;

            var value = ReadStoredValue(hash);
            if (value.ExpiresAt.HasValue && value.ExpiresAt.Value < DateTimeOffset.UtcNow)
            {
                await db.KeyDeleteAsync(prefixedKey).ConfigureAwait(false);
                return null;
            }

            return value;
        }
        catch (RedisConnectionException ex)
        {
            _log.LogWarning(ex, "[RedisStateStore] Redis unavailable for GetAsync({Key})", key);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetAsync(string key, StoredValue value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(key);

        try
        {
            var db = _redis.GetDatabase();
            var prefixedKey = _config.KeyPrefix + key;
            var now = DateTimeOffset.UtcNow;
            var version = Guid.NewGuid().ToString("N");
            var expiresAt = ttl.HasValue ? now.Add(ttl.Value) : (DateTimeOffset?)null;

            var existingHash = await db.HashGetAllAsync(prefixedKey).WaitAsync(ct).ConfigureAwait(false);
            var createdAt = existingHash.Length > 0
                ? ReadStoredValue(existingHash).CreatedAt
                : now;

            var updatedValue = value with { Version = version, CreatedAt = createdAt, UpdatedAt = now, ExpiresAt = expiresAt };

            var entries = BuildHashEntries(updatedValue);
            var expiry = expiresAt.HasValue ? (TimeSpan?)(expiresAt.Value - now) : null;

            await db.HashSetAsync(prefixedKey, entries).ConfigureAwait(false);
            if (expiry.HasValue)
                await db.KeyExpireAsync(prefixedKey, expiry.Value).ConfigureAwait(false);

            NotifyWatchers(key, updatedValue);
        }
        catch (RedisConnectionException ex)
        {
            _log.LogWarning(ex, "[RedisStateStore] Redis unavailable for SetAsync({Key})", key);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> CompareAndSetAsync(
        string key,
        StoredValue value,
        string? expectedVersion,
        TimeSpan? ttl = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(key);

        try
        {
            var db = _redis.GetDatabase();
            var prefixedKey = _config.KeyPrefix + key;
            var now = DateTimeOffset.UtcNow;
            var version = Guid.NewGuid().ToString("N");
            var expiresAt = ttl.HasValue ? now.Add(ttl.Value) : (DateTimeOffset?)null;

            if (expectedVersion is null)
            {
                // Create-if-not-exists using NX
                var existingHash = await db.HashGetAllAsync(prefixedKey).WaitAsync(ct).ConfigureAwait(false);
                if (existingHash.Length > 0)
                    return false;

                var hashEntries = BuildHashEntries(value with
                {
                    Version = version,
                    CreatedAt = now,
                    UpdatedAt = now,
                    ExpiresAt = expiresAt
                });
                await db.HashSetAsync(prefixedKey, hashEntries).ConfigureAwait(false);
                return true;
            }

            // Optimistic locking: WATCH current version, then MULTI/EXEC
            var tran = db.CreateTransaction();
            var watchedKey = prefixedKey;

            // Get current version under watch
            var currentHash = await db.HashGetAllAsync(watchedKey).WaitAsync(ct).ConfigureAwait(false);
            if (currentHash.Length == 0)
                return false; // Key doesn't exist

            var current = ReadStoredValue(currentHash);
            if (current.Version != expectedVersion)
                return false; // Version mismatch

            // Re-read createdAt
            var createdAt = current.CreatedAt;
            var updatedValue = value with { Version = version, CreatedAt = createdAt, UpdatedAt = now, ExpiresAt = expiresAt };
            var entries = BuildHashEntries(updatedValue);

            var setTask = tran.HashSetAsync(watchedKey, entries);
            if (expiresAt.HasValue)
                _ = tran.KeyExpireAsync(watchedKey, expiresAt.Value - now);

            var committed = await tran.ExecuteAsync().WaitAsync(ct).ConfigureAwait(false);
            if (committed)
            {
                NotifyWatchers(key, updatedValue);
                return true;
            }

            return false; // CAS failed due to concurrent modification
        }
        catch (RedisConnectionException ex)
        {
            _log.LogWarning(ex, "[RedisStateStore] Redis unavailable for CompareAndSetAsync({Key})", key);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(key);

        try
        {
            var db = _redis.GetDatabase();
            var prefixedKey = _config.KeyPrefix + key;
            var deleted = await db.KeyDeleteAsync(prefixedKey).WaitAsync(ct).ConfigureAwait(false);
            if (deleted)
                NotifyWatchers(key, null);
            return deleted;
        }
        catch (RedisConnectionException ex)
        {
            _log.LogWarning(ex, "[RedisStateStore] Redis unavailable for DeleteAsync({Key})", key);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(key);

        try
        {
            var db = _redis.GetDatabase();
            var prefixedKey = _config.KeyPrefix + key;
            return await db.KeyExistsAsync(prefixedKey).WaitAsync(ct).ConfigureAwait(false);
        }
        catch (RedisConnectionException ex)
        {
            _log.LogWarning(ex, "[RedisStateStore] Redis unavailable for ExistsAsync({Key})", key);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<long> IncrementAsync(string key, long delta = 1, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        try
        {
            var db = _redis.GetDatabase();
            var prefixedKey = _config.KeyPrefix + key;
            var result = await db.StringIncrementAsync(prefixedKey, delta).WaitAsync(ct).ConfigureAwait(false);

            // Refresh TTL on each increment (sliding-window semantics): the counter
            // expires `ttl` after the last call instead of after the first.
            if (ttl.HasValue)
            {
                await db.KeyExpireAsync(prefixedKey, ttl.Value).WaitAsync(ct).ConfigureAwait(false);
            }

            // Notify watchers on change
            var stored = await GetAsync(key, ct).ConfigureAwait(false);
            if (stored != null)
                NotifyWatchers(key, stored);

            return result;
        }
        catch (RedisConnectionException ex)
        {
            _log.LogWarning(ex, "[RedisStateStore] Redis unavailable for IncrementAsync({Key})", key);
            throw;
        }
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
            // Prefix watcher — use polling since Redis keyspace notifications on patterns are complex
            var prefix = key[..^1];
            var channel = Channel.CreateUnbounded<StoredValue>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

            var watchers = _prefixWatchers.GetOrAdd(prefix, _ => new List<Channel<StoredValue>>());
            lock (watchers)
            {
                watchers.Add(channel);
            }

            var timer = new Timer(
                state => ((RedisMeshStateStore)state!).PollPrefixWatchers(prefix, channel),
                this,
                TimeSpan.FromMilliseconds(_config.WatchPollingIntervalMs),
                TimeSpan.FromMilliseconds(_config.WatchPollingIntervalMs));

            _watchTimers.TryAdd($"prefix:{prefix}", timer);

            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var value in channel.Reader.ReadAllAsync(ct))
                        await handler(value, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
            }, ct);

            return Task.FromResult<IDisposable>(new PrefixWatcher(watchers, channel, $"prefix:{prefix}", _watchTimers));
        }

        // Exact key watcher — use polling
        var exactChannel = _watchChannels.GetOrAdd(
            key,
            static _ => Channel.CreateUnbounded<StoredValue>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }));

        var exactTimer = new Timer(
            state => ((RedisMeshStateStore)state!).PollExactWatchers(key, exactChannel),
            this,
            TimeSpan.FromMilliseconds(_config.WatchPollingIntervalMs),
            TimeSpan.FromMilliseconds(_config.WatchPollingIntervalMs));

        _watchTimers.TryAdd($"exact:{key}", exactTimer);

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var value in exactChannel.Reader.ReadAllAsync(ct))
                    await handler(value, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
        }, ct);

        return Task.FromResult<IDisposable>(new ExactWatcher(_watchChannels, key, $"exact:{key}", _watchTimers));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ScanKeysAsync(
        string prefix,
        int limit = 100,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        try
        {
            var db = _redis.GetDatabase();
            var prefixedPrefix = _config.KeyPrefix + prefix;
            var result = new List<string>();

            var server = _redis.GetServer(_redis.GetEndPoints().First());
            await foreach (var key in server.KeysAsync(pattern: prefixedPrefix + "*", pageSize: limit).WithCancellation(ct))
            {
                if (result.Count >= limit) break;
                var keyStr = key.ToString();
                if (keyStr.StartsWith(_config.KeyPrefix))
                    keyStr = keyStr[_config.KeyPrefix.Length..];
                result.Add(keyStr);
            }

            return result;
        }
        catch (RedisConnectionException ex)
        {
            _log.LogWarning(ex, "[RedisStateStore] Redis unavailable for ScanKeysAsync({Prefix})", prefix);
            return Array.Empty<string>();
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        if (_disposed) return false;
        try
        {
            var db = _redis.GetDatabase();
            await db.PingAsync().WaitAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private HashEntry[] BuildHashEntries(StoredValue value)
    {
        return new[]
        {
            new HashEntry("data", value.Data),
            new HashEntry("version", value.Version),
            new HashEntry("createdAt", value.CreatedAt.ToUnixTimeMilliseconds()),
            new HashEntry("updatedAt", value.UpdatedAt.ToUnixTimeMilliseconds()),
            new HashEntry("expiresAt", value.ExpiresAt?.ToUnixTimeMilliseconds() ?? 0),
            new HashEntry("lastWriterAgentId", value.LastWriterAgentId ?? ""),
        };
    }

    private StoredValue ReadStoredValue(HashEntry[] hash)
    {
        string data = "", version = "", lastWriterAgentId = "";
        long createdAtMs = 0, updatedAtMs = 0, expiresAtMs = 0;

        foreach (var entry in hash)
        {
            switch (entry.Name.ToString())
            {
                case "data": data = entry.Value.ToString(); break;
                case "version": version = entry.Value.ToString(); break;
                case "createdAt": _ = long.TryParse(entry.Value.ToString(), out createdAtMs); break;
                case "updatedAt": _ = long.TryParse(entry.Value.ToString(), out updatedAtMs); break;
                case "expiresAt": _ = long.TryParse(entry.Value.ToString(), out expiresAtMs); break;
                case "lastWriterAgentId": lastWriterAgentId = entry.Value.ToString(); break;
            }
        }

        return new StoredValue
        {
            Data = data,
            Version = version,
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(createdAtMs),
            UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(updatedAtMs),
            ExpiresAt = expiresAtMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(expiresAtMs) : null,
            LastWriterAgentId = string.IsNullOrEmpty(lastWriterAgentId) ? null : lastWriterAgentId
        };
    }

    private void NotifyWatchers(string key, StoredValue? value)
    {
        var sentinel = value ?? new StoredValue
        {
            Data = "",
            Version = "",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Exact key watchers
        if (_watchChannels.TryGetValue(key, out var channel))
            channel.Writer.TryWrite(sentinel);

        // Prefix watchers
        foreach (var kvp in _prefixWatchers)
        {
            if (key.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var prefixChannel in kvp.Value)
                    prefixChannel.Writer.TryWrite(sentinel);
            }
        }
    }

    private string? _lastExactSnapshot;

    private void PollExactWatchers(string key, Channel<StoredValue> channel)
    {
        try
        {
            var stored = GetAsync(key, CancellationToken.None).GetAwaiter().GetResult();
            var snapshot = stored?.Version ?? "";
            if (snapshot != _lastExactSnapshot)
            {
                _lastExactSnapshot = snapshot;
                channel.Writer.TryWrite(stored ?? new StoredValue
                {
                    Data = "",
                    Version = "",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
        }
        catch { /* Polling — ignore errors */ }
    }

    private void PollPrefixWatchers(string prefix, Channel<StoredValue> channel)
    {
        try
        {
            var keys = ScanKeysAsync(prefix, 10, CancellationToken.None).GetAwaiter().GetResult();
            foreach (var k in keys)
            {
                var stored = GetAsync(k, CancellationToken.None).GetAwaiter().GetResult();
                if (stored != null)
                    channel.Writer.TryWrite(stored);
            }
        }
        catch { /* Polling — ignore errors */ }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RedisMeshStateStore));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true) == true)
            return;

        foreach (var timer in _watchTimers.Values)
            timer.Dispose();
        _watchTimers.Clear();
        _watchChannels.Clear();
        _prefixWatchers.Clear();
    }

    private sealed class ExactWatcher : IDisposable
    {
        private readonly ConcurrentDictionary<string, Channel<StoredValue>> _channels;
        private readonly string _key;
        private readonly string _timerKey;
        private readonly ConcurrentDictionary<string, Timer> _timers;
        private bool _disposed;

        public ExactWatcher(ConcurrentDictionary<string, Channel<StoredValue>> channels, string key, string timerKey, ConcurrentDictionary<string, Timer> timers)
        {
            _channels = channels;
            _key = key;
            _timerKey = timerKey;
            _timers = timers;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, true) == false)
            {
                _channels.TryRemove(_key, out _);
                if (_timers.TryRemove(_timerKey, out var timer))
                    timer.Dispose();
            }
        }
    }

    private sealed class PrefixWatcher : IDisposable
    {
        private readonly List<Channel<StoredValue>> _watchers;
        private readonly Channel<StoredValue> _channel;
        private readonly string _timerKey;
        private readonly ConcurrentDictionary<string, Timer> _timers;
        private bool _disposed;

        public PrefixWatcher(List<Channel<StoredValue>> watchers, Channel<StoredValue> channel, string timerKey, ConcurrentDictionary<string, Timer> timers)
        {
            _watchers = watchers;
            _channel = channel;
            _timerKey = timerKey;
            _timers = timers;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, true) == false)
            {
                lock (_watchers)
                {
                    _watchers.Remove(_channel);
                }

                if (_timers.TryRemove(_timerKey, out var timer))
                    timer.Dispose();
            }
        }
    }
}
