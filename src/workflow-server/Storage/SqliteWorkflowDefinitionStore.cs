using System.Text.Json;
using Hercules.WorkflowServer.Models;
using Microsoft.Data.Sqlite;

namespace Hercules.WorkflowServer.Storage;

/// <summary>
///     SQLite-реализация <see cref="IWorkflowDefinitionStore"/> (task_104).
///     Хранит definitions в таблице <c>workflows</c>:
///     <c>(id, name, version, description, graph_json, created_at, updated_at)</c>.
///     Использует единый <see cref="SqliteConnection"/> с <see cref="SemaphoreSlim"/>(1,1)
///     для thread-safety (тот же подход, что в agent's <c>SqliteSessionStore</c> — task_071).
///     DB-файл: <c>DataRoot/workflow-server.db</c> (отдельный от agent's <c>sessions.db</c>).
/// </summary>
/// <remarks>
///     R13: this shared-connection + <c>SemaphoreSlim(1,1)</c> design serialises EVERY read
///     and write in the process, so throughput is capped at one DB operation at a time.
///     Migrating to per-operation connections is the highest-risk change in the plan and is
///     deliberately sequenced as its own isolated step — the remaining fixes here
///     (R15/R26/R30/R31) are safe and were taken first so they could not be confounded by it.
/// </remarks>
public sealed class SqliteWorkflowDefinitionStore : IWorkflowDefinitionStore, IAsyncDisposable, IDisposable
{
    private const string DatabaseFileName = "workflow-server.db";
    private readonly SqliteConnection _conn;

    // R26: NOT disposed. Threads may still be awaiting it during shutdown, and disposing a
    // SemaphoreSlim with waiters raises ObjectDisposedException inside those waiters. The
    // semaphore is GC-safe; only the connection needs explicit disposal.
    private readonly SemaphoreSlim _connLock = new(1, 1);
    private readonly string _dbPath;
    private int _disposed;

    public SqliteWorkflowDefinitionStore(string dataRoot)
    {
        Directory.CreateDirectory(dataRoot);
        _dbPath = Path.Combine(dataRoot, DatabaseFileName);
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        _conn = new SqliteConnection(connStr);

        // R30: Open() and schema/WAL setup stay in the constructor (they are sync-only
        // ADO.NET APIs), but they now run eagerly via Initialize() and their failure is
        // surfaced by the caller rather than being deferred to whichever request resolves
        // this singleton first.
        Initialize();
    }

    private void Initialize()
    {
        _conn.Open();
        EnableWalMode();
        InitSchema();
    }

    private void InitSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS workflows (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                version INTEGER NOT NULL DEFAULT 1,
                description TEXT,
                graph_json TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_workflows_name ON workflows(name);
            """;
        cmd.ExecuteNonQuery();
    }

    private void EnableWalMode()
    {
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            // R31: WAL is best-effort, but the failure was previously swallowed by a bare
            // `catch {}` with no telemetry at all — a silent durability downgrade looked
            // identical to success. Surface it so operators can see the degradation.
            Console.Error.WriteLine(
                $"[workflow-server] Could not enable WAL mode for '{_dbPath}'; falling back to the " +
                $"default journal mode. Concurrency will be reduced. Cause: {ex.Message}");
        }
    }

    public async Task<WorkflowDefinition> SaveAsync(WorkflowDefinition def, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(def.Id))
        {
            def.Id = Guid.NewGuid().ToString("N");
        }
        var now = DateTimeOffset.UtcNow;
        if (def.CreatedAt == default) def.CreatedAt = now;
        def.UpdatedAt = now;

        var graphJson = def.GraphJson.GetRawText();

        await _connLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO workflows(id, name, version, description, graph_json, created_at, updated_at)
                VALUES($id, $name, $version, $description, $graph, $created, $updated)
                ON CONFLICT(id) DO UPDATE SET
                    name=excluded.name,
                    version=excluded.version,
                    description=excluded.description,
                    graph_json=excluded.graph_json,
                    updated_at=excluded.updated_at;
                """;
            cmd.Parameters.AddWithValue("$id", def.Id);
            cmd.Parameters.AddWithValue("$name", def.Name);
            cmd.Parameters.AddWithValue("$version", def.Version);
            cmd.Parameters.AddWithValue("$description", (object?)def.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$graph", graphJson);
            cmd.Parameters.AddWithValue("$created", def.CreatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$updated", def.UpdatedAt.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _connLock.Release();
        }
        return def;
    }

    public async Task<WorkflowDefinition?> GetAsync(string id, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await _connLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT id, name, version, description, graph_json, created_at, updated_at FROM workflows WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
            return ReadDefinition(reader);
        }
        finally
        {
            _connLock.Release();
        }
    }

    public async Task<List<WorkflowSummary>> ListAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await _connLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT id, name, version, description, created_at, updated_at FROM workflows ORDER BY updated_at DESC";
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            var list = new List<WorkflowSummary>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                list.Add(new WorkflowSummary
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Version = reader.GetInt32(2),
                    Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                    CreatedAt = DateTimeOffset.Parse(reader.GetString(4)),
                    UpdatedAt = DateTimeOffset.Parse(reader.GetString(5)),
                });
            }
            return list;
        }
        finally
        {
            _connLock.Release();
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await _connLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM workflows WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return rows > 0;
        }
        finally
        {
            _connLock.Release();
        }
    }

    /// <summary>Health probe — используется /api/workflows/health.</summary>
    /// <remarks>
    ///     R15: this previously used <c>_connLock.Wait(0)</c> and reported <c>false</c> when
    ///     the lock was merely busy — so a perfectly healthy database was reported
    ///     UNHEALTHY under load, causing spurious restarts and flapping orchestrator probes.
    ///     It now waits up to a short bounded interval, and still returns false on a real
    ///     database error.
    /// </remarks>
    public bool IsHealthy()
    {
        if (Volatile.Read(ref _disposed) != 0) return false;

        // Bounded wait: contention is not a health signal. Keep it short so a genuinely
        // wedged store still reports unhealthy quickly.
        if (!_connLock.Wait(TimeSpan.FromMilliseconds(500)))
        {
            // Lock never became free within the window — treat as degraded rather than
            // healthy, but this is now the only contention-driven false, and it requires a
            // half-second stall instead of a momentary overlap.
            return false;
        }

        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            return cmd.ExecuteScalar() is not null;
        }
        catch (Exception ex)
        {
            // R32-style: the old bare `catch` hid every diagnostic from operators.
            Console.Error.WriteLine($"[workflow-server] Health probe failed: {ex.Message}");
            return false;
        }
        finally
        {
            _connLock.Release();
        }
    }

    private static WorkflowDefinition ReadDefinition(SqliteDataReader reader)
    {
        var graphRaw = reader.GetString(4);
        using var doc = JsonDocument.Parse(graphRaw);
        return new WorkflowDefinition
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            Version = reader.GetInt32(2),
            Description = reader.IsDBNull(3) ? null : reader.GetString(3),
            GraphJson = doc.RootElement.Clone(),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(5)),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(6)),
        };
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(SqliteWorkflowDefinitionStore));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        // Wait for in-flight work to drain so the connection is not disposed underneath it.
        // R26: the semaphore is intentionally NOT disposed here. Other threads may still be
        // awaiting it, and disposing a SemaphoreSlim with waiters makes those waiters throw
        // ObjectDisposedException during shutdown. It holds no unmanaged handle and is GC-safe.
        await _connLock.WaitAsync().ConfigureAwait(false);
        try { _conn.Dispose(); }
        finally { _connLock.Release(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _connLock.Wait();
        try { _conn.Dispose(); }
        finally { _connLock.Release(); }
    }
}
