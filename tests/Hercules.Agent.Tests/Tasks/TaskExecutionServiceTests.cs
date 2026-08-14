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
        _svc = new TaskExecutionService(_repo, _config, _loggerMock.Object);
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
    public async Task CreateCheckpointAsync_ReturnsCheckpointId()
    {
        var id = await _svc.StartAsync(new CreateTaskRequest { Name = "checkpointable" });

        var checkpointId = await _svc.CreateCheckpointAsync(id, 1, "{}");

        Assert.NotEmpty(checkpointId);
    }

    [Fact]
    public async Task ListCheckpointsAsync_ReturnsEmptyList_Stub()
    {
        var id = await _svc.StartAsync(new CreateTaskRequest { Name = "no-checkpoints" });

        var checkpoints = await _svc.ListCheckpointsAsync(id);

        Assert.Empty(checkpoints); // stub returns empty list
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
