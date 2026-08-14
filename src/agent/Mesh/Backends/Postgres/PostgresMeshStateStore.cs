using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Hercules.Mesh.Abstractions;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using PostgresMeshConfig = Hercules.Mesh.Backends.Postgres.PostgresMeshConfig;

namespace Hercules.Mesh.Backends.Postgres;

/// <summary>
///     Distributed state store backed by PostgreSQL (JSONB column + version-based optimistic locking).
///     Schema is auto-created on first use. Watch uses LISTEN/NOTIFY when enabled, polling otherwise.
///     Spec: task_069.
/// </summary>
public sealed class PostgresMeshStateStore : IMeshStateStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly PostgresMeshConfig _config;
    private readonly ILogger<PostgresMeshStateStore> _log;
    private readonly JsonSerializerOptions _json;
    private readonly ConcurrentDictionary<string, Channel<StoredValue?>> _watchChannels = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<Channel<StoredValue?>>> _prefixWatchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Timer> _watchTimers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, NpgsqlConnection?> _listenConnections = new(StringComparer.OrdinalIgnoreCase);
    private int _schemaInitialized;
    private bool _disposed;

    public string BackendKind => "postgres";

    public PostgresMeshStateStore(
        NpgsqlDataSource dataSource,
        PostgresMeshConfig config,
        ILogger<PostgresMeshStateStore> log)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _log = log ?? throw new ArgumentNullException(nameof(log));
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

        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        const string sql = "SELECT data::text, version, created_at, updated_at, expires_at, last_writer_agent_id " +
                           "FROM state WHERE key = @key AND (expires_at IS NULL OR expires_at > NOW())";

        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("key", key);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        return ReadStoredValue(reader);
    }

    /// <inheritdoc />
    public async Task SetAsync(string key, StoredValue value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(key);

        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var version = Guid.NewGuid().ToString("N");
        var expiresAt = ttl.HasValue ? now.Add(ttl.Value) : (DateTimeOffset?)null;

        const string sql = @"
            INSERT INTO state (key, data, version, created_at, updated_at, expires_at, last_writer_agent_id)
            VALUES (@key, @data::jsonb, @version, @createdAt, @updatedAt, @expiresAt, @agentId)
            ON CONFLICT (key) DO UPDATE SET
                data = EXCLUDED.data,
                version = EXCLUDED.version,
                created_at = state.created_at,
                updated_at = EXCLUDED.updated_at,
                expires_at = EXCLUDED.expires_at,
                last_writer_agent_id = EXCLUDED.last_writer_agent_id";

        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using (var cmd = new NpgsqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("key", key);
            cmd.Parameters.AddWithValue("data", value.Data);
            cmd.Parameters.AddWithValue("version", version);
            cmd.Parameters.AddWithValue("createdAt", now);
            cmd.Parameters.AddWithValue("updatedAt", now);
            cmd.Parameters.AddWithValue("expiresAt", (object?)expiresAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("agentId", (object?)value.LastWriterAgentId ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await PublishWatchAsync(key, ct).ConfigureAwait(false);
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

        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var version = Guid.NewGuid().ToString("N");
        var expiresAt = ttl.HasValue ? now.Add(ttl.Value) : (DateTimeOffset?)null;

        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        if (expectedVersion is null)
        {
            // Create-if-not-exists
            const string insertSql = @"
                INSERT INTO state (key, data, version, created_at, updated_at, expires_at, last_writer_agent_id)
                VALUES (@key, @data::jsonb, @version, @createdAt, @updatedAt, @expiresAt, @agentId)
                ON CONFLICT (key) DO NOTHING";

            await using (var cmd = new NpgsqlCommand(insertSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("key", key);
                cmd.Parameters.AddWithValue("data", value.Data);
                cmd.Parameters.AddWithValue("version", version);
                cmd.Parameters.AddWithValue("createdAt", now);
                cmd.Parameters.AddWithValue("updatedAt", now);
                cmd.Parameters.AddWithValue("expiresAt", (object?)expiresAt ?? DBNull.Value);
                cmd.Parameters.AddWithValue("agentId", (object?)value.LastWriterAgentId ?? DBNull.Value);
                var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                if (rows == 0)
                {
                    await tx.RollbackAsync(ct).ConfigureAwait(false);
                    return false;
                }
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
            await PublishWatchAsync(key, ct).ConfigureAwait(false);
            return true;
        }

        // Optimistic CAS: UPDATE ... WHERE version = expected
        const string updateSql = @"
            UPDATE state SET
                data = @data::jsonb,
                version = @version,
                updated_at = @updatedAt,
                expires_at = @expiresAt,
                last_writer_agent_id = @agentId
            WHERE key = @key AND version = @expectedVersion
            RETURNING version";

        bool ok;
        await using (var cmd = new NpgsqlCommand(updateSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("key", key);
            cmd.Parameters.AddWithValue("data", value.Data);
            cmd.Parameters.AddWithValue("version", version);
            cmd.Parameters.AddWithValue("updatedAt", now);
            cmd.Parameters.AddWithValue("expiresAt", (object?)expiresAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("agentId", (object?)value.LastWriterAgentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("expectedVersion", expectedVersion);
            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            ok = result is not null && result is not DBNull;
        }

        if (ok)
        {
            await tx.CommitAsync(ct).ConfigureAwait(false);
            await PublishWatchAsync(key, ct).ConfigureAwait(false);
        }
        else
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
        }

        return ok;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(key);

        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        const string sql = "DELETE FROM state WHERE key = @key";
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("key", key);
        var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        var deleted = rows > 0;
        if (deleted)
            await PublishWatchAsync(key, ct).ConfigureAwait(false);
        return deleted;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(key);

        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        const string sql = "SELECT 1 FROM state WHERE key = @key AND (expires_at IS NULL OR expires_at > NOW())";
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("key", key);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }

    /// <inheritdoc />
    public async Task<long> IncrementAsync(string key, long delta = 1, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(key);

        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        // Use a UPSERT to atomically increment a counter stored as JSONB {"value": N}
        // expires_at is refreshed on every increment when ttl is provided (sliding-window).
        const string sql = @"
            INSERT INTO state (key, data, version, created_at, updated_at, expires_at)
            VALUES (@key, jsonb_build_object('value', @delta), @version, NOW(), NOW(), @expiresAt)
            ON CONFLICT (key) DO UPDATE SET
                data = jsonb_set(state.data, '{value}', (COALESCE((state.data->>'value')::bigint, 0) + @delta)::text::jsonb),
                version = @version,
                updated_at = NOW(),
                expires_at = COALESCE(@expiresAt, state.expires_at)
            RETURNING (data->>'value')::bigint";

        var version = Guid.NewGuid().ToString("N");
        var expiresAt = ttl.HasValue ? (DateTime?)DateTime.UtcNow.Add(ttl.Value) : null;

        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("delta", delta);
        cmd.Parameters.AddWithValue("version", version);
        cmd.Parameters.AddWithValue("expiresAt", (object?)expiresAt ?? DBNull.Value);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        var value = result is long l ? l : Convert.ToInt64(result);

        await PublishWatchAsync(key, ct).ConfigureAwait(false);
        return value;
    }

    /// <inheritdoc />
    public async Task<IDisposable> WatchAsync(
        string key,
        Func<StoredValue, CancellationToken, Task> handler,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(handler);

        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        var isPrefix = key.EndsWith("*");
        if (isPrefix)
        {
            return await WatchPrefixAsync(key[..^1], handler, ct).ConfigureAwait(false);
        }
        return await WatchExactAsync(key, handler, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ScanKeysAsync(
        string prefix,
        int limit = 100,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(prefix);

        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        // Escape LIKE metacharacters: % _ \
        var escaped = prefix
            .Replace(@"\", @"\\")
            .Replace("%", @"\%")
            .Replace("_", @"\_");
        var pattern = escaped + "%";

        const string sql = "SELECT key FROM state WHERE key LIKE @pattern ESCAPE '\\' AND (expires_at IS NULL OR expires_at > NOW()) LIMIT @limit";
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("pattern", pattern);
        cmd.Parameters.AddWithValue("limit", limit);

        var result = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add(reader.GetString(0));
        }
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        if (_disposed) return false;
        try
        {
            await using var conn = await OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand("SELECT 1", conn);
            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return result is not null && result is not DBNull;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     Public helper to build a fully-qualified table name with the configured schema.
    /// </summary>
    public string QualifiedStateTable => $"{QuoteIdent(_config.Schema)}.{QuoteIdent(_config.StateTable)}";

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        return await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
    }

    private async Task EnsureSchemaAsync(CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _schemaInitialized, 1, 0) != 0)
            return;

        if (!_config.AutoCreateSchema)
        {
            Interlocked.Exchange(ref _schemaInitialized, 1);
            return;
        }

        try
        {
            await using var conn = await OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(GetCreateSchemaSql(), conn);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Reset so a retry is possible
            Interlocked.Exchange(ref _schemaInitialized, 0);
            _log.LogWarning(ex, "[PostgresStateStore] Failed to ensure schema");
            throw;
        }
    }

    private string GetCreateSchemaSql()
    {
        var schema = QuoteIdent(_config.Schema);
        var state = QuoteIdent(_config.StateTable);
        return $@"
            CREATE SCHEMA IF NOT EXISTS {schema};
            CREATE TABLE IF NOT EXISTS {schema}.{state} (
                key TEXT PRIMARY KEY,
                data JSONB NOT NULL,
                version TEXT NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                expires_at TIMESTAMPTZ NULL,
                last_writer_agent_id TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_{_config.StateTable}_expires_at
                ON {schema}.{state}(expires_at) WHERE expires_at IS NOT NULL;
        ";
    }

    private static string QuoteIdent(string ident)
    {
        // PostgreSQL identifier quoting: wrap in double quotes, escape embedded double quotes.
        return "\"" + ident.Replace("\"", "\"\"") + "\"";
    }

    private async Task PublishWatchAsync(string key, CancellationToken ct)
    {
        if (!_config.UseListenNotify)
            return;
        try
        {
            var channel = _config.ChannelPrefix + "state_" + SanitizeChannel(key);
            const string sql = "SELECT pg_notify(@channel, @payload)";
            await using var conn = await OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("channel", channel);
            cmd.Parameters.AddWithValue("payload", key);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "[PostgresStateStore] Watch notify failed for {Key}", key);
        }
    }

    private static string SanitizeChannel(string key)
    {
        // PostgreSQL channel name length cap is 63; trim and replace unsafe chars.
        var sb = new StringBuilder(key.Length);
        foreach (var c in key)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(char.ToLowerInvariant(c));
            else
                sb.Append('_');
        }
        if (sb.Length > 50) sb.Length = 50;
        return sb.ToString();
    }

    private async Task<IDisposable> WatchExactAsync(
        string key,
        Func<StoredValue, CancellationToken, Task> handler,
        CancellationToken ct)
    {
        var channel = _watchChannels.GetOrAdd(
            key,
            static _ => Channel.CreateUnbounded<StoredValue?>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }));

        // Drain the channel in a background task
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var value in channel.Reader.ReadAllAsync(ct))
                {
                    if (value is null) continue;
                    try { await handler(value, ct).ConfigureAwait(false); }
                    catch (Exception ex) { _log.LogWarning(ex, "[PostgresStateStore] Watch handler error for {Key}", key); }
                }
            }
            catch (OperationCanceledException) { }
        }, ct);

        if (_config.UseListenNotify)
        {
            await StartListenAsync(key, channel, ct).ConfigureAwait(false);
        }
        else
        {
            StartPollingExact(key, channel);
        }

        return new ExactWatcher(_watchChannels, key);
    }

    private async Task StartListenAsync(string key, Channel<StoredValue?> channel, CancellationToken ct)
    {
        var channelName = _config.ChannelPrefix + "state_" + SanitizeChannel(key);
        if (_listenConnections.ContainsKey(channelName))
            return;

        try
        {
            var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
            conn.Notification += (_, e) =>
            {
                if (e.Payload == key)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var stored = await GetAsync(key, CancellationToken.None).ConfigureAwait(false);
                            channel.Writer.TryWrite(stored);
                        }
                        catch { /* swallow */ }
                    });
                }
            };
            await using (var cmd = new NpgsqlCommand($"LISTEN {QuoteIdent(channelName)}", conn))
            {
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            _listenConnections[channelName] = conn;
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_disposed && conn.State == System.Data.ConnectionState.Open)
                    {
                        await conn.WaitAsync(ct).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception) { }
            }, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[PostgresStateStore] LISTEN failed for {Key}, falling back to polling", key);
            StartPollingExact(key, channel);
        }
    }

    private void StartPollingExact(string key, Channel<StoredValue?> channel)
    {
        var timerKey = "exact:" + key;
        if (_watchTimers.ContainsKey(timerKey)) return;

        // task_077: timer callback is sync void; it re-enters via fire-and-forget async helper.
        var timer = new Timer(
            state =>
            {
                var (k, ch) = ((string, Channel<StoredValue?>))state!;
                _ = PollExactAsync(k, ch);
            },
            (key, channel),
            TimeSpan.FromMilliseconds(_config.WatchPollingIntervalMs),
            TimeSpan.FromMilliseconds(_config.WatchPollingIntervalMs));

        _watchTimers.TryAdd(timerKey, timer);
    }

    private async Task PollExactAsync(string key, Channel<StoredValue?> channel)
    {
        try
        {
            var stored = await GetAsync(key, CancellationToken.None).ConfigureAwait(false);
            channel.Writer.TryWrite(stored);
        }
        catch { /* swallow */ }
    }

    private async Task<IDisposable> WatchPrefixAsync(
        string prefix,
        Func<StoredValue, CancellationToken, Task> handler,
        CancellationToken ct)
    {
        var watchers = _prefixWatchers.GetOrAdd(prefix, _ => new List<Channel<StoredValue?>>());
        var channel = Channel.CreateUnbounded<StoredValue?>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

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
                    if (value is null) continue;
                    try { await handler(value, ct).ConfigureAwait(false); }
                    catch (Exception ex) { _log.LogWarning(ex, "[PostgresStateStore] Prefix watch handler error for {Prefix}", prefix); }
                }
            }
            catch (OperationCanceledException) { }
        }, ct);

        // Periodically scan matching keys and emit them
        var timerKey = "prefix:" + prefix;
        // task_077: timer callback is sync void; it re-enters via fire-and-forget async helper.
        var timer = new Timer(
            state =>
            {
                var (p, ch) = ((string, Channel<StoredValue?>))state!;
                _ = PollPrefixAsync(p, ch);
            },
            (prefix, channel),
            TimeSpan.FromMilliseconds(_config.WatchPollingIntervalMs),
            TimeSpan.FromMilliseconds(_config.WatchPollingIntervalMs));
        _watchTimers.TryAdd(timerKey, timer);

        return new PrefixWatcher(watchers, channel, timerKey, _watchTimers);
    }

    private async Task PollPrefixAsync(string prefix, Channel<StoredValue?> channel)
    {
        try
        {
            var keys = await ScanKeysAsync(prefix, 100, CancellationToken.None).ConfigureAwait(false);
            foreach (var k in keys)
            {
                var stored = await GetAsync(k, CancellationToken.None).ConfigureAwait(false);
                if (stored is not null)
                    channel.Writer.TryWrite(stored);
            }
        }
        catch { /* swallow */ }
    }

    private static StoredValue? ReadStoredValue(NpgsqlDataReader reader)
    {
        var data = reader.GetString(0);
        var version = reader.GetString(1);
        var createdAt = reader.GetFieldValue<DateTime>(2);
        var updatedAt = reader.GetFieldValue<DateTime>(3);
        DateTime? expiresAt = reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTime>(4);
        var lastWriterAgentId = reader.IsDBNull(5) ? null : reader.GetString(5);

        return new StoredValue
        {
            Data = data,
            Version = version,
            CreatedAt = new DateTimeOffset(createdAt, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(updatedAt, TimeSpan.Zero),
            ExpiresAt = expiresAt.HasValue ? new DateTimeOffset(expiresAt.Value, TimeSpan.Zero) : null,
            LastWriterAgentId = lastWriterAgentId
        };
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PostgresMeshStateStore));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true) == true)
            return;

        foreach (var timer in _watchTimers.Values)
            timer.Dispose();
        _watchTimers.Clear();

        foreach (var conn in _listenConnections.Values)
        {
            try { conn?.Dispose(); } catch { /* ignore */ }
        }
        _listenConnections.Clear();

        _watchChannels.Clear();
        _prefixWatchers.Clear();
    }

    private sealed class ExactWatcher : IDisposable
    {
        private readonly ConcurrentDictionary<string, Channel<StoredValue?>> _channels;
        private readonly string _key;
        private bool _disposed;

        public ExactWatcher(ConcurrentDictionary<string, Channel<StoredValue?>> channels, string key)
        {
            _channels = channels;
            _key = key;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, true)) return;
            _channels.TryRemove(_key, out _);
        }
    }

    private sealed class PrefixWatcher : IDisposable
    {
        private readonly List<Channel<StoredValue?>> _watchers;
        private readonly Channel<StoredValue?> _channel;
        private readonly string _timerKey;
        private readonly ConcurrentDictionary<string, Timer> _timers;
        private bool _disposed;

        public PrefixWatcher(List<Channel<StoredValue?>> watchers, Channel<StoredValue?> channel, string timerKey, ConcurrentDictionary<string, Timer> timers)
        {
            _watchers = watchers;
            _channel = channel;
            _timerKey = timerKey;
            _timers = timers;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, true)) return;
            lock (_watchers) { _watchers.Remove(_channel); }
            if (_timers.TryRemove(_timerKey, out var t)) t.Dispose();
        }
    }
}
