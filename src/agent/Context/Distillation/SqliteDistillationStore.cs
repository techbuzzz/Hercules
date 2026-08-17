using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Hercules.Context.Distillation;

/// <summary>
///     SQLite-backed implementation of <see cref="IDistillationStore"/> (task_102).
///     Co-locates distillation tables with the rest of agent state via shared
///     <see cref="Hercules.Storage.SqliteSessionStore"/> connection.
///     <para>
///         Uses a private <see cref="SemaphoreSlim"/> to serialise commands on the
///         shared connection (same pattern as <c>SqliteOutboxStore</c>).
///     </para>
/// </summary>
public sealed class SqliteDistillationStore : IDistillationStore
{
    private readonly SqliteConnection _conn;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<SqliteDistillationStore> _log;
    private int _disposed;

    public SqliteDistillationStore(
        Hercules.Storage.SqliteSessionStore store,
        ILogger<SqliteDistillationStore> log)
    {
        _conn = store.Connection;
        _log = log;
        InitSchema();
    }

    private void InitSchema()
    {
        const string ddl = """
            CREATE TABLE IF NOT EXISTS context_summaries (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id      TEXT NOT NULL,
                from_index      INTEGER NOT NULL,
                to_index        INTEGER NOT NULL,
                summary         TEXT NOT NULL,
                message_count   INTEGER NOT NULL,
                token_estimate  INTEGER NOT NULL DEFAULT 0,
                created_at      TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_cs_session ON context_summaries(session_id, from_index);

            CREATE TABLE IF NOT EXISTS context_key_facts (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id      TEXT NOT NULL,
                fact_text       TEXT NOT NULL,
                score           REAL NOT NULL,
                source_count    INTEGER NOT NULL,
                first_seen      TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_ckf_session_fact
                ON context_key_facts(session_id, fact_text);
            CREATE INDEX IF NOT EXISTS ix_ckf_session_score
                ON context_key_facts(session_id, score DESC);
            """;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = ddl;
        cmd.ExecuteNonQuery();
    }

    public async Task<long> SaveSummaryAsync(DistillationSummary summary, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await _lock.WaitAsync(ct);
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO context_summaries
                    (session_id, from_index, to_index, summary, message_count, token_estimate, created_at)
                VALUES ($s, $f, $t, $sm, $mc, $te, $c)
                RETURNING id;
                """;
            cmd.Parameters.AddWithValue("$s", summary.SessionId);
            cmd.Parameters.AddWithValue("$f", summary.FromIndex);
            cmd.Parameters.AddWithValue("$t", summary.ToIndex);
            cmd.Parameters.AddWithValue("$sm", summary.Summary);
            cmd.Parameters.AddWithValue("$mc", summary.MessageCount);
            cmd.Parameters.AddWithValue("$te", summary.TokenEstimate);
            cmd.Parameters.AddWithValue("$c", summary.CreatedAt.ToString("o"));
            var result = await cmd.ExecuteScalarAsync(ct);
            var id = Convert.ToInt64(result ?? 0L);
            _log.LogDebug("[DistillationStore] Saved summary id={Id} session={Session} range=[{From}..{To}] tokens={Tokens}",
                id, summary.SessionId, summary.FromIndex, summary.ToIndex, summary.TokenEstimate);
            return id;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<DistillationSummary>> GetSummariesAsync(string sessionId, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await _lock.WaitAsync(ct);
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, session_id, from_index, to_index, summary, message_count, token_estimate, created_at
                FROM context_summaries
                WHERE session_id = $s
                ORDER BY from_index ASC
                """;
            cmd.Parameters.AddWithValue("$s", sessionId);
            using var reader = await cmd.ExecuteReaderAsync(ct);
            var list = new List<DistillationSummary>();
            while (await reader.ReadAsync(ct))
            {
                list.Add(new DistillationSummary(
                    Id: reader.GetInt64(0),
                    SessionId: reader.GetString(1),
                    FromIndex: reader.GetInt32(2),
                    ToIndex: reader.GetInt32(3),
                    Summary: reader.GetString(4),
                    MessageCount: reader.GetInt32(5),
                    TokenEstimate: reader.GetInt32(6),
                    CreatedAt: DateTime.Parse(reader.GetString(7), null, System.Globalization.DateTimeStyles.RoundtripKind)));
            }
            return list;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteSummariesAsync(string sessionId, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await _lock.WaitAsync(ct);
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM context_summaries WHERE session_id = $s";
            cmd.Parameters.AddWithValue("$s", sessionId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveKeyFactsAsync(string sessionId, IReadOnlyList<KeyFact> facts, CancellationToken ct = default)
    {
        if (facts.Count == 0) return;
        ThrowIfDisposed();
        await _lock.WaitAsync(ct);
        try
        {
            // upsert: insert or update score/source_count/first_seen by (session_id, fact_text)
            using var tx = _conn.BeginTransaction();
            using var cmd = _conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO context_key_facts (session_id, fact_text, score, source_count, first_seen)
                VALUES ($s, $t, $sc, $c, $f)
                ON CONFLICT(session_id, fact_text) DO UPDATE SET
                    score = excluded.score,
                    source_count = excluded.source_count,
                    first_seen = MIN(first_seen, excluded.first_seen);
                """;
            var pS = cmd.Parameters.Add("$s", SqliteType.Text);
            var pT = cmd.Parameters.Add("$t", SqliteType.Text);
            var pSc = cmd.Parameters.Add("$sc", SqliteType.Real);
            var pC = cmd.Parameters.Add("$c", SqliteType.Integer);
            var pF = cmd.Parameters.Add("$f", SqliteType.Text);
            foreach (var fact in facts)
            {
                pS.Value = sessionId;
                pT.Value = fact.FactText;
                pSc.Value = fact.Score;
                pC.Value = fact.SourceCount;
                pF.Value = fact.FirstSeen.ToString("o");
                await cmd.ExecuteNonQueryAsync(ct);
            }
            tx.Commit();
            _log.LogDebug("[DistillationStore] Upserted {Count} key facts for session {Session}", facts.Count, sessionId);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<KeyFact>> GetKeyFactsAsync(string sessionId, int limit, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await _lock.WaitAsync(ct);
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                SELECT fact_text, score, source_count, first_seen
                FROM context_key_facts
                WHERE session_id = $s
                ORDER BY score DESC
                LIMIT $l
                """;
            cmd.Parameters.AddWithValue("$s", sessionId);
            cmd.Parameters.AddWithValue("$l", Math.Max(1, limit));
            using var reader = await cmd.ExecuteReaderAsync(ct);
            var list = new List<KeyFact>();
            while (await reader.ReadAsync(ct))
            {
                list.Add(new KeyFact(
                    FactText: reader.GetString(0),
                    Score: reader.GetDouble(1),
                    SourceCount: reader.GetInt32(2),
                    FirstSeen: DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind)));
            }
            return list;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteKeyFactsAsync(string sessionId, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await _lock.WaitAsync(ct);
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM context_key_facts WHERE session_id = $s";
            cmd.Parameters.AddWithValue("$s", sessionId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lock.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(SqliteDistillationStore));
    }
}
