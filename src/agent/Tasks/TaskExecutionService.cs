using Hercules.Config;
using Hercules.Storage;
using Microsoft.Extensions.Logging;

namespace Hercules.Tasks;

/// <summary>
///     Implementation of <see cref="ITaskExecutionService"/>.
///     Coordinates durable task lifecycle with persistence and cancellation.
/// </summary>
public sealed class TaskExecutionService : ITaskExecutionService
{
    private readonly ITaskRepository _repo;
    private readonly ISessionStore _sessionStore;
    private readonly TaskConfig _config;
    private readonly ILogger<TaskExecutionService> _logger;

    public TaskExecutionService(
        ITaskRepository repo,
        ISessionStore sessionStore,
        TaskConfig config,
        ILogger<TaskExecutionService> logger)
    {
        _repo = repo;
        _sessionStore = sessionStore;
        _config = config;
        _logger = logger;
    }

    public async Task<TaskId> StartAsync(CreateTaskRequest request, CancellationToken ct = default)
    {
        var id = TaskId.New();
        var now = DateTime.UtcNow;

        var task = new DurableTask(
            id,
            request.Name,
            DurableTaskStatus.Running,
            request.Metadata ?? new DurableTaskMetadata(),
            request.RetryPolicy ?? new TaskRetryPolicy
            {
                MaxRetries = _config.DefaultMaxRetries,
                RetryDelayMs = _config.DefaultRetryDelayMs,
                BackoffMultiplier = _config.DefaultBackoffMultiplier
            },
            AttemptCount: 0,
            CurrentStep: 0,
            Result: null,
            Error: null,
            CreatedAt: now,
            UpdatedAt: now,
            CompletedAt: null,
            CancellationReason: null);

        await _repo.CreateAsync(task, ct);
        _logger.LogInformation("[Task] Started durable task {TaskId} ('{Name}')", id, request.Name);
        return id;
    }

    public async Task ResumeAsync(TaskId taskId, string? checkpointId = null, CancellationToken ct = default)
    {
        var task = await _repo.GetAsync(taskId, ct);
        if (task is null)
        {
            _logger.LogWarning("[Task] Resume failed: task {TaskId} not found", taskId);
            return;
        }

        if (task.Status != DurableTaskStatus.Paused && task.Status != DurableTaskStatus.Failed)
        {
            _logger.LogWarning("[Task] Resume failed: task {TaskId} is not paused or failed (status={Status})", taskId, task.Status);
            return;
        }

        // task_108: if a checkpoint was provided, load it and log the snapshot step;
        // otherwise pick the most recent checkpoint for this task.
        TaskCheckpoint? checkpoint = null;
        if (!string.IsNullOrEmpty(checkpointId))
        {
            checkpoint = await _sessionStore.LoadCheckpointAsync(checkpointId, ct);
            if (checkpoint is null)
            {
                _logger.LogWarning("[Task] Resume: checkpoint {CheckpointId} not found, falling back to most recent", checkpointId);
            }
        }
        if (checkpoint is null)
        {
            var existing = await _sessionStore.ListCheckpointsAsync(taskId.ToString(), ct);
            checkpoint = existing.Count > 0 ? existing[^1] : null;
        }

        var updated = task with
        {
            Status = DurableTaskStatus.Running,
            UpdatedAt = DateTime.UtcNow,
            AttemptCount = task.Status == DurableTaskStatus.Failed ? task.AttemptCount : task.AttemptCount,
            Error = null
        };
        await _repo.UpdateAsync(updated, ct);

        if (checkpoint is not null)
        {
            _logger.LogInformation(
                "[Task] Resumed task {TaskId} from checkpoint {CheckpointId} (step={Step}, snapshotBytes={Bytes})",
                taskId, checkpoint.Id, checkpoint.StepNumber, checkpoint.StateSnapshot.Length);
        }
        else
        {
            _logger.LogInformation("[Task] Resumed task {TaskId} (no checkpoints)", taskId);
        }
    }

    public async Task PauseAsync(TaskId taskId, CancellationToken ct = default)
    {
        var task = await _repo.GetAsync(taskId, ct);
        if (task is null) return;

        var updated = task with
        {
            Status = DurableTaskStatus.Paused,
            UpdatedAt = DateTime.UtcNow
        };
        await _repo.UpdateAsync(updated, ct);
        _logger.LogInformation("[Task] Paused task {TaskId}", taskId);
    }

    public async Task CancelAsync(TaskId taskId, string? reason = null, CancellationToken ct = default)
    {
        var task = await _repo.GetAsync(taskId, ct);
        if (task is null) return;

        var updated = task with
        {
            Status = DurableTaskStatus.Cancelled,
            UpdatedAt = DateTime.UtcNow,
            CancellationReason = reason ?? "User requested cancellation"
        };
        await _repo.UpdateAsync(updated, ct);
        _logger.LogInformation("[Task] Cancelled task {TaskId}: {Reason}", taskId, reason ?? "unknown");
    }

    public Task<DurableTask?> GetStatusAsync(TaskId taskId, CancellationToken ct = default)
    {
        return _repo.GetAsync(taskId, ct);
    }

    public async Task<string> CreateCheckpointAsync(
        TaskId taskId,
        int stepNumber,
        string stateSnapshot,
        CancellationToken ct = default)
    {
        var checkpoint = new TaskCheckpoint
        {
            TaskId = taskId,
            StepNumber = stepNumber,
            StateSnapshot = stateSnapshot,
            CreatedAt = DateTime.UtcNow
        };

        // task_108: actually persist via ISessionStore.
        await _sessionStore.SaveCheckpointAsync(checkpoint, ct);

        _logger.LogDebug(
            "[Task] Created checkpoint {CheckpointId} for task {TaskId} step {Step} (snapshotBytes={Bytes})",
            checkpoint.Id, taskId, stepNumber, stateSnapshot.Length);
        return checkpoint.Id;
    }

    public Task<List<TaskCheckpoint>> ListCheckpointsAsync(TaskId taskId, CancellationToken ct = default)
    {
        // task_108: query the store instead of returning an empty stub.
        return _sessionStore.ListCheckpointsAsync(taskId.ToString(), ct);
    }

    public Task<TaskCheckpoint?> LoadCheckpointAsync(string checkpointId, CancellationToken ct = default)
    {
        return _sessionStore.LoadCheckpointAsync(checkpointId, ct);
    }

    public async Task IncrementStepAsync(TaskId taskId, CancellationToken ct = default)
    {
        var task = await _repo.GetAsync(taskId, ct);
        if (task is null) return;

        var updated = task with
        {
            CurrentStep = task.CurrentStep + 1,
            AttemptCount = task.AttemptCount + 1,
            UpdatedAt = DateTime.UtcNow
        };
        await _repo.UpdateAsync(updated, ct);
    }

    /// <summary>
    ///     task_108: scan for incomplete tasks (Running/Paused/Failed) and resume them.
    ///     Paused tasks are reactivated to Running so the workflow executor (future)
    ///     can pick them up. Failed tasks stay in Failed state and are logged for
    ///     manual intervention. Completed/Cancelled tasks are skipped entirely.
    ///     Returns the number of tasks reactivated.
    /// </summary>
    public async Task<int> RecoverIncompleteTasksAsync(CancellationToken ct = default)
    {
        var reactivated = 0;
        foreach (var status in new[] { DurableTaskStatus.Running, DurableTaskStatus.Paused, DurableTaskStatus.Failed })
        {
            var tasks = await _repo.ListAsync(statusFilter: status, limit: 1000, ct);
            foreach (var task in tasks)
            {
                if (task.Status == DurableTaskStatus.Cancelled || task.Status == DurableTaskStatus.Completed)
                {
                    continue;
                }

                // Pick the most recent checkpoint, if any.
                var checkpoints = await _sessionStore.ListCheckpointsAsync(task.Id.ToString(), ct);
                var latest = checkpoints.Count > 0 ? checkpoints[^1] : null;

                if (task.Status == DurableTaskStatus.Failed)
                {
                    // Leave Failed tasks in Failed state so the operator can decide.
                    _logger.LogInformation(
                        "[Recovery] Skipped failed task {TaskId} (checkpoint={CheckpointId}) — manual intervention required",
                        task.Id, latest?.Id ?? "none");
                    continue;
                }

                // For Running or Paused: bump UpdatedAt, clear Error, and put Paused
                // tasks back into Running so the executor (future) picks them up.
                var updated = task with
                {
                    Status = DurableTaskStatus.Running,
                    UpdatedAt = DateTime.UtcNow,
                    Error = null
                };
                await _repo.UpdateAsync(updated, ct);

                _logger.LogInformation(
                    "[Recovery] Resumed {PreviousStatus} task {TaskId} (checkpoint={CheckpointId}, step={Step})",
                    task.Status, task.Id, latest?.Id ?? "none", latest?.StepNumber ?? 0);
                reactivated++;
            }
        }

        // Best-effort retention cleanup. Failures are logged but don't block startup.
        try
        {
            var retentionDays = Math.Max(1, _config.CheckpointRetentionDays);
            await _sessionStore.CleanupOldCheckpointsAsync(retentionDays, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Recovery] Checkpoint retention cleanup failed (non-fatal)");
        }

        return reactivated;
    }

    /// <summary>
    ///     Helper: update task to Completed state.
    /// </summary>
    internal async Task CompleteTaskAsync(TaskId taskId, string? result, CancellationToken ct = default)
    {
        var task = await _repo.GetAsync(taskId, ct);
        if (task is null) return;

        var updated = task with
        {
            Status = DurableTaskStatus.Completed,
            Result = result,
            UpdatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };
        await _repo.UpdateAsync(updated, ct);
    }

    /// <summary>
    ///     Helper: update task to Failed state.
    /// </summary>
    internal async Task FailTaskAsync(TaskId taskId, string error, CancellationToken ct = default)
    {
        var task = await _repo.GetAsync(taskId, ct);
        if (task is null) return;

        var updated = task with
        {
            Status = DurableTaskStatus.Failed,
            Error = error,
            UpdatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };
        await _repo.UpdateAsync(updated, ct);
    }
}
