using Hercules.Mesh.TaskLifecycle;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Phase3Tests;

/// <summary>
///     Phase 3: task_036 — Inter-agent task lifecycle protocol tests.
/// </summary>
public class TaskLifecycleProtocolTests
{
    private readonly TaskLifecycleProtocol _protocol;

    public TaskLifecycleProtocolTests()
    {
        var logger = new Mock<ILogger<TaskLifecycleProtocol>>();
        _protocol = new TaskLifecycleProtocol("hercules-local", logger.Object);
    }

    // ---- Accept ----

    [Fact]
    public async Task AcceptAsync_CreatesTaskWithAcceptedState()
    {
        var task = await _protocol.AcceptAsync(
            "req-001", "caller-agent", "code-review",
            """{"file":"src/foo.cs"}""", null, null);

        Assert.NotNull(task);
        Assert.NotEmpty(task.TaskId);
        Assert.Equal("req-001", task.ParentRequestId);
        Assert.Equal("caller-agent", task.CallerAgentId);
        Assert.Equal("code-review", task.Intent);
        Assert.Equal(DelegatedTaskState.Accepted, task.State);
        Assert.Null(task.CompletedAt);
        Assert.Null(task.Result);
        Assert.Null(task.Error);
    }

    [Fact]
    public async Task AcceptAsync_WithDeadline_SetsExpiresAt()
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(5);
        var task = await _protocol.AcceptAsync(
            "req-002", "caller-agent", "csharp-refactor",
            "{}", null, deadline);

        Assert.NotNull(task.ExpiresAt);
        Assert.Equal(deadline, task.ExpiresAt);
    }

    // ---- State transitions ----

    [Fact]
    public async Task UpdateStateAsync_AcceptedToWorking_Succeeds()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");
        var updated = await _protocol.UpdateStateAsync(task.TaskId, DelegatedTaskState.Working);

        Assert.Equal(DelegatedTaskState.Working, updated.State);
    }

    [Fact]
    public async Task UpdateStateAsync_InvalidTransition_Throws()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");
        await _protocol.CompleteAsync(task.TaskId, "done");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _protocol.UpdateStateAsync(task.TaskId, DelegatedTaskState.Working));
    }

    [Fact]
    public async Task UpdateStateAsync_SameState_IsIdempotent()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");
        var updated = await _protocol.UpdateStateAsync(task.TaskId, DelegatedTaskState.Accepted);

        Assert.Equal(DelegatedTaskState.Accepted, updated.State);
    }

    // ---- AwaitInput ----

    [Fact]
    public async Task AwaitInputAsync_TransitionsToAwaitingInput()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");
        await _protocol.UpdateStateAsync(task.TaskId, DelegatedTaskState.Working);

        var updated = await _protocol.AwaitInputAsync(
            task.TaskId, "approval", "Approve refactoring?", new List<string> { "yes", "no" });

        Assert.Equal(DelegatedTaskState.AwaitingInput, updated.State);
        Assert.NotNull(updated.AwaitingInputContext);
        Assert.Equal("approval", updated.AwaitingInputContext.InputType);
        Assert.Equal("Approve refactoring?", updated.AwaitingInputContext.Question);
        Assert.Equal(2, updated.AwaitingInputContext.Choices!.Count);
    }

    [Fact]
    public async Task AwaitInputAsync_FromTerminalState_Throws()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");
        await _protocol.CompleteAsync(task.TaskId, "ok");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _protocol.AwaitInputAsync(task.TaskId, "text", null, null));
    }

    // ---- Complete ----

    [Fact]
    public async Task CompleteAsync_TransitionsToCompleted()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");
        await _protocol.UpdateStateAsync(task.TaskId, DelegatedTaskState.Working);

        var completed = await _protocol.CompleteAsync(task.TaskId, """{"status":"done"}""");

        Assert.Equal(DelegatedTaskState.Completed, completed.State);
        Assert.Equal("""{"status":"done"}""", completed.Result);
        Assert.NotNull(completed.CompletedAt);
    }

    [Fact]
    public async Task CompleteAsync_FromTerminalState_Throws()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");
        await _protocol.CompleteAsync(task.TaskId, "ok");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _protocol.CompleteAsync(task.TaskId, "ok"));
    }

    // ---- Fail ----

    [Fact]
    public async Task FailAsync_TransitionsToFailed()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");

        var failed = await _protocol.FailAsync(task.TaskId, "Compilation error");

        Assert.Equal(DelegatedTaskState.Failed, failed.State);
        Assert.Equal("Compilation error", failed.Error);
        Assert.NotNull(failed.CompletedAt);
    }

    [Fact]
    public async Task FailAsync_FromTerminalState_Throws()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");
        await _protocol.FailAsync(task.TaskId, "err");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _protocol.FailAsync(task.TaskId, "err2"));
    }

    // ---- Cancel ----

    [Fact]
    public async Task CancelAsync_TransitionsToCancelled()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");

        var cancelled = await _protocol.CancelAsync(task.TaskId, "User cancelled");

        Assert.Equal(DelegatedTaskState.Cancelled, cancelled.State);
        Assert.Equal("User cancelled", cancelled.CancellationReason);
    }

    [Fact]
    public async Task CancelAsync_FromTerminalState_Throws()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");
        await _protocol.CompleteAsync(task.TaskId, "ok");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _protocol.CancelAsync(task.TaskId, "reason"));
    }

    // ---- Expiration ----

    [Fact]
    public async Task CheckExpiredAsync_WithExpiredDeadline_MarksExpired()
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(-1); // already expired
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}",
            deadline: deadline);

        var expired = await _protocol.CheckExpiredAsync(task.TaskId);

        Assert.True(expired);
        var updated = await _protocol.GetStateAsync(task.TaskId);
        Assert.Equal(DelegatedTaskState.Expired, updated!.State);
    }

    [Fact]
    public async Task CheckExpiredAsync_WithoutDeadline_ReturnsFalse()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");

        var expired = await _protocol.CheckExpiredAsync(task.TaskId);

        Assert.False(expired);
        var updated = await _protocol.GetStateAsync(task.TaskId);
        Assert.Equal(DelegatedTaskState.Accepted, updated!.State);
    }

    [Fact]
    public async Task CheckExpiredAsync_NonExistentTask_ReturnsFalse()
    {
        var expired = await _protocol.CheckExpiredAsync("non-existent-task");

        Assert.False(expired);
    }

    // ---- RecordInput ----

    [Fact]
    public async Task RecordInputAsync_FromAwaitingInput_ResumesWorking()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");
        await _protocol.AwaitInputAsync(task.TaskId, "text", "Enter value", null);

        var resumed = await _protocol.RecordInputAsync(task.TaskId, "user input");

        Assert.Equal(DelegatedTaskState.Working, resumed.State);
        Assert.Null(resumed.AwaitingInputContext);
    }

    [Fact]
    public async Task RecordInputAsync_NotAwaitingInput_Throws()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _protocol.RecordInputAsync(task.TaskId, "input"));
    }

    // ---- BindLocalTask ----

    [Fact]
    public async Task BindLocalTaskAsync_SetsLocalTaskId()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");

        var bound = await _protocol.BindLocalTaskAsync(task.TaskId, "local-uuid-123");

        Assert.Equal("local-uuid-123", bound.LocalTaskId);
    }

    // ---- PollForInputDelivery ----

    [Fact]
    public async Task PollForInputDeliveryAsync_AlreadyResolved_ReturnsImmediately()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");

        var result = await _protocol.PollForInputDeliveryAsync(task.TaskId, 5000);

        Assert.NotNull(result);
        Assert.Equal(DelegatedTaskState.Accepted, result.State);
    }

    [Fact]
    public async Task PollForInputDeliveryAsync_ResolvesOnInput()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");
        await _protocol.AwaitInputAsync(task.TaskId, "text", null, null);

        // Start polling in background
        var pollTask = _protocol.PollForInputDeliveryAsync(task.TaskId, 5000);

        // Simulate input delivery
        await _protocol.RecordInputAsync(task.TaskId, "input data");

        var result = await pollTask;

        Assert.NotNull(result);
        Assert.Equal(DelegatedTaskState.Working, result.State);
    }

    [Fact]
    public async Task PollForInputDeliveryAsync_NonExistent_ReturnsNull()
    {
        var result = await _protocol.PollForInputDeliveryAsync("non-existent", 1000);

        Assert.Null(result);
    }

    // ---- GetState ----

    [Fact]
    public async Task GetStateAsync_NonExistent_ReturnsNull()
    {
        var task = await _protocol.GetStateAsync("non-existent");

        Assert.Null(task);
    }

    [Fact]
    public async Task GetStateAsync_AfterTransitions_ReturnsCurrentState()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");
        await _protocol.UpdateStateAsync(task.TaskId, DelegatedTaskState.Working);
        await _protocol.CompleteAsync(task.TaskId, "result");

        var current = await _protocol.GetStateAsync(task.TaskId);

        Assert.NotNull(current);
        Assert.Equal(DelegatedTaskState.Completed, current.State);
        Assert.Equal("result", current.Result);
    }

    // ---- Terminal state — no further transitions ----

    [Fact]
    public async Task Completed_BlocksAllTransitions()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");
        await _protocol.CompleteAsync(task.TaskId, "ok");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _protocol.UpdateStateAsync(task.TaskId, DelegatedTaskState.Working));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _protocol.CancelAsync(task.TaskId, "reason"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _protocol.FailAsync(task.TaskId, "err"));
    }

    [Fact]
    public async Task Failed_BlocksAllTransitions()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");
        await _protocol.FailAsync(task.TaskId, "error");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _protocol.UpdateStateAsync(task.TaskId, DelegatedTaskState.Working));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _protocol.CompleteAsync(task.TaskId, "ok"));
    }

    [Fact]
    public async Task Cancelled_BlocksAllTransitions()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");
        await _protocol.CancelAsync(task.TaskId, "reason");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _protocol.UpdateStateAsync(task.TaskId, DelegatedTaskState.Working));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _protocol.CompleteAsync(task.TaskId, "ok"));
    }

    // ---- AwaitingInput context ----

    [Fact]
    public async Task AwaitingInput_FromWorking_Allowed()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");
        await _protocol.UpdateStateAsync(task.TaskId, DelegatedTaskState.Working);

        var updated = await _protocol.AwaitInputAsync(task.TaskId, "choice", "Select option", null);

        Assert.Equal(DelegatedTaskState.AwaitingInput, updated.State);
    }

    [Fact]
    public async Task AwaitingInput_FromAwaitingInput_IsIdempotent()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");
        await _protocol.UpdateStateAsync(task.TaskId, DelegatedTaskState.Working);
        await _protocol.AwaitInputAsync(task.TaskId, "choice", "Select option", null);

        var updated = await _protocol.UpdateStateAsync(task.TaskId, DelegatedTaskState.AwaitingInput);

        Assert.Equal(DelegatedTaskState.AwaitingInput, updated.State);
    }

    [Fact]
    public async Task AwaitingInput_ToCompleted_Allowed()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");
        await _protocol.UpdateStateAsync(task.TaskId, DelegatedTaskState.Working);
        await _protocol.AwaitInputAsync(task.TaskId, "text", null, null);

        var completed = await _protocol.CompleteAsync(task.TaskId, "result");

        Assert.Equal(DelegatedTaskState.Completed, completed.State);
    }

    [Fact]
    public async Task AwaitingInput_ToFailed_Allowed()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");
        await _protocol.UpdateStateAsync(task.TaskId, DelegatedTaskState.Working);
        await _protocol.AwaitInputAsync(task.TaskId, "approval", null, null);

        var failed = await _protocol.FailAsync(task.TaskId, "cancelled by upstream");

        Assert.Equal(DelegatedTaskState.Failed, failed.State);
    }

    [Fact]
    public async Task AwaitingInput_ToCancelled_Allowed()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}");
        await _protocol.UpdateStateAsync(task.TaskId, DelegatedTaskState.Working);
        await _protocol.AwaitInputAsync(task.TaskId, "choice", null, null);

        var cancelled = await _protocol.CancelAsync(task.TaskId, "no longer needed");

        Assert.Equal(DelegatedTaskState.Cancelled, cancelled.State);
    }

    [Fact]
    public async Task AwaitingInput_ToExpired_Allowed()
    {
        var task = await _protocol.AcceptAsync("req", "caller", "test", "{}",
            deadline: DateTimeOffset.UtcNow.AddSeconds(-5));
        await _protocol.UpdateStateAsync(task.TaskId, DelegatedTaskState.Working);
        await _protocol.AwaitInputAsync(task.TaskId, "text", null, null);

        var expired = await _protocol.CheckExpiredAsync(task.TaskId);

        Assert.True(expired);
    }
}
