using System.Collections.Concurrent;
using Hercules.Mesh.Abstractions;
using NATS.Client.JetStream;

namespace Hercules.Mesh.Backends.Nats;

/// <summary>
///     In-memory tracker of messages dequeued from JetStream and not yet acknowledged.
///     Holds ack/nak/terminate action delegates so that <see cref="NatsTaskQueue.AckAsync"/>
///     and <see cref="NatsTaskQueue.FailAsync"/> can find the original message by task id
///     after the fetch loop has returned (JetStream ack/nak is a separate publish to a
///     well-known subject and does not require the original <c>NatsJSMsg</c> to be in scope).
///     Spec: task_074.
/// </summary>
public sealed class NatsTaskInFlightTracker
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>Number of currently tracked in-flight entries.</summary>
    public int Count => _entries.Count;

    /// <summary>
    ///     Insert or replace the entry for <paramref name="taskId"/>. If a prior entry exists
    ///     (e.g. message was redelivered after AckWait) it is silently overwritten.
    /// </summary>
    public void AddOrReplace(Entry newEntry) =>
        _entries.AddOrUpdate(newEntry.TaskId, newEntry, (_, _) => newEntry);

    /// <summary>Get the entry for <paramref name="taskId"/>, or <c>null</c> if not tracked.</summary>
    public Entry? Get(string taskId) =>
        _entries.TryGetValue(taskId, out var entry) ? entry : null;

    /// <summary>Remove the entry for <paramref name="taskId"/>. Returns true if removed.</summary>
    public bool Remove(string taskId) => _entries.TryRemove(taskId, out _);

    /// <summary>Snapshot of all current task ids (for diagnostics/tests).</summary>
    public IReadOnlyCollection<string> Ids => _entries.Keys.ToArray();

    /// <summary>One in-flight task entry.</summary>
    public sealed class Entry
    {
        public required string TaskId { get; init; }
        public required string QueueName { get; init; }
        public required int NumDelivered { get; init; }
        public required MeshTask Task { get; init; }
        public required Func<CancellationToken, ValueTask> AckAsync { get; init; }
        public required Func<AckOpts?, CancellationToken, ValueTask> NakAsync { get; init; }
        public required Func<AckOpts?, CancellationToken, ValueTask> TerminateAsync { get; init; }
    }
}
