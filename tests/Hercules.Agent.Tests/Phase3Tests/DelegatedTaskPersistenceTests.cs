using Hercules.Config;
using Hercules.Mesh.TaskLifecycle;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Phase3Tests;

/// <summary>
///     Phase 8 / task_106: persistence layer for inter-agent delegated tasks.
///     Covers the SQLite store, the protocol write-through, startup recovery,
///     expiry handling, and the JSON roundtrip of <see cref="AwaitingInputContext"/>.
/// </summary>
public class DelegatedTaskPersistenceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StorageConfig _cfg;

    public DelegatedTaskPersistenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hercules-delegated-tasks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _cfg = new StorageConfig { DataRoot = _tempDir, SqliteFile = "delegated.db" };
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private static DelegatedTask MakeSample(
        string? id = null,
        DelegatedTaskState state = DelegatedTaskState.Accepted,
        DateTimeOffset? deadline = null,
        AwaitingInputContext? awaiting = null) =>
        new(
            TaskId: id ?? "task-" + Guid.NewGuid().ToString("N"),
            ParentRequestId: "req-001",
            CallerAgentId: "caller-agent",
            Intent: "code-review",
            Payload: """{"file":"src/foo.cs"}""",
            State: state,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            CompletedAt: null,
            Result: null,
            Error: null,
            CancellationReason: null,
            ExpiresAt: deadline,
            AwaitingInputContext: awaiting,
            LocalTaskId: null);

    // ---- SqliteDelegatedTaskStore: round-trip ----

    [Fact]
    public async Task Save_AndGet_PreservesAllFields()
    {
        await using var store = new SqliteDelegatedTaskStore(_cfg);

        var awaiting = new AwaitingInputContext(
            RequestedAt: DateTimeOffset.UtcNow,
            InputType: "approval",
            Question: "Approve refactor?",
            Choices: new List<string> { "yes", "no" });
        var sample = MakeSample(state: DelegatedTaskState.AwaitingInput, awaiting: awaiting);

        await store.SaveAsync(sample);
        var loaded = await store.GetAsync(sample.TaskId);

        Assert.NotNull(loaded);
        Assert.Equal(sample.TaskId, loaded!.TaskId);
        Assert.Equal(sample.ParentRequestId, loaded.ParentRequestId);
        Assert.Equal(sample.CallerAgentId, loaded.CallerAgentId);
        Assert.Equal(sample.Intent, loaded.Intent);
        Assert.Equal(sample.Payload, loaded.Payload);
        Assert.Equal(DelegatedTaskState.AwaitingInput, loaded.State);
        Assert.NotNull(loaded.AwaitingInputContext);
        Assert.Equal("approval", loaded.AwaitingInputContext!.InputType);
        Assert.Equal("Approve refactor?", loaded.AwaitingInputContext.Question);
        Assert.Equal(new[] { "yes", "no" }, loaded.AwaitingInputContext.Choices);
    }

    [Fact]
    public async Task Get_NotFound_ReturnsNull()
    {
        await using var store = new SqliteDelegatedTaskStore(_cfg);

        var loaded = await store.GetAsync("nonexistent");

        Assert.Null(loaded);
    }

    [Fact]
    public async Task Save_IsIdempotent_UpsertsByTaskId()
    {
        await using var store = new SqliteDelegatedTaskStore(_cfg);

        var sample = MakeSample(state: DelegatedTaskState.Accepted);
        await store.SaveAsync(sample);

        var updated = sample with
        {
            State = DelegatedTaskState.Working,
            UpdatedAt = sample.UpdatedAt.AddSeconds(1)
        };
        await store.SaveAsync(updated);

        var loaded = await store.GetAsync(sample.TaskId);
        Assert.NotNull(loaded);
        Assert.Equal(DelegatedTaskState.Working, loaded!.State);
    }

    [Fact]
    public async Task List_FilterByState_ReturnsMatchingOnly()
    {
        await using var store = new SqliteDelegatedTaskStore(_cfg);

        await store.SaveAsync(MakeSample(state: DelegatedTaskState.Accepted));
        await store.SaveAsync(MakeSample(state: DelegatedTaskState.Working));
        await store.SaveAsync(MakeSample(state: DelegatedTaskState.Working));
        await store.SaveAsync(MakeSample(state: DelegatedTaskState.Completed));

        var working = await store.ListAsync(stateFilter: DelegatedTaskState.Working);

        Assert.Equal(2, working.Count);
        Assert.All(working, t => Assert.Equal(DelegatedTaskState.Working, t.State));
    }

    [Fact]
    public async Task List_FilterByParentRequestId_ReturnsMatchingOnly()
    {
        await using var store = new SqliteDelegatedTaskStore(_cfg);

        var t1 = MakeSample() with { ParentRequestId = "req-A" };
        var t2 = MakeSample() with { ParentRequestId = "req-A" };
        var t3 = MakeSample() with { ParentRequestId = "req-B" };

        await store.SaveAsync(t1);
        await store.SaveAsync(t2);
        await store.SaveAsync(t3);

        var grouped = await store.ListAsync(parentRequestId: "req-A");

        Assert.Equal(2, grouped.Count);
        Assert.All(grouped, t => Assert.Equal("req-A", t.ParentRequestId));
    }

    [Fact]
    public async Task ListPending_ReturnsOnlyNonTerminalStates()
    {
        await using var store = new SqliteDelegatedTaskStore(_cfg);

        await store.SaveAsync(MakeSample(state: DelegatedTaskState.Accepted));
        await store.SaveAsync(MakeSample(state: DelegatedTaskState.Working));
        await store.SaveAsync(MakeSample(state: DelegatedTaskState.AwaitingInput));
        await store.SaveAsync(MakeSample(state: DelegatedTaskState.Completed));
        await store.SaveAsync(MakeSample(state: DelegatedTaskState.Failed));
        await store.SaveAsync(MakeSample(state: DelegatedTaskState.Cancelled));
        await store.SaveAsync(MakeSample(state: DelegatedTaskState.Expired));

        var pending = await store.ListPendingAsync();

        Assert.Equal(3, pending.Count);
        Assert.Contains(pending, t => t.State == DelegatedTaskState.Accepted);
        Assert.Contains(pending, t => t.State == DelegatedTaskState.Working);
        Assert.Contains(pending, t => t.State == DelegatedTaskState.AwaitingInput);
    }

    [Fact]
    public async Task Delete_RemovesRow()
    {
        await using var store = new SqliteDelegatedTaskStore(_cfg);
        var sample = MakeSample();
        await store.SaveAsync(sample);

        await store.DeleteAsync(sample.TaskId);

        Assert.Null(await store.GetAsync(sample.TaskId));
    }

    [Fact]
    public async Task PruneExpired_RemovesOnlyTerminalRowsPastDeadline()
    {
        await using var store = new SqliteDelegatedTaskStore(_cfg);
        var longAgo = DateTimeOffset.UtcNow.AddDays(-2);

        // Three terminal tasks with various deadlines
        var completedLongAgo = MakeSample(state: DelegatedTaskState.Completed, deadline: longAgo);
        var failedRecent = MakeSample(state: DelegatedTaskState.Failed,
            deadline: DateTimeOffset.UtcNow.AddMinutes(5));
        var cancelledLongAgo = MakeSample(state: DelegatedTaskState.Cancelled, deadline: longAgo);

        // One non-terminal task with a long-past deadline (must NOT be pruned —
        // the protocol first transitions it to Expired, then prune picks it up).
        var acceptedLongAgo = MakeSample(state: DelegatedTaskState.Accepted, deadline: longAgo);

        await store.SaveAsync(completedLongAgo);
        await store.SaveAsync(failedRecent);
        await store.SaveAsync(cancelledLongAgo);
        await store.SaveAsync(acceptedLongAgo);

        var removed = await store.PruneExpiredAsync(DateTimeOffset.UtcNow);

        Assert.Equal(2, removed);
        Assert.Null(await store.GetAsync(completedLongAgo.TaskId));
        Assert.NotNull(await store.GetAsync(failedRecent.TaskId));
        Assert.Null(await store.GetAsync(cancelledLongAgo.TaskId));
        Assert.NotNull(await store.GetAsync(acceptedLongAgo.TaskId));
    }

    // ---- TaskLifecycleProtocol: write-through + recovery ----

    [Fact]
    public async Task AcceptAsync_PersistsToStore()
    {
        await using var store = new SqliteDelegatedTaskStore(_cfg);
        var protocol = new TaskLifecycleProtocol(
            transport: null,
            localAgentId: "local-agent",
            logger: Mock.Of<ILogger<TaskLifecycleProtocol>>(),
            store: store);

        var task = await protocol.AcceptAsync("req-1", "caller", "intent", "{}");

        var loaded = await store.GetAsync(task.TaskId);
        Assert.NotNull(loaded);
        Assert.Equal(DelegatedTaskState.Accepted, loaded!.State);
    }

    [Fact]
    public async Task StateTransitions_AreWriteThrough()
    {
        await using var store = new SqliteDelegatedTaskStore(_cfg);
        var protocol = new TaskLifecycleProtocol(
            transport: null,
            localAgentId: "local-agent",
            logger: Mock.Of<ILogger<TaskLifecycleProtocol>>(),
            store: store);

        var task = await protocol.AcceptAsync("req", "caller", "intent", "{}");
        await protocol.UpdateStateAsync(task.TaskId, DelegatedTaskState.Working);

        var loaded = await store.GetAsync(task.TaskId);
        Assert.Equal(DelegatedTaskState.Working, loaded!.State);

        await protocol.CompleteAsync(task.TaskId, "done");
        loaded = await store.GetAsync(task.TaskId);
        Assert.Equal(DelegatedTaskState.Completed, loaded!.State);
        Assert.Equal("done", loaded.Result);
        Assert.NotNull(loaded.CompletedAt);
    }

    [Fact]
    public async Task StartupRecovery_HydratesCacheFromStore()
    {
        // Phase 1: create a task, complete it (terminal — should NOT be in the pending cache)
        var first = new SqliteDelegatedTaskStore(_cfg);
        var protocol1 = new TaskLifecycleProtocol(
            transport: null,
            localAgentId: "local-agent",
            logger: Mock.Of<ILogger<TaskLifecycleProtocol>>(),
            store: first);
        var t1 = await protocol1.AcceptAsync("req-1", "caller", "intent-1", "{}");
        await protocol1.CompleteAsync(t1.TaskId, "ok");

        // Phase 2: create a task, leave it Accepted (pending — SHOULD be in the recovery cache)
        var t2 = await protocol1.AcceptAsync("req-2", "caller", "intent-2", "{}");
        await protocol1.AwaitInputAsync(t2.TaskId, "text", "Enter name", null);

        // Simulate restart: dispose protocol1, create a fresh one with a new store handle on the same file
        protocol1.Dispose();
        await first.DisposeAsync();

        await using var second = new SqliteDelegatedTaskStore(_cfg);
        var protocol2 = new TaskLifecycleProtocol(
            transport: null,
            localAgentId: "local-agent",
            logger: Mock.Of<ILogger<TaskLifecycleProtocol>>(),
            store: second);

        // The pending task survives; the completed one is filtered out.
        var recovered = await protocol2.GetStateAsync(t2.TaskId);
        Assert.NotNull(recovered);
        Assert.Equal(DelegatedTaskState.AwaitingInput, recovered!.State);
        Assert.Equal("Enter name", recovered.AwaitingInputContext?.Question);

        var goneAfterRestart = await protocol2.GetStateAsync(t1.TaskId);
        Assert.Null(goneAfterRestart);

        protocol2.Dispose();
    }

    [Fact]
    public async Task CheckExpired_PersistsExpiredTransition()
    {
        await using var store = new SqliteDelegatedTaskStore(_cfg);
        var protocol = new TaskLifecycleProtocol(
            transport: null,
            localAgentId: "local-agent",
            logger: Mock.Of<ILogger<TaskLifecycleProtocol>>(),
            store: store);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(-1);
        var task = await protocol.AcceptAsync("req", "caller", "intent", "{}", deadline: deadline);

        var expired = await protocol.CheckExpiredAsync(task.TaskId);

        Assert.True(expired);
        var loaded = await store.GetAsync(task.TaskId);
        Assert.Equal(DelegatedTaskState.Expired, loaded!.State);
    }

    [Fact]
    public async Task RecordInput_PersistsResumeToWorking()
    {
        await using var store = new SqliteDelegatedTaskStore(_cfg);
        var protocol = new TaskLifecycleProtocol(
            transport: null,
            localAgentId: "local-agent",
            logger: Mock.Of<ILogger<TaskLifecycleProtocol>>(),
            store: store);

        var task = await protocol.AcceptAsync("req", "caller", "intent", "{}");
        await protocol.AwaitInputAsync(task.TaskId, "text", "q", null);
        await protocol.RecordInputAsync(task.TaskId, "answer");

        var loaded = await store.GetAsync(task.TaskId);
        Assert.Equal(DelegatedTaskState.Working, loaded!.State);
        Assert.Null(loaded.AwaitingInputContext);
    }

    [Fact]
    public async Task BindLocalTask_PersistsLocalTaskId()
    {
        await using var store = new SqliteDelegatedTaskStore(_cfg);
        var protocol = new TaskLifecycleProtocol(
            transport: null,
            localAgentId: "local-agent",
            logger: Mock.Of<ILogger<TaskLifecycleProtocol>>(),
            store: store);

        var task = await protocol.AcceptAsync("req", "caller", "intent", "{}");
        await protocol.BindLocalTaskAsync(task.TaskId, "local-uuid-7");

        var loaded = await store.GetAsync(task.TaskId);
        Assert.Equal("local-uuid-7", loaded!.LocalTaskId);
    }

    [Fact]
    public async Task BackwardCompat_NoStore_BehavesLikeInMemory()
    {
        // Verifies the 2-arg constructor still works for the existing Phase-3 tests.
        var protocol = new TaskLifecycleProtocol("local", Mock.Of<ILogger<TaskLifecycleProtocol>>());

        var task = await protocol.AcceptAsync("req", "caller", "intent", "{}");
        var updated = await protocol.UpdateStateAsync(task.TaskId, DelegatedTaskState.Working);

        Assert.Equal(DelegatedTaskState.Working, updated.State);
    }
}
