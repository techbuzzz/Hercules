using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Hercules.Mesh;
using Hercules.Mesh.Abstractions;
using Microsoft.Extensions.Logging;
using Npgsql;
using PostgresMeshConfig = Hercules.Mesh.Backends.Postgres.PostgresMeshConfig;

namespace Hercules.Mesh.Backends.Postgres;

/// <summary>
///     Pub/sub bus for inter-agent messages backed by PostgreSQL LISTEN/NOTIFY.
///     Publish writes a payload to a side-table and emits <c>NOTIFY hercules_bus_{topic}</c>.
///     Subscribers LISTEN on the same channel name and read the most recent payload from the table
///     when a notification arrives. Request/reply uses a dedicated <c>replies_{correlationId}</c> channel
///     with TaskCompletionSource for synchronous waiting.
///     Spec: task_069.
/// </summary>
public sealed class PostgresMeshBus : IMeshBus
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly PostgresMeshConfig _config;
    private readonly ILogger<PostgresMeshBus> _log;
    private readonly JsonSerializerOptions _json;
    private readonly ConcurrentDictionary<string, List<Func<IntentEnvelope, CancellationToken, Task>>> _localSubscribers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IntentResponse>> _pendingReplies = new();
    private readonly ConcurrentDictionary<string, NpgsqlConnection?> _listenConnections = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _listenRefCount = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _listenLock = new();
    private readonly Timer _replyCleanupTimer;
    private bool _disposed;
    private int _schemaInitialized;

    public string BackendKind => "postgres";

    public PostgresMeshBus(
        NpgsqlDataSource dataSource,
        PostgresMeshConfig config,
        ILogger<PostgresMeshBus> log)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        _replyCleanupTimer = new Timer(
            static state => ((PostgresMeshBus)state!).CleanupStaleReplies(),
            this,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(10));
    }

    /// <inheritdoc />
    public async Task PublishAsync(string topic, IntentEnvelope envelope, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(topic);

        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        var channelName = ChannelForTopic(topic);
        var json = envelope.ToJson();

        // Persist payload for late subscribers to read on notification.
        const string upsertSql = @"
            INSERT INTO messages (channel, payload, sent_at)
            VALUES (@channel, @payload::jsonb, NOW())
            ON CONFLICT (channel) DO UPDATE SET
                payload = EXCLUDED.payload,
                sent_at = NOW()";

        await using (var conn = await OpenAsync(ct).ConfigureAwait(false))
        await using (var cmd = new NpgsqlCommand(upsertSql, conn))
        {
            cmd.Parameters.AddWithValue("channel", channelName);
            cmd.Parameters.AddWithValue("payload", json);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // Fire-and-forget NOTIFY
        const string notifySql = "SELECT pg_notify(@channel, @payload)";
        await using (var conn2 = await OpenAsync(ct).ConfigureAwait(false))
        await using (var cmd2 = new NpgsqlCommand(notifySql, conn2))
        {
            cmd2.Parameters.AddWithValue("channel", channelName);
            cmd2.Parameters.AddWithValue("payload", "");
            await cmd2.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // Always deliver to local in-process subscribers (covers co-hosted agents)
        DeliverLocal(topic, envelope, ct).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async Task<IDisposable> SubscribeAsync(
        string topic,
        Func<IntentEnvelope, CancellationToken, Task> handler,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(topic);
        ArgumentNullException.ThrowIfNull(handler);

        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        var subscribers = _localSubscribers.GetOrAdd(topic, static _ => new List<Func<IntentEnvelope, CancellationToken, Task>>());
        lock (subscribers)
        {
            subscribers.Add(handler);
        }

        var channelName = ChannelForTopic(topic);
        await EnsureListeningAsync(channelName, ct).ConfigureAwait(false);

        return new Subscription(() =>
        {
            lock (subscribers)
            {
                subscribers.Remove(handler);
            }
            DecrementListenRef(channelName);
        });
    }

    /// <inheritdoc />
    public async Task<IntentResponse> RequestReplyAsync(
        string targetAgentId,
        IntentEnvelope envelope,
        CancellationToken ct = default,
        TimeSpan? defaultTimeout = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(envelope);

        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        var correlationId = envelope.RequestId;
        if (string.IsNullOrEmpty(correlationId))
            correlationId = Guid.NewGuid().ToString("N");

        var replyChannel = "reply_" + SanitizeChannel(correlationId);
        var tcs = new TaskCompletionSource<IntentResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingReplies[correlationId] = tcs;

        try
        {
            await EnsureListeningAsync(replyChannel, ct).ConfigureAwait(false);

            // Publish the request to target's channel
            var requestChannel = ChannelForTopic($"agent.{targetAgentId}");
            const string upsertSql = @"
                INSERT INTO messages (channel, payload, sent_at)
                VALUES (@channel, @payload::jsonb, NOW())
                ON CONFLICT (channel) DO UPDATE SET
                    payload = EXCLUDED.payload,
                    sent_at = NOW()";
            await using (var conn = await OpenAsync(ct).ConfigureAwait(false))
            await using (var cmd = new NpgsqlCommand(upsertSql, conn))
            {
                cmd.Parameters.AddWithValue("channel", requestChannel);
                cmd.Parameters.AddWithValue("payload", envelope.ToJson());
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            const string notifySql = "SELECT pg_notify(@channel, '')";
            await using (var conn2 = await OpenAsync(ct).ConfigureAwait(false))
            await using (var cmd2 = new NpgsqlCommand(notifySql, conn2))
            {
                cmd2.Parameters.AddWithValue("channel", requestChannel);
                await cmd2.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            // Wait for reply
            var timeout = defaultTimeout ?? TimeSpan.FromSeconds(30);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            using var registration = cts.Token.Register(() => tcs.TrySetCanceled(cts.Token));

            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pendingReplies.TryRemove(correlationId, out _);
            DecrementListenRef(replyChannel);
        }
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

    private async Task DeliverLocal(string topic, IntentEnvelope envelope, CancellationToken ct)
    {
        if (!_localSubscribers.TryGetValue(topic, out var subscribers)) return;
        List<Func<IntentEnvelope, CancellationToken, Task>> snapshot;
        lock (subscribers) { snapshot = subscribers.ToList(); }
        foreach (var handler in snapshot)
        {
            try { await handler(envelope, ct).ConfigureAwait(false); }
            catch (Exception ex) { _log.LogWarning(ex, "[PostgresMeshBus] Local subscriber error for {Topic}", topic); }
        }
    }

    private async Task EnsureListeningAsync(string channelName, CancellationToken ct)
    {
        lock (_listenLock)
        {
            _listenRefCount.AddOrUpdate(channelName, 1, (_, current) => current + 1);
            if (_listenConnections.ContainsKey(channelName)) return;
        }

        try
        {
            var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
            conn.Notification += async (_, e) =>
            {
                if (!string.Equals(e.Channel, channelName, StringComparison.OrdinalIgnoreCase))
                    return;

                // Read the latest payload for the channel
                try
                {
                    var payload = await ReadLatestPayloadAsync(channelName, CancellationToken.None).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(payload)) return;

                    var envelope = JsonSerializer.Deserialize<IntentEnvelope>(payload, _json);
                    if (envelope is null) return;

                    // Reply path
                    if (channelName.StartsWith("reply_", StringComparison.OrdinalIgnoreCase))
                    {
                        var reply = JsonSerializer.Deserialize<IntentResponse>(payload, _json);
                        if (reply is null) return;
                        var corrId = channelName["reply_".Length..];
                        if (_pendingReplies.TryGetValue(corrId, out var tcs))
                            tcs.TrySetResult(reply);
                    }
                    else
                    {
                        // Strip prefix to recover the user-facing topic for handler dispatch
                        var topic = channelName.StartsWith(_config.ChannelPrefix, StringComparison.OrdinalIgnoreCase)
                            ? channelName[_config.ChannelPrefix.Length..]
                            : channelName;
                        await DeliverLocal(topic, envelope, CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "[PostgresMeshBus] Notification handler error for {Channel}", channelName);
                }
            };

            await using (var cmd = new NpgsqlCommand($"LISTEN {QuoteIdent(channelName)}", conn))
            {
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            lock (_listenLock) { _listenConnections[channelName] = conn; }

            // Background Wait loop
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
            _log.LogWarning(ex, "[PostgresMeshBus] LISTEN failed for {Channel}", channelName);
        }
    }

    private void DecrementListenRef(string channelName)
    {
        lock (_listenLock)
        {
            if (_listenRefCount.AddOrUpdate(channelName, 0, (_, current) => Math.Max(0, current - 1)) == 0)
            {
                if (_listenConnections.TryRemove(channelName, out var conn))
                {
                    try { conn?.Dispose(); } catch { /* ignore */ }
                }
            }
        }
    }

    private async Task<string?> ReadLatestPayloadAsync(string channelName, CancellationToken ct)
    {
        const string sql = "SELECT payload::text FROM messages WHERE channel = @channel";
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("channel", channelName);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is null || result is DBNull ? null : (string)result;
    }

    private void CleanupStaleReplies()
    {
        if (_disposed) return;
        // TCS that have been around > 5 minutes are presumed stuck and removed
        // to avoid unbounded growth. Active waiters own their own cancellation.
        var staleThreshold = DateTimeOffset.UtcNow.AddMinutes(-5);
        foreach (var kvp in _pendingReplies)
        {
            if (kvp.Value.Task.IsCompleted && kvp.Value.Task.Status == TaskStatus.RanToCompletion
                && (kvp.Value.Task.Result is null || DateTimeOffset.UtcNow - staleThreshold > TimeSpan.Zero))
            {
                _pendingReplies.TryRemove(kvp.Key, out _);
            }
        }
    }

    private string ChannelForTopic(string topic) =>
        _config.ChannelPrefix + SanitizeChannel(topic);

    private static string SanitizeChannel(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '.')
                sb.Append(char.ToLowerInvariant(c));
            else
                sb.Append('_');
        }
        if (sb.Length > 50) sb.Length = 50;
        return sb.ToString();
    }

    private static string QuoteIdent(string ident) =>
        "\"" + ident.Replace("\"", "\"\"") + "\"";

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct) =>
        await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);

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
            Interlocked.Exchange(ref _schemaInitialized, 0);
            _log.LogWarning(ex, "[PostgresMeshBus] Failed to ensure schema");
            throw;
        }
    }

    private string GetCreateSchemaSql()
    {
        var schema = QuoteIdent(_config.Schema);
        return $@"
            CREATE SCHEMA IF NOT EXISTS {schema};
            CREATE TABLE IF NOT EXISTS {schema}.messages (
                channel TEXT PRIMARY KEY,
                payload JSONB NOT NULL,
                sent_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
        ";
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PostgresMeshBus));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true) == true)
            return;
        _replyCleanupTimer.Dispose();

        foreach (var conn in _listenConnections.Values)
        {
            try { conn?.Dispose(); } catch { /* ignore */ }
        }
        _listenConnections.Clear();
        _listenRefCount.Clear();
        _localSubscribers.Clear();
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action _onDispose;
        private bool _disposed;

        public Subscription(Action onDispose) { _onDispose = onDispose; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, true)) return;
            _onDispose();
        }
    }
}
