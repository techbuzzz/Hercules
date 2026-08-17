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
public sealed class SqliteWorkflowDefinitionStore : IWorkflowDefinitionStore, IAsyncDisposable, IDisposable
{
    private const string DatabaseFileName = "workflow-server.db";
    private readonly SqliteConnection _conn;
    private readonly SemaphoreSlim _connLock = new(1, 1);
    private int _disposed;

    public SqliteWorkflowDefinitionStore(string dataRoot)
    {
        Directory.CreateDirectory(dataRoot);
        var dbPath = Path.Combine(dataRoot, DatabaseFileName);
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        _conn = new SqliteConnection(connStr);
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
        catch
        {
            // WAL is best-effort; falls back to default journal.
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
    public bool IsHealthy()
    {
        if (Volatile.Read(ref _disposed) != 0) return false;
        if (!_connLock.Wait(0)) return false;
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            return cmd.ExecuteScalar() is not null;
        }
        catch
        {
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
        await _connLock.WaitAsync().ConfigureAwait(false);
        try { _conn.Dispose(); }
        finally { _connLock.Release(); _connLock.Dispose(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _connLock.Wait();
        try { _conn.Dispose(); }
        finally { _connLock.Release(); _connLock.Dispose(); }
    }
}
