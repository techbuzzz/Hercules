using Hercules.WorkflowServer.Models;

namespace Hercules.WorkflowServer.Storage;

/// <summary>
///     Контракт хранилища workflow definitions (task_104).
///     SQLite-реализация — <see cref="SqliteWorkflowDefinitionStore"/>.
/// </summary>
public interface IWorkflowDefinitionStore
{
    /// <summary>Создать или перезаписать definition (idempotent по <paramref name="id"/>, если задан).</summary>
    Task<WorkflowDefinition> SaveAsync(WorkflowDefinition def, CancellationToken ct = default);

    /// <summary>Получить полный definition с GraphJson, или null.</summary>
    Task<WorkflowDefinition?> GetAsync(string id, CancellationToken ct = default);

    /// <summary>Список сводок (без GraphJson — для быстрого листинга).</summary>
    Task<List<WorkflowSummary>> ListAsync(CancellationToken ct = default);

    /// <summary>Удалить definition. Возвращает true, если существовал.</summary>
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}
