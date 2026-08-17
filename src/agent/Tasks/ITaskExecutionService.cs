namespace Hercules.Tasks;

/// <summary>
///     Orchestrates durable task lifecycle: create, resume, pause, cancel, status.
///     Long-running tasks go through this service; synchronous chat requests bypass it.
/// </summary>
public interface ITaskExecutionService
{
    /// <summary>Create and start a durable task. Returns TaskId.</summary>
    Task<TaskId> StartAsync(CreateTaskRequest request, CancellationToken ct = default);

    /// <summary>Resume a paused task. Optionally from a specific checkpoint.</summary>
    Task ResumeAsync(TaskId taskId, string? checkpointId = null, CancellationToken ct = default);

    /// <summary>Pause a running task.</summary>
    Task PauseAsync(TaskId taskId, CancellationToken ct = default);

    /// <summary>Cancel a task.</summary>
    Task CancelAsync(TaskId taskId, string? reason = null, CancellationToken ct = default);

    /// <summary>Get current task status.</summary>
    Task<DurableTask?> GetStatusAsync(TaskId taskId, CancellationToken ct = default);

    /// <summary>Create a checkpoint for a running task. Returns the checkpoint ID.</summary>
    Task<string> CreateCheckpointAsync(TaskId taskId, int stepNumber, string stateSnapshot, CancellationToken ct = default);

    /// <summary>List checkpoints for a task, ordered by step number ascending.</summary>
    Task<List<TaskCheckpoint>> ListCheckpointsAsync(TaskId taskId, CancellationToken ct = default);

    /// <summary>Load a single checkpoint by ID. Returns null if not found.</summary>
    Task<TaskCheckpoint?> LoadCheckpointAsync(string checkpointId, CancellationToken ct = default);

    /// <summary>Increment the task step counter and persist the new state.</summary>
    Task IncrementStepAsync(TaskId taskId, CancellationToken ct = default);

    /// <summary>Find and resume all incomplete tasks (Running/Paused/Failed) — startup recovery.</summary>
    Task<int> RecoverIncompleteTasksAsync(CancellationToken ct = default);
}
