using System.Collections.Concurrent;
using Hercules.Mesh.Schema;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.TaskLifecycle;

/// <summary>
///     Реализация <see cref="ITaskLifecycleProtocol"/>.
///     Хранит delegated задачи in-memory (ConcurrentDictionary) с возможностью
///     привязки к локальному DurableTask.
///     Persistence в SQLite добавляется как follow-up оптимизация.
/// </summary>
public sealed class TaskLifecycleProtocol : ITaskLifecycleProtocol
{
    private readonly ConcurrentDictionary<string, DelegatedTask> _tasks = new();
    private readonly ConcurrentDictionary<string, List<TaskCompletionSource<DelegatedTask>>> _inputPollers = new();
    private readonly IntentTransport? _transport;
    private readonly string _localAgentId;
    private readonly ILogger<TaskLifecycleProtocol> _logger;

    public TaskLifecycleProtocol(IntentTransport? transport, string localAgentId, ILogger<TaskLifecycleProtocol> logger)
    {
        _transport = transport;
        _localAgentId = localAgentId;
        _logger = logger;
    }

    /// <summary>Constructor without IntentTransport (for testing / no-callback mode).</summary>
    public TaskLifecycleProtocol(string localAgentId, ILogger<TaskLifecycleProtocol> logger)
        : this(null, localAgentId, logger)
    {
    }

    public Task<DelegatedTask> AcceptAsync(
        string parentRequestId,
        string callerAgentId,
        string intent,
        string payload,
        AuthContext? auth = null,
        DateTimeOffset? deadline = null,
        CancellationToken ct = default)
    {
        var taskId = IntentIds.NewRequestId();
        var now = DateTimeOffset.UtcNow;

        var task = new DelegatedTask(
            TaskId: taskId,
            ParentRequestId: parentRequestId,
            CallerAgentId: callerAgentId,
            Intent: intent,
            Payload: payload,
            State: DelegatedTaskState.Accepted,
            CreatedAt: now,
            UpdatedAt: now,
            CompletedAt: null,
            Result: null,
            Error: null,
            CancellationReason: null,
            ExpiresAt: deadline,
            AwaitingInputContext: null,
            LocalTaskId: null);

        _tasks[taskId] = task;
        _logger.LogInformation(
            "[TaskLifecycle] Accepted delegated task {TaskId} (parent={ParentRequestId}, caller={CallerAgentId}, intent={Intent})",
            taskId, parentRequestId, callerAgentId, intent);

        return Task.FromResult(task);
    }

    public Task<DelegatedTask> UpdateStateAsync(string taskId, DelegatedTaskState newState, CancellationToken ct = default)
    {
        if (!_tasks.TryGetValue(taskId, out var task))
            throw new InvalidOperationException($"Delegated task '{taskId}' not found.");

        ValidateTransition(task.State, newState, taskId);

        var updated = task with { State = newState, UpdatedAt = DateTimeOffset.UtcNow };
        _tasks[taskId] = updated;

        _logger.LogInformation("[TaskLifecycle] Task {TaskId} state: {OldState} → {NewState}",
            taskId, task.State, newState);

        return Task.FromResult(updated);
    }

    public Task<DelegatedTask> AwaitInputAsync(
        string taskId,
        string inputType,
        string? question,
        List<string>? choices,
        CancellationToken ct = default)
    {
        if (!_tasks.TryGetValue(taskId, out var task))
            throw new InvalidOperationException($"Delegated task '{taskId}' not found.");

        ValidateTransition(task.State, DelegatedTaskState.AwaitingInput, taskId);

        var ctx = new AwaitingInputContext(
            RequestedAt: DateTimeOffset.UtcNow,
            InputType: inputType,
            Question: question,
            Choices: choices);

        var updated = task with
        {
            State = DelegatedTaskState.AwaitingInput,
            UpdatedAt = DateTimeOffset.UtcNow,
            AwaitingInputContext = ctx
        };

        _tasks[taskId] = updated;
        _logger.LogInformation("[TaskLifecycle] Task {TaskId} awaiting input (type={InputType})", taskId, inputType);

        return Task.FromResult(updated);
    }

    public Task<DelegatedTask?> GetStateAsync(string taskId, CancellationToken ct = default)
    {
        _tasks.TryGetValue(taskId, out var task);
        return Task.FromResult(task);
    }

    public Task<DelegatedTask> CompleteAsync(string taskId, string result, CancellationToken ct = default)
    {
        if (!_tasks.TryGetValue(taskId, out var task))
            throw new InvalidOperationException($"Delegated task '{taskId}' not found.");

        ValidateTransition(task.State, DelegatedTaskState.Completed, taskId);

        var updated = task with
        {
            State = DelegatedTaskState.Completed,
            Result = result,
            CompletedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _tasks[taskId] = updated;
        _logger.LogInformation("[TaskLifecycle] Task {TaskId} completed", taskId);

        // Resolve waiting pollers
        ResolvePollers(taskId, updated);

        return Task.FromResult(updated);
    }

    public Task<DelegatedTask> FailAsync(string taskId, string error, CancellationToken ct = default)
    {
        if (!_tasks.TryGetValue(taskId, out var task))
            throw new InvalidOperationException($"Delegated task '{taskId}' not found.");

        ValidateTransition(task.State, DelegatedTaskState.Failed, taskId);

        var updated = task with
        {
            State = DelegatedTaskState.Failed,
            Error = error,
            CompletedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _tasks[taskId] = updated;
        _logger.LogWarning("[TaskLifecycle] Task {TaskId} failed: {Error}", taskId, error);

        ResolvePollers(taskId, updated);

        return Task.FromResult(updated);
    }

    public Task<DelegatedTask> CancelAsync(string taskId, string? reason, CancellationToken ct = default)
    {
        if (!_tasks.TryGetValue(taskId, out var task))
            throw new InvalidOperationException($"Delegated task '{taskId}' not found.");

        ValidateTransition(task.State, DelegatedTaskState.Cancelled, taskId);

        var updated = task with
        {
            State = DelegatedTaskState.Cancelled,
            CancellationReason = reason ?? "Cancelled by agent",
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _tasks[taskId] = updated;
        _logger.LogInformation("[TaskLifecycle] Task {TaskId} cancelled: {Reason}", taskId, reason ?? "unknown");

        ResolvePollers(taskId, updated);

        return Task.FromResult(updated);
    }

    public Task<bool> CheckExpiredAsync(string taskId, CancellationToken ct = default)
    {
        if (!_tasks.TryGetValue(taskId, out var task))
            return Task.FromResult(false);

        if (task.ExpiresAt.HasValue && DateTimeOffset.UtcNow > task.ExpiresAt.Value)
        {
            var updated = task with
            {
                State = DelegatedTaskState.Expired,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _tasks[taskId] = updated;
            _logger.LogWarning("[TaskLifecycle] Task {TaskId} expired (deadline={Deadline})", taskId, task.ExpiresAt);

            ResolvePollers(taskId, updated);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<DelegatedTask> RecordInputAsync(string taskId, string input, CancellationToken ct = default)
    {
        if (!_tasks.TryGetValue(taskId, out var task))
            throw new InvalidOperationException($"Delegated task '{taskId}' not found.");

        if (task.State != DelegatedTaskState.AwaitingInput)
            throw new InvalidOperationException(
                $"Task '{taskId}' is not awaiting input (current state: {task.State}).");

        var updated = task with
        {
            State = DelegatedTaskState.Working,
            UpdatedAt = DateTimeOffset.UtcNow,
            AwaitingInputContext = null
        };

        _tasks[taskId] = updated;
        _logger.LogInformation("[TaskLifecycle] Task {TaskId} received input, resuming (Working)", taskId);

        ResolvePollers(taskId, updated);

        return Task.FromResult(updated);
    }

    public Task<DelegatedTask> BindLocalTaskAsync(string taskId, string localTaskId, CancellationToken ct = default)
    {
        if (!_tasks.TryGetValue(taskId, out var task))
            throw new InvalidOperationException($"Delegated task '{taskId}' not found.");

        var updated = task with { LocalTaskId = localTaskId };
        _tasks[taskId] = updated;

        return Task.FromResult(updated);
    }

    public async Task<DelegatedTask?> PollForInputDeliveryAsync(
        string taskId,
        int pollTimeoutMs,
        CancellationToken ct = default)
    {
        if (!_tasks.TryGetValue(taskId, out var currentTask))
            return null;

        if (currentTask.State != DelegatedTaskState.AwaitingInput)
            return currentTask;

        // Check expiration
        if (currentTask.ExpiresAt.HasValue && DateTimeOffset.UtcNow > currentTask.ExpiresAt.Value)
        {
            await CheckExpiredAsync(taskId, ct);
            return _tasks.TryGetValue(taskId, out var expired) ? expired : null;
        }

        var tcs = new TaskCompletionSource<DelegatedTask>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var registration = ct.Register(() => tcs.TrySetCanceled());
        using var timeoutCts = new CancellationTokenSource(pollTimeoutMs);
        using var timeoutReg = timeoutCts.Token.Register(() => tcs.TrySetResult(currentTask));

        // Register poller
        var list = _inputPollers.GetOrAdd(taskId, _ => new List<TaskCompletionSource<DelegatedTask>>());
        lock (list)
        {
            list.Add(tcs);
        }

        try
        {
            return await tcs.Task;
        }
        finally
        {
            // Cleanup registration
            lock (list)
            {
                list.Remove(tcs);
                if (list.Count == 0)
                    _inputPollers.TryRemove(taskId, out _);
            }
        }
    }

    public async Task NotifyStateChangeAsync(string taskId, CancellationToken ct = default)
    {
        if (!_tasks.TryGetValue(taskId, out var task))
            return;

        if (_transport is null)
        {
            _logger.LogDebug("[TaskLifecycle] No IntentTransport configured — skipping callback for task {TaskId}", taskId);
            return;
        }

        if (string.IsNullOrWhiteSpace(task.CallerAgentId))
        {
            _logger.LogDebug("[TaskLifecycle] No callerAgentId for task {TaskId} — skipping callback", taskId);
            return;
        }

        var callbackPayload = new DelegatedTaskCallback
        {
            TaskId = taskId,
            ParentRequestId = task.ParentRequestId,
            State = task.State.ToString(),
            Result = task.Result,
            Error = task.Error,
            AwaitingInput = task.AwaitingInputContext is not null,
            UpdatedAt = task.UpdatedAt
        };

        var envelope = IntentEnvelope.Create(
            IntentIds.NewRequestId(),
            _localAgentId,
            "task.lifecycle.callback",
            callbackPayload,
            recipient: task.CallerAgentId,
            traceId: null);

        try
        {
            await _transport.SendToAsync(task.CallerAgentId, envelope, ct);
            _logger.LogDebug("[TaskLifecycle] Callback sent for task {TaskId} to {CallerAgentId}", taskId, task.CallerAgentId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TaskLifecycle] Failed to send callback for task {TaskId}", taskId);
        }
    }

    private void ResolvePollers(string taskId, DelegatedTask task)
    {
        if (_inputPollers.TryGetValue(taskId, out var list))
        {
            lock (list)
            {
                foreach (var poller in list.ToList())
                {
                    poller.TrySetResult(task);
                }
            }
        }
    }

    private static void ValidateTransition(DelegatedTaskState from, DelegatedTaskState to, string taskId)
    {
        var valid = (from, to) switch
        {
            // Normal forward transitions
            (DelegatedTaskState.Accepted, DelegatedTaskState.Working) => true,
            (DelegatedTaskState.Accepted, DelegatedTaskState.AwaitingInput) => true,
            (DelegatedTaskState.Accepted, DelegatedTaskState.Completed) => true,
            (DelegatedTaskState.Accepted, DelegatedTaskState.Failed) => true,
            (DelegatedTaskState.Accepted, DelegatedTaskState.Cancelled) => true,
            (DelegatedTaskState.Accepted, DelegatedTaskState.Expired) => true,

            (DelegatedTaskState.Working, DelegatedTaskState.AwaitingInput) => true,
            (DelegatedTaskState.Working, DelegatedTaskState.Completed) => true,
            (DelegatedTaskState.Working, DelegatedTaskState.Failed) => true,
            (DelegatedTaskState.Working, DelegatedTaskState.Cancelled) => true,
            (DelegatedTaskState.Working, DelegatedTaskState.Expired) => true,

            (DelegatedTaskState.AwaitingInput, DelegatedTaskState.Working) => true, // input delivered
            (DelegatedTaskState.AwaitingInput, DelegatedTaskState.Completed) => true,
            (DelegatedTaskState.AwaitingInput, DelegatedTaskState.Failed) => true,
            (DelegatedTaskState.AwaitingInput, DelegatedTaskState.Cancelled) => true,
            (DelegatedTaskState.AwaitingInput, DelegatedTaskState.Expired) => true,

            // Terminal states — no further transitions
            (DelegatedTaskState.Completed, _) => false,
            (DelegatedTaskState.Failed, _) => false,
            (DelegatedTaskState.Cancelled, _) => false,
            (DelegatedTaskState.Expired, _) => false,

            // Same state — idempotent
            (var s, var t) when s == t => true,

            _ => false
        };

        if (!valid)
        {
            throw new InvalidOperationException(
                $"Invalid state transition for delegated task '{taskId}': {from} → {to}");
        }
    }
}

/// <summary>
///     Callback payload, отправляемый вызывающему агенту при смене состояния.
/// </summary>
public sealed record DelegatedTaskCallback
{
    public string TaskId { get; set; } = "";
    public string ParentRequestId { get; set; } = "";
    public string State { get; set; } = "";
    public string? Result { get; set; }
    public string? Error { get; set; }
    public bool AwaitingInput { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
