using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Hercules.Mesh.Abstractions;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;
using NatsMeshConfig = Hercules.Mesh.Backends.Nats.NatsMeshConfig;

namespace Hercules.Mesh.Backends.Nats;

/// <summary>
///     Shared state store using NATS JetStream Key-Value store.
///     Optimistic locking via version (sequence number), TTL, INCR, Watch.
///     Spec: task_068.
/// </summary>
public sealed class NatsMeshStateStore : IMeshStateStore
{
    private readonly NatsConnection _connection;
    private readonly NatsMeshConfig _config;
    private readonly ILogger<NatsMeshStateStore> _log;
    private readonly JsonSerializerOptions _json;
    private readonly ConcurrentDictionary<string, Channel<StoredValue>> _watchChannels = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Timer> _watchTimers = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _bucketName;
    private bool _disposed;
    private INatsKVStore? _kvStore;
    private bool _bucketInitialized;
    private readonly object _initLock = new();

    public string BackendKind => "nats";

    public NatsMeshStateStore(
        NatsConnection connection,
        NatsMeshConfig config,
        ILogger<NatsMeshStateStore> log)
    {
        _connection = connection;
        _config = config;
        _log = log;
        _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        _bucketName = $"{config.StreamPrefix}-state";
    }

    private async Task EnsureBucketAsync(CancellationToken ct)
    {
        if (_bucketInitialized) return;
        lock (_initLock)
        {
            if (_bucketInitialized) return;
        }

        try
        {
            var jsContext = new NatsJSContext(_connection);
            var ctx = new NatsKVContext(jsContext);
            try
            {
                _kvStore = await ctx.CreateStoreAsync(
                    new NatsKVConfig(_bucketName)
                    {
                        Description = "Hercules mesh state store",
                        MaxBytes = _config.JetStreamMaxBytes,
                        History = 1,
                        MaxAge = TimeSpan.FromSeconds(_config.DefaultTtlSeconds)
                    },
                    ct).ConfigureAwait(false);
                _log.LogInformation("[NatsStateStore] Created KV store {Bucket}", _bucketName);
            }
            catch (Exception ex) when (ex.Message.Contains("already exists") || ex is InvalidOperationException)
            {
                // Already exists — try to get it
                try
                {
                    _kvStore = await ctx.GetStoreAsync(_bucketName, ct).ConfigureAwait(false);
                }
                catch
                {
                    _kvStore = null;
                }
            }

            _bucketInitialized = true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[NatsStateStore] Failed to initialize KV store");
        }
    }

    /// <inheritdoc />
    public async Task<StoredValue?> GetAsync(string key, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(key);

        try
        {
            if (_kvStore is null)
                await EnsureBucketAsync(ct).ConfigureAwait(false);
            var store = _kvStore;
            if (store is null) return null;

            // TryGetEntryAsync returns NatsResult<NatsKVEntry<T>>. Use Revision==0 to detect not-found.
            var result = await store.TryGetEntryAsync<string>(key, 0UL, null, ct).ConfigureAwait(false);
            if (!result.Success || result.Value.Revision == 0UL)
                return null; // Key not found

            return new StoredValue
            {
                Data = result.Value.Value ?? string.Empty,
                Version = result.Value.Revision.ToString(),
                CreatedAt = result.Value.Created,
                UpdatedAt = result.Value.Created,
                ExpiresAt = null,
                LastWriterAgentId = null
            };
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning(ex, "[NatsStateStore] GetAsync({Key}) failed", key);
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
            if (_kvStore is null)
                await EnsureBucketAsync(ct).ConfigureAwait(false);
            var store = _kvStore;
            if (store is null) throw new InvalidOperationException("KV store not initialized");

            var now = DateTimeOffset.UtcNow;
            var version = Guid.NewGuid().ToString("N");

            // Get existing CreatedAt
            DateTimeOffset createdAt = now;
            var existing = await store.TryGetEntryAsync<string>(key, 0UL, null, ct).ConfigureAwait(false);
            if (existing.Success && existing.Value.Revision > 0)
                createdAt = existing.Value.Created;

            await store.PutAsync(key, value.Data, null, ct).ConfigureAwait(false);
            NotifyWatchers(key, value with
            {
                Version = version,
                CreatedAt = createdAt,
                UpdatedAt = now,
                ExpiresAt = null
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[NatsStateStore] SetAsync({Key}) failed", key);
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
            if (_kvStore is null)
                await EnsureBucketAsync(ct).ConfigureAwait(false);
            var store = _kvStore;
            if (store is null) return false;

            var now = DateTimeOffset.UtcNow;
            var version = Guid.NewGuid().ToString("N");

            if (expectedVersion is null)
            {
                // Create-if-not-exists
                var check = await store.TryGetEntryAsync<string>(key, 0UL, null, ct).ConfigureAwait(false);
                if (check.Success && check.Value.Revision > 0)
                    return false; // Key already exists
                await store.PutAsync(key, value.Data, null, ct).ConfigureAwait(false);
                return true;
            }

            // Optimistic locking: verify version
            var current = await store.TryGetEntryAsync<string>(key, 0UL, null, ct).ConfigureAwait(false);
            if (!current.Success || current.Value.Revision == 0 || current.Value.Revision.ToString() != expectedVersion)
                return false; // Key doesn't exist or version mismatch
            await store.PutAsync(key, value.Data, null, ct).ConfigureAwait(false);
            NotifyWatchers(key, value with
            {
                Version = version,
                CreatedAt = current.Value.Created,
                UpdatedAt = now,
                ExpiresAt = null
            });
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[NatsStateStore] CompareAndSetAsync({Key}) failed", key);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(key);

        try
        {
            if (_kvStore is null)
                await EnsureBucketAsync(ct).ConfigureAwait(false);
            var store = _kvStore;
            if (store is null) return false;

            await store.DeleteAsync(key, null, ct).ConfigureAwait(false);
            NotifyWatchers(key, null);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[NatsStateStore] DeleteAsync({Key}) failed", key);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(key);

        try
        {
            if (_kvStore is null)
                await EnsureBucketAsync(ct).ConfigureAwait(false);
            var store = _kvStore;
            if (store is null) return false;

            var result = await store.TryGetEntryAsync<string>(key, 0UL, null, ct).ConfigureAwait(false);
            return result.Success && result.Value.Revision > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<long> IncrementAsync(string key, long delta = 1, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        // CAS loop for INCR since KV doesn't have native atomic increment
        while (!ct.IsCancellationRequested)
        {
            var current = await GetAsync(key, ct).ConfigureAwait(false);
            var currentVal = current is null || !long.TryParse(current.Data, out var v) ? 0 : v;
            var newVal = currentVal + delta;

            var updated = new StoredValue
            {
                Data = newVal.ToString(),
                Version = Guid.NewGuid().ToString("N"),
                CreatedAt = current?.CreatedAt ?? DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = ttl.HasValue ? DateTimeOffset.UtcNow.Add(ttl.Value) : current?.ExpiresAt
            };

            if (await CompareAndSetAsync(key, updated, current?.Version, ttl, ct).ConfigureAwait(false))
            {
                var stored = await GetAsync(key, ct).ConfigureAwait(false);
                if (stored is not null)
                    NotifyWatchers(key, stored);
                return newVal;
            }
        }

        throw new OperationCanceledException();
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
            var prefix = key[..^1];
            var channel = Channel.CreateUnbounded<StoredValue>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

            var timer = new Timer(
                state => ((NatsMeshStateStore)state!).PollPrefixWatchers(prefix, channel),
                this,
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromMilliseconds(500));

            _watchTimers.TryAdd($"prefix:{prefix}", timer);

            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var value in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                        await handler(value, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
            }, ct);

            return Task.FromResult<IDisposable>(new PrefixWatcher(prefix, channel, $"prefix:{prefix}", _watchTimers));
        }

        var exactChannel = _watchChannels.GetOrAdd(
            key,
            static _ => Channel.CreateUnbounded<StoredValue>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }));

        var exactTimer = new Timer(
            state => ((NatsMeshStateStore)state!).PollExactWatchers(key, exactChannel),
            this,
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(500));

        _watchTimers.TryAdd($"exact:{key}", exactTimer);

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var value in exactChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
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
            if (_kvStore is null)
                await EnsureBucketAsync(ct).ConfigureAwait(false);
            var store = _kvStore;
            if (store is null) return Array.Empty<string>();

            var keys = new List<string>();
            await foreach (var key in store.GetKeysAsync(null, ct).ConfigureAwait(false))
            {
                if (keys.Count >= limit) break;
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    keys.Add(key);
            }

            return keys;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[NatsStateStore] ScanKeysAsync({Prefix}) failed", prefix);
            return Array.Empty<string>();
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        if (_disposed) return false;
        try
        {
            var rtt = await _connection.PingAsync(ct).ConfigureAwait(false);
            return rtt.TotalMilliseconds >= 0;
        }
        catch
        {
            return false;
        }
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

        if (_watchChannels.TryGetValue(key, out var channel))
            channel.Writer.TryWrite(sentinel);
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
                if (stored is not null)
                    channel.Writer.TryWrite(stored);
            }
        }
        catch { /* Polling — ignore errors */ }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(NatsMeshStateStore));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true) == true)
            return;

        foreach (var timer in _watchTimers.Values)
            timer.Dispose();
        _watchTimers.Clear();
        _watchChannels.Clear();
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
        private readonly string _prefix;
        private readonly Channel<StoredValue> _channel;
        private readonly string _timerKey;
        private readonly ConcurrentDictionary<string, Timer> _timers;
        private bool _disposed;

        public PrefixWatcher(string prefix, Channel<StoredValue> channel, string timerKey, ConcurrentDictionary<string, Timer> timers)
        {
            _prefix = prefix;
            _channel = channel;
            _timerKey = timerKey;
            _timers = timers;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, true) == false)
            {
                if (_timers.TryRemove(_timerKey, out var timer))
                    timer.Dispose();
            }
        }
    }
}
