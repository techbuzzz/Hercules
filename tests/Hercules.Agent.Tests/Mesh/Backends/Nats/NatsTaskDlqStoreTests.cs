using Hercules.Mesh.Abstractions;
using Hercules.Mesh.Backends.Nats;
using Xunit;

namespace Hercules.Agent.Tests.Mesh.Backends.Nats;

/// <summary>
///     Unit tests for <see cref="NatsTaskDlqStore"/> (task_074).
///     Covers JSONL append/list/remove and file persistence.
/// </summary>
public class NatsTaskDlqStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _file;

    public NatsTaskDlqStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nats-dlq-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "test-dlq.jsonl");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static MeshTask SampleTask(string id = "task-1", string queue = "q1") => new()
    {
        Id = id,
        QueueName = queue,
        Intent = "noop",
        Payload = "{\"k\":\"v\"}",
        MaxRetries = 3,
        Metadata = new Dictionary<string, string> { ["k"] = "v" }
    };

    [Fact]
    public async Task Append_Then_List_ReturnsEntry()
    {
        var store = new NatsTaskDlqStore(_file);
        var task = SampleTask();

        await store.AppendAsync(task, "boom", deliveryCount: 4, queueName: "q1");

        var entries = await store.ListAsync(queueName: "q1", limit: 10);
        Assert.Single(entries);
        Assert.Equal("task-1", entries[0].TaskId);
        Assert.Equal("q1", entries[0].QueueName);
        Assert.Equal("boom", entries[0].Reason);
        Assert.Equal(4, entries[0].DeliveryCount);
        Assert.Equal(3, entries[0].MaxRetries);
    }

    [Fact]
    public async Task List_FiltersByQueueName()
    {
        var store = new NatsTaskDlqStore(_file);
        await store.AppendAsync(SampleTask("a", "alpha"), "r1", 1);
        await store.AppendAsync(SampleTask("b", "beta"), "r2", 2);
        await store.AppendAsync(SampleTask("c", "alpha"), "r3", 3);

        var alpha = await store.ListAsync("alpha", 10);
        Assert.Equal(2, alpha.Count);
        Assert.All(alpha, e => Assert.Equal("alpha", e.QueueName));

        var beta = await store.ListAsync("beta", 10);
        Assert.Single(beta);
    }

    [Fact]
    public async Task List_LimitsResults()
    {
        var store = new NatsTaskDlqStore(_file);
        for (var i = 0; i < 5; i++)
        {
            await store.AppendAsync(SampleTask($"t{i}"), "r", i);
        }

        var entries = await store.ListAsync(queueName: null, limit: 2);
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public async Task List_NoFile_ReturnsEmpty()
    {
        var fresh = Path.Combine(_dir, "missing.jsonl");
        var store = new NatsTaskDlqStore(fresh);
        var entries = await store.ListAsync();
        Assert.Empty(entries);
    }

    [Fact]
    public async Task Remove_DeletesEntry_PreservesOthers()
    {
        var store = new NatsTaskDlqStore(_file);
        await store.AppendAsync(SampleTask("a"), "r", 1);
        await store.AppendAsync(SampleTask("b"), "r", 2);
        await store.AppendAsync(SampleTask("c"), "r", 3);

        var removed = await store.RemoveAsync("b");

        Assert.True(removed);
        var entries = await store.ListAsync(queueName: null, limit: 100);
        Assert.Equal(2, entries.Count);
        Assert.DoesNotContain(entries, e => e.TaskId == "b");
        Assert.Contains(entries, e => e.TaskId == "a");
        Assert.Contains(entries, e => e.TaskId == "c");
    }

    [Fact]
    public async Task Remove_MissingTask_ReturnsFalse()
    {
        var store = new NatsTaskDlqStore(_file);
        await store.AppendAsync(SampleTask("a"), "r", 1);

        var removed = await store.RemoveAsync("does-not-exist");

        Assert.False(removed);
        var entries = await store.ListAsync(queueName: null, limit: 100);
        Assert.Single(entries);
    }

    [Fact]
    public async Task Remove_NoFile_ReturnsFalse()
    {
        var fresh = Path.Combine(_dir, "missing.jsonl");
        var store = new NatsTaskDlqStore(fresh);

        var removed = await store.RemoveAsync("any");

        Assert.False(removed);
    }

    [Fact]
    public async Task Entries_PersistAcrossInstances()
    {
        var s1 = new NatsTaskDlqStore(_file);
        await s1.AppendAsync(SampleTask("persisted"), "r", 1);

        // New instance reads the same file.
        var s2 = new NatsTaskDlqStore(_file);
        var entries = await s2.ListAsync(queueName: null, limit: 10);

        Assert.Single(entries);
        Assert.Equal("persisted", entries[0].TaskId);
    }

    [Fact]
    public async Task Append_SkipsBlankLines_OnParse()
    {
        // Pre-write a blank line to simulate a partially-written file.
        await File.WriteAllTextAsync(_file, "\n\n" + new string(' ', 4) + "\n");

        var store = new NatsTaskDlqStore(_file);
        await store.AppendAsync(SampleTask("after-blank"), "r", 1);

        var entries = await store.ListAsync(queueName: null, limit: 10);
        Assert.Single(entries);
        Assert.Equal("after-blank", entries[0].TaskId);
    }

    [Fact]
    public async Task Append_AfterRemove_NewEntry_AppendsToEnd()
    {
        var store = new NatsTaskDlqStore(_file);
        await store.AppendAsync(SampleTask("x"), "r", 1);
        await store.RemoveAsync("x");
        await store.AppendAsync(SampleTask("y"), "r", 2);

        var entries = await store.ListAsync(queueName: null, limit: 10);
        Assert.Single(entries);
        Assert.Equal("y", entries[0].TaskId);
    }
}
