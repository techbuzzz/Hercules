using Hercules.Storage;

namespace Hercules.Tasks;

/// <summary>
///     SQLite-backed implementation of <see cref="ITaskRepository"/>.
/// </summary>
public sealed class SqliteTaskRepository : ITaskRepository
{
    private readonly SqliteSessionStore _store;

    public SqliteTaskRepository(SqliteSessionStore store)
    {
        _store = store;
    }

    public Task<DurableTask> CreateAsync(DurableTask task, CancellationToken ct = default)
    {
        return _store.SaveDurableTaskAsync(task, ct).ContinueWith(_ => task, ct)!;
    }

    public async Task<DurableTask?> GetAsync(TaskId taskId, CancellationToken ct = default)
    {
        return await _store.LoadDurableTaskAsync(taskId.ToString(), ct);
    }

    public Task UpdateAsync(DurableTask task, CancellationToken ct = default)
    {
        return _store.SaveDurableTaskAsync(task, ct);
    }

    public Task DeleteAsync(TaskId taskId, CancellationToken ct = default)
    {
        return _store.DeleteDurableTaskAsync(taskId.ToString(), ct);
    }

    public Task<List<DurableTask>> ListAsync(
        DurableTaskStatus? statusFilter = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        return _store.ListDurableTasksAsync(statusFilter, limit, ct);
    }
}
