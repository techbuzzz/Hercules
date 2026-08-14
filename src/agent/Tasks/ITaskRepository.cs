namespace Hercules.Tasks;

/// <summary>
///     Персистентное хранилище durable задач.
/// </summary>
public interface ITaskRepository
{
    /// <summary>Создать новую задачу. Если taskId уже существует — обновляет.</summary>
    Task<DurableTask> CreateAsync(DurableTask task, CancellationToken ct = default);

    /// <summary>Загрузить задачу по ID.</summary>
    Task<DurableTask?> GetAsync(TaskId taskId, CancellationToken ct = default);

    /// <summary>Обновить состояние задачи.</summary>
    Task UpdateAsync(DurableTask task, CancellationToken ct = default);

    /// <summary>Удалить задачу.</summary>
    Task DeleteAsync(TaskId taskId, CancellationToken ct = default);

    /// <summary>
    ///     Список задач с опциональным фильтром по статусу.
    /// </summary>
    Task<List<DurableTask>> ListAsync(
        DurableTaskStatus? statusFilter = null,
        int limit = 100,
        CancellationToken ct = default);
}
