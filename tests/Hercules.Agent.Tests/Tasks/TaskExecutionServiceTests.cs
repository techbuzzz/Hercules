using Hercules.Config;
using Hercules.Storage;
using Hercules.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Tasks;

public class TaskExecutionServiceTests : IDisposable
{
    private readonly SqliteSessionStore _store;
    private readonly ITaskRepository _repo;
    private readonly TaskConfig _config;
    private readonly Mock<ILogger<TaskExecutionService>> _loggerMock;
    private readonly TaskExecutionService _svc;

    public TaskExecutionServiceTests()
    {
        _store = new SqliteSessionStore(new StorageConfig
        {
            DataRoot = Path.Combine(Path.GetTempPath(), $"hercules_tests_{Guid.NewGuid():N}"),
            SqliteFile = "test.db"
        });
        _repo = new SqliteTaskRepository(_store);
        _config = new TaskConfig
        {
            DefaultMaxRetries = 3,
            DefaultRetryDelayMs = 1000,
            DefaultBackoffMultiplier = 2.0,
            MaxConcurrentDurableTasks = 10,
            CheckpointRetentionDays = 7
        };
        _loggerMock = new Mock<ILogger<TaskExecutionService>>();
        _svc = new TaskExecutionService(_repo, _store, _config, _loggerMock.Object);
    }

    public void Dispose()
    {
        _store.Dispose();
    }

    [Fact]
    public async Task StartAsync_CreatesRunningTask()
    {
        var request = new CreateTaskRequest
        {
            Name = "test-task",
            Metadata = new DurableTaskMetadata { SkillId = "skill1", Priority = "high" },
            RetryPolicy = new TaskRetryPolicy { MaxRetries = 2 }
        };

        var id = await _svc.StartAsync(request);

        var task = await _svc.GetStatusAsync(id);
        Assert.NotNull(task);
        Assert.Equal("test-task", task!.Name);
        Assert.Equal(DurableTaskStatus.Running, task.Status);
        Assert.Equal("skill1", task.Metadata.SkillId);
        Assert.Equal("high", task.Metadata.Priority);
        Assert.Equal(0, task.AttemptCount);
        Assert.Equal(0, task.CurrentStep);
    }

    [Fact]
    public async Task StartAsync_WithDefaultRetryPolicy_UsesConfigDefaults()
    {
        var request = new CreateTaskRequest { Name = "task-with-defaults" };
        var id = await _svc.StartAsync(request);

        var task = await _svc.GetStatusAsync(id);
        Assert.NotNull(task);
        Assert.Equal(3, task!.RetryPolicy.MaxRetries);
        Assert.Equal(1000, task.RetryPolicy.RetryDelayMs);
        Assert.Equal(2.0, task.RetryPolicy.BackoffMultiplier);
    }

    [Fact]
    public async Task PauseAsync_SetsStatusToPaused()
    {
        var id = await _svc.StartAsync(new CreateTaskRequest { Name = "pauseable" });

        await _svc.PauseAsync(id);

        var task = await _svc.GetStatusAsync(id);
        Assert.Equal(DurableTaskStatus.Paused, task!.Status);
    }

    [Fact]
    public async Task ResumeAsync_SetsStatusToRunning()
    {
        var id = await _svc.StartAsync(new CreateTaskRequest { Name = "resumable" });
        await _svc.PauseAsync(id);

        await _svc.ResumeAsync(id, null);

        var task = await _svc.GetStatusAsync(id);
        Assert.Equal(DurableTaskStatus.Running, task!.Status);
    }

    [Fact]
    public async Task CancelAsync_SetsStatusToCancelled()
    {
        var id = await _svc.StartAsync(new CreateTaskRequest { Name = "cancellable" });

        await _svc.CancelAsync(id, "user requested");

        var task = await _svc.GetStatusAsync(id);
        Assert.Equal(DurableTaskStatus.Cancelled, task!.Status);
        Assert.Equal("user requested", task.CancellationReason);
    }

    [Fact]
    public async Task CancelAsync_WithoutReason_UsesDefault()
    {
        var id = await _svc.StartAsync(new CreateTaskRequest { Name = "cancellable2" });

        await _svc.CancelAsync(id, null);

        var task = await _svc.GetStatusAsync(id);
        Assert.Equal(DurableTaskStatus.Cancelled, task!.Status);
        Assert.NotEmpty(task.CancellationReason ?? "");
    }

    [Fact]
    public async Task ResumeAsync_FailedTask_IncrementsAttemptCount()
    {
        var id = await _svc.StartAsync(new CreateTaskRequest { Name = "retryable" });
        await _svc.PauseAsync(id);

        // Simulate a failed state manually
        var task = await _svc.GetStatusAsync(id);
        var failedTask = task! with { Status = DurableTaskStatus.Failed, AttemptCount = 1 };
        await _repo.UpdateAsync(failedTask);

        await _svc.ResumeAsync(id);

        var resumed = await _svc.GetStatusAsync(id);
        Assert.Equal(DurableTaskStatus.Running, resumed!.Status);
        Assert.Equal(1, resumed.AttemptCount); // attempt count preserved
    }

    [Fact]
    public async Task GetStatusAsync_Nonexistent_ReturnsNull()
    {
        var result = await _svc.GetStatusAsync(new TaskId(Guid.NewGuid()));
        Assert.Null(result);
    }

    [Fact]
    public async Task PauseAsync_Nonexistent_DoesNotThrow()
    {
        var id = new TaskId(Guid.NewGuid());
        await _svc.PauseAsync(id); // should not throw
        await _svc.CancelAsync(id); // should not throw
    }

    [Fact]
    public async Task CreateCheckpointAsync_PersistsCheckpoint()
    {
        var id = await _svc.StartAsync(new CreateTaskRequest { Name = "checkpointable" });

        var checkpointId = await _svc.CreateCheckpointAsync(id, 1, "{\"step\":1}");

        Assert.NotEmpty(checkpointId);

        // task_108: the checkpoint must be retrievable from the store.
        var loaded = await _svc.LoadCheckpointAsync(checkpointId);
        Assert.NotNull(loaded);
        Assert.Equal(1, loaded!.StepNumber);
        Assert.Equal("{\"step\":1}", loaded.StateSnapshot);
        Assert.Equal(id.ToString(), loaded.TaskId.ToString());
    }

    [Fact]
    public async Task ListCheckpointsAsync_ReturnsPersistedCheckpoints()
    {
        var id = await _svc.StartAsync(new CreateTaskRequest { Name = "no-checkpoints" });

        var first = await _svc.CreateCheckpointAsync(id, 1, "{\"step\":1}");
        var second = await _svc.CreateCheckpointAsync(id, 2, "{\"step\":2}");

        var checkpoints = await _svc.ListCheckpointsAsync(id);

        Assert.Equal(2, checkpoints.Count);
        Assert.Equal(first, checkpoints[0].Id);
        Assert.Equal(second, checkpoints[1].Id);
        Assert.Equal(1, checkpoints[0].StepNumber);
        Assert.Equal(2, checkpoints[1].StepNumber);
    }

    [Fact]
    public async Task LoadCheckpointAsync_Nonexistent_ReturnsNull()
    {
        var result = await _svc.LoadCheckpointAsync("does-not-exist");
        Assert.Null(result);
    }

    [Fact]
    public async Task IncrementStepAsync_Persists()
    {
        var id = await _svc.StartAsync(new CreateTaskRequest { Name = "stepper" });

        await _svc.IncrementStepAsync(id);
        await _svc.IncrementStepAsync(id);
        await _svc.IncrementStepAsync(id);

        var task = await _svc.GetStatusAsync(id);
        Assert.Equal(3, task!.CurrentStep);
        Assert.Equal(3, task.AttemptCount);
    }

    [Fact]
    public async Task ResumeAsync_LoadsCheckpointSnapshot()
    {
        var id = await _svc.StartAsync(new CreateTaskRequest { Name = "snapshot-resume" });
        var ckptId = await _svc.CreateCheckpointAsync(id, 7, "{\"phase\":\"midway\"}");
        await _svc.PauseAsync(id);

        await _svc.ResumeAsync(id, ckptId);

        var task = await _svc.GetStatusAsync(id);
        Assert.Equal(DurableTaskStatus.Running, task!.Status);
    }

    [Fact]
    public async Task ResumeAsync_FallsBackToLatestCheckpoint_WhenIdMissing()
    {
        var id = await _svc.StartAsync(new CreateTaskRequest { Name = "fallback-resume" });
        await _svc.CreateCheckpointAsync(id, 3, "{\"phase\":\"a\"}");
        await _svc.CreateCheckpointAsync(id, 5, "{\"phase\":\"b\"}");
        await _svc.PauseAsync(id);

        await _svc.ResumeAsync(id, "ghost-checkpoint");

        var task = await _svc.GetStatusAsync(id);
        Assert.Equal(DurableTaskStatus.Running, task!.Status);
    }

    [Fact]
    public async Task RecoverIncompleteTasksAsync_ResumesRunningAndPaused()
    {
        var runningId = await _svc.StartAsync(new CreateTaskRequest { Name = "running-recover" });
        var pausedId = await _svc.StartAsync(new CreateTaskRequest { Name = "paused-recover" });
        var completedId = await _svc.StartAsync(new CreateTaskRequest { Name = "completed-recover" });
        await _svc.CreateCheckpointAsync(pausedId, 2, "{\"step\":2}");

        // Mark the completed one via the internal helper.
        var completeMethod = typeof(TaskExecutionService)
            .GetMethod("CompleteTaskAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)completeMethod!.Invoke(_svc, new object[] { completedId, "ok", CancellationToken.None })!;
        await _svc.PauseAsync(pausedId);

        var reactivated = await _svc.RecoverIncompleteTasksAsync();

        Assert.Equal(2, reactivated);
        var running = await _svc.GetStatusAsync(runningId);
        var paused = await _svc.GetStatusAsync(pausedId);
        var completed = await _svc.GetStatusAsync(completedId);
        Assert.Equal(DurableTaskStatus.Running, running!.Status);
        Assert.Equal(DurableTaskStatus.Running, paused!.Status);
        Assert.Equal(DurableTaskStatus.Completed, completed!.Status);
    }

    [Fact]
    public async Task RecoverIncompleteTasksAsync_SkipsFailedTasks()
    {
        var failedId = await _svc.StartAsync(new CreateTaskRequest { Name = "failed-recover" });
        var task = await _svc.GetStatusAsync(failedId);
        var failed = task! with { Status = DurableTaskStatus.Failed };
        await _repo.UpdateAsync(failed);

        var reactivated = await _svc.RecoverIncompleteTasksAsync();

        Assert.Equal(0, reactivated);
        var after = await _svc.GetStatusAsync(failedId);
        Assert.Equal(DurableTaskStatus.Failed, after!.Status);
    }

    [Fact]
    public async Task CompleteTaskAsync_SetsCompletedState()
    {
        var id = await _svc.StartAsync(new CreateTaskRequest { Name = "completable" });

        // Use reflection to call internal helper
        var method = typeof(TaskExecutionService)
            .GetMethod("CompleteTaskAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(_svc, new object[] { id, "done!", CancellationToken.None })!;

        var task = await _svc.GetStatusAsync(id);
        Assert.Equal(DurableTaskStatus.Completed, task!.Status);
        Assert.Equal("done!", task.Result);
        Assert.NotNull(task.CompletedAt);
    }

    [Fact]
    public async Task FailTaskAsync_SetsFailedState()
    {
        var id = await _svc.StartAsync(new CreateTaskRequest { Name = "failable" });

        var method = typeof(TaskExecutionService)
            .GetMethod("FailTaskAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(_svc, new object[] { id, "something went wrong", CancellationToken.None })!;

        var task = await _svc.GetStatusAsync(id);
        Assert.Equal(DurableTaskStatus.Failed, task!.Status);
        Assert.Equal("something went wrong", task.Error);
        Assert.NotNull(task.CompletedAt);
    }
}
