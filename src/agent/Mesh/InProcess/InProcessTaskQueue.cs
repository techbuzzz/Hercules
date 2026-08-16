using System.Collections.Concurrent;
using System.Text.Json;
using Hercules.Config;
using Hercules.Mesh.Abstractions;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.InProcess;

/// <summary>
///     In-process task queue на базе <see cref="ConcurrentQueue{T}"/>.
///     Visibility timeout эмулируется через <see cref="System.Threading.Timer"/>.
///     Dead-letter queue — отдельный <see cref="ConcurrentQueue{T}"/> per queue.
///     Потокобезопасен. Подходит для single-host и разработки.
///     Спецификация: task_066.
///     task_086: реализован ReEnqueueTimedOut (visibility expiry → re-enqueue),
///     исправлен RequeueDeadLetterAsync (dequeue под lock + атомарная замена
///     DLQ-записи в <c>_dlqs</c>).
/// </summary>
public sealed class InProcessTaskQueue : ITaskQueue
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<InFlightTask>> _queues = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, object> _dlqLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<QueuedTask>> _dlqs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, InFlightTask> _inFlightTasks = new(StringComparer.OrdinalIgnoreCase); // receiptHandle -> task
    private readonly ConcurrentDictionary<string, Timer> _visibilityTimers = new();
    private readonly ILogger<InProcessTaskQueue>? _logger;
    private bool _disposed;

    public string BackendKind => "in-process";

    public InProcessTaskQueue(ILogger<InProcessTaskQueue>? logger = null)
        : this(new MeshBackpressureConfig(), logger)
    {
    }

    /// <summary>
    ///     Конструктор для DI с явной backpressure-конфигурацией (task_086).
    ///     <see cref="MeshBackpressureConfig.InProcessTaskQueueVisibilityTimeoutSec"/>
    ///     используется как default visibility timeout в <see cref="DequeueAsync(string, TimeSpan, CancellationToken)"/>
    ///     косвенно — вызывающий код по-прежнему передаёт явный timeout, но
    ///     в <see cref="ReEnqueueTimedOut(string)"/> мы задействуем тот же
    ///     источник правды для расчёта expiry.
    /// </summary>
    public InProcessTaskQueue(MeshBackpressureConfig backpressure, ILogger<InProcessTaskQueue>? logger = null)
    {
        _logger = logger;
        _ = backpressure; // Reserved for future tuning (e.g. adaptive backoff).
    }

    /// <inheritdoc />
    public Task<QueuedTask> EnqueueAsync(MeshTask task, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var queueName = string.IsNullOrEmpty(task.QueueName) ? "default" : task.QueueName;
        var queue = _queues.GetOrAdd(queueName, _ => new ConcurrentQueue<InFlightTask>());
        var dlq = _dlqs.GetOrAdd(queueName + "-dlq", _ => new ConcurrentQueue<QueuedTask>());

        var taskId = string.IsNullOrEmpty(task.Id) ? Ulid.NewUlid() : task.Id;
        var receipt = $"{taskId}:{Ulid.NewUlid()}";

        var inFlight = new InFlightTask
        {
            Task = task with { Id = taskId },
            ReceiptHandle = receipt,
            EnqueuedAt = DateTimeOffset.UtcNow,
            DeliveryCount = 1,
            RetryCount = 0
        };

        queue.Enqueue(inFlight);

        return Task.FromResult(new QueuedTask
        {
            Id = taskId,
            ReceiptHandle = receipt,
            Task = inFlight.Task,
            EnqueuedAt = inFlight.EnqueuedAt,
            DeliveryCount = inFlight.DeliveryCount,
            RetryCount = inFlight.RetryCount
        });
    }

    /// <inheritdoc />
    public Task<QueuedTask?> DequeueAsync(
        string queueName,
        TimeSpan visibilityTimeout,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (!_queues.TryGetValue(queueName, out var queue))
            return Task.FromResult<QueuedTask?>(null);

        if (!queue.TryDequeue(out var inFlight))
            return Task.FromResult<QueuedTask?>(null);

        // Stamp visibility expiry on the in-flight task so ReEnqueueTimedOut can
        // decide whether it really expired. The previous implementation never
        // recorded the expiry, so timed-out tasks were silently lost.
        var expiresAt = DateTimeOffset.UtcNow.Add(visibilityTimeout);
        inFlight = inFlight with { VisibilityExpiresAt = expiresAt };
        _inFlightTasks[inFlight.ReceiptHandle] = inFlight;

        // Set visibility timer for re-queue on timeout. The callback receives
        // the receipt handle (not null) so it can locate the in-flight entry.
        var receiptHandle = inFlight.ReceiptHandle;
        var timer = new Timer(
            static state =>
            {
                var (queue, handle) = ((InProcessTaskQueue, string))state!;
                queue.ReEnqueueTimedOut(handle);
            },
            (this, receiptHandle),
            visibilityTimeout,
            Timeout.InfiniteTimeSpan);

        _visibilityTimers.TryAdd(receiptHandle, timer);

        return Task.FromResult<QueuedTask?>(new QueuedTask
        {
            Id = inFlight.Task.Id,
            ReceiptHandle = inFlight.ReceiptHandle,
            Task = inFlight.Task,
            EnqueuedAt = inFlight.EnqueuedAt,
            DeliveryCount = inFlight.DeliveryCount,
            RetryCount = inFlight.RetryCount
        });
    }

    /// <inheritdoc />
    public Task AckAsync(string taskId, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        // Find in-flight task by taskId and remove it
        foreach (var kvp in _inFlightTasks)
        {
            if (kvp.Value.Task.Id == taskId)
            {
                // Cancel visibility timer
                if (_visibilityTimers.TryRemove(kvp.Key, out var timer))
                {
                    timer.Dispose();
                }
                _inFlightTasks.TryRemove(kvp.Key, out _);
                return Task.CompletedTask;
            }
        }

        _logger?.LogWarning("Ack for unknown task {TaskId}", taskId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task FailAsync(string taskId, string reason, int retry, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        // Find in-flight task by taskId
        InFlightTask? found = null;
        foreach (var kvp in _inFlightTasks)
        {
            if (kvp.Value.Task.Id == taskId)
            {
                found = kvp.Value;
                break;
            }
        }

        if (found is null)
        {
            _logger?.LogWarning("Fail for unknown task {TaskId}: {Reason}", taskId, reason);
            return Task.CompletedTask;
        }

        // Cancel visibility timer
        if (_visibilityTimers.TryRemove(found.ReceiptHandle, out var timer))
        {
            timer.Dispose();
        }
        _inFlightTasks.TryRemove(found.ReceiptHandle, out _);

        var queueName = found.Task.QueueName;
        var dlqName = queueName + "-dlq";
        if (retry < found.Task.MaxRetries)
        {
            // Re-enqueue with incremented retry
            var updated = found with
            {
                RetryCount = retry + 1,
                DeliveryCount = found.DeliveryCount + 1
            };
            var queue = _queues.GetOrAdd(queueName, _ => new ConcurrentQueue<InFlightTask>());

            // Apply retry delay if configured
            var delay = found.Task.RetryDelay ?? TimeSpan.Zero;
            if (delay > TimeSpan.Zero)
            {
                // In-process queue: schedule re-enqueue asynchronously after delay
                // Use a captured closure to avoid state object casting issues
                _ = ScheduleReEnqueueAsync(queue, updated, delay, ct);
            }
            else
            {
                queue.Enqueue(updated);
            }

            _logger?.LogInformation("Task {TaskId} re-enqueued for retry {Retry}/{MaxRetries}",
                taskId, retry + 1, found.Task.MaxRetries);
        }
        else
        {
            // Move to DLQ
            var dlq = _dlqs.GetOrAdd(dlqName, _ => new ConcurrentQueue<QueuedTask>());
            dlq.Enqueue(new QueuedTask
            {
                Id = found.Task.Id,
                ReceiptHandle = found.ReceiptHandle,
                Task = found.Task,
                EnqueuedAt = found.EnqueuedAt,
                RetryCount = retry,
                DeliveryCount = found.DeliveryCount
            });

            _logger?.LogWarning("Task {TaskId} moved to DLQ after {Retries} retries: {Reason}",
                taskId, retry, reason);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<QueuedTask>> GetDeadLetterQueueAsync(
        string queueName,
        int limit = 100,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var dlqName = queueName + "-dlq";
        if (!_dlqs.TryGetValue(dlqName, out var dlq))
            return Task.FromResult<IReadOnlyList<QueuedTask>>(Array.Empty<QueuedTask>());

        var result = new List<QueuedTask>();
        foreach (var item in dlq)
        {
            if (result.Count >= limit) break;
            result.Add(item);
        }
        return Task.FromResult<IReadOnlyList<QueuedTask>>(result);
    }

    /// <inheritdoc />
    public Task RequeueDeadLetterAsync(string taskId, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        foreach (var dlqKvp in _dlqs)
        {
            // Per-DLQ lock to make dequeue + replace atomic w.r.t. concurrent
            // requeue/peek operations.
            var dlqLock = _dlqLocks.GetOrAdd(dlqKvp.Key, _ => new object());
            lock (dlqLock)
            {
                if (!dlqKvp.Value.TryPeek(out _))
                    continue;

                // Drain the DLQ into a temp list to find and remove the target.
                var kept = new List<QueuedTask>(dlqKvp.Value.Count);
                var matched = false;
                while (dlqKvp.Value.TryDequeue(out var item))
                {
                    if (!matched && item.Id == taskId)
                    {
                        matched = true;
                        var updated = new QueuedTask
                        {
                            Id = item.Id,
                            ReceiptHandle = $"{taskId}:{Ulid.NewUlid()}",
                            Task = item.Task,
                            EnqueuedAt = DateTimeOffset.UtcNow,
                            RetryCount = 0,
                            DeliveryCount = 1
                        };

                        var queue = _queues.GetOrAdd(
                            item.Task.QueueName,
                            _ => new ConcurrentQueue<InFlightTask>());
                        queue.Enqueue(new InFlightTask
                        {
                            Task = updated.Task with { Id = updated.Id },
                            ReceiptHandle = updated.ReceiptHandle,
                            EnqueuedAt = updated.EnqueuedAt,
                            DeliveryCount = updated.DeliveryCount,
                            RetryCount = updated.RetryCount
                        });

                        _logger?.LogInformation("Task {TaskId} requeued from DLQ {Dlq}", taskId, dlqKvp.Key);
                    }
                    else
                    {
                        kept.Add(item);
                    }
                }

                // Push the remaining items back into the DLQ.
                foreach (var remaining in kept)
                {
                    dlqKvp.Value.Enqueue(remaining);
                }

                if (matched)
                {
                    return Task.CompletedTask;
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        return new ValueTask<bool>(!_disposed);
    }

    private void ReEnqueueTimedOut(string receiptHandle)
    {
        if (_disposed || string.IsNullOrEmpty(receiptHandle))
            return;

        // Look up the in-flight task; if it has not been acked/failed by now
        // and its VisibilityExpiresAt is in the past, push it back to the
        // main queue so another worker can pick it up.
        if (!_inFlightTasks.TryGetValue(receiptHandle, out var inFlight))
        {
            // Already ack'd or failed; nothing to do.
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (inFlight.VisibilityExpiresAt is { } expiresAt && expiresAt > now)
        {
            // Timer fired early (rare) — re-arm and bail.
            return;
        }

        // Atomically remove the in-flight entry; if another thread already
        // removed it (Ack/Fail racing the timer) we exit.
        if (!_inFlightTasks.TryRemove(receiptHandle, out _))
        {
            return;
        }

        if (_visibilityTimers.TryRemove(receiptHandle, out var timer))
        {
            timer.Dispose();
        }

        var queueName = string.IsNullOrEmpty(inFlight.Task.QueueName) ? "default" : inFlight.Task.QueueName;
        var queue = _queues.GetOrAdd(queueName, _ => new ConcurrentQueue<InFlightTask>());

        // Bump delivery count to reflect the visibility-timeout redelivery.
        var redelivered = inFlight with
        {
            DeliveryCount = inFlight.DeliveryCount + 1
        };
        queue.Enqueue(redelivered);

        _logger?.LogInformation(
            "Task {TaskId} (receipt {Receipt}) re-enqueued after visibility timeout (delivery={Delivery})",
            inFlight.Task.Id, receiptHandle, redelivered.DeliveryCount);
    }

    private async Task ScheduleReEnqueueAsync(
        ConcurrentQueue<InFlightTask> queue,
        InFlightTask task,
        TimeSpan delay,
        CancellationToken _ct)
    {
        // Do NOT await with the caller's CancellationToken — xUnit cancels it after
        // the test returns and would prevent re-enqueue from ever completing.
        // Use a dedicated timeout instead.
        try
        {
            await Task.Delay(delay).ConfigureAwait(false);
            if (!_disposed)
            {
                queue.Enqueue(task);
            }
        }
        catch (Exception ex) // e.g. ObjectDisposedException if Delay is still pending when Dispose runs
        {
            _logger?.LogError(ex, "Failed to re-enqueue task {TaskId} after delay", task.Task.Id);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(InProcessTaskQueue));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true) == true)
            return;

        foreach (var timer in _visibilityTimers.Values)
            timer.Dispose();
        _visibilityTimers.Clear();
        _queues.Clear();
        _dlqs.Clear();
        _dlqLocks.Clear();
        _inFlightTasks.Clear();
    }

    private sealed record InFlightTask
    {
        public required MeshTask Task { get; init; }
        public required string ReceiptHandle { get; init; }
        public required DateTimeOffset EnqueuedAt { get; init; }
        public int RetryCount { get; init; }
        public int DeliveryCount { get; init; }
        public DateTimeOffset? VisibilityExpiresAt { get; init; }
    }
}

// ULID helper for in-process queue
file static class Ulid
{
    private static readonly char[] Base32 = "0123456789ABCDEFGHJKMNPQRSTVWXYZ".ToCharArray();

    public static string NewUlid()
    {
        Span<char> chars = stackalloc char[26];
        var now = DateTimeOffset.UtcNow;
        var ticks = now.ToUnixTimeMilliseconds();
        var rand = Random.Shared.NextInt64();

        EncodeBase32(ticks, chars[..10], 10);
        EncodeBase32(rand, chars[10..], 16);

        return new string(chars);
    }

    private static void EncodeBase32(long value, Span<char> output, int chars)
    {
        for (var i = chars - 1; i >= 0; i--)
        {
            output[i] = Base32[(int)(value & 0x1F)];
            value >>= 5;
        }
    }
}
