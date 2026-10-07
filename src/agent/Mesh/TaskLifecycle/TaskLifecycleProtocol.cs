using System.Collections.Concurrent;
using Hercules.Mesh.Audit;
using Hercules.Mesh.Observability;
using Hercules.Mesh.Schema;
using Hercules.Mesh.Transport;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.TaskLifecycle;

/// <summary>
///     Реализация <see cref="ITaskLifecycleProtocol"/>.
///     Хранит delegated задачи в SQLite-backed <see cref="IDelegatedTaskStore"/>
///     (task_106) с in-memory cache для быстрого чтения на hot-path poll'а.
///     State transitions пишут через store; long-poll <see cref="TaskCompletionSource"/>
///     реестр остаётся in-memory (per-process) и пересоздаётся из store при старте.
/// </summary>
public sealed class TaskLifecycleProtocol : ITaskLifecycleProtocol, IDisposable, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, DelegatedTask> _tasks = new();
    private readonly ConcurrentDictionary<string, List<TaskCompletionSource<DelegatedTask>>> _inputPollers = new();
    private readonly ITransport? _transport;
    private readonly string _localAgentId;
    private readonly ILogger<TaskLifecycleProtocol> _logger;
    private readonly MeshAuditService? _auditService;
    private readonly IMeshObservabilityService? _observability;
    private readonly IDelegatedTaskStore? _store;
    private readonly Timer? _expiryTimer;
    private readonly TimeSpan _expiryScanInterval;
    private int _disposed;

    /// <summary>
    ///     Constructor with full DI surface (production).
    ///     If <paramref name="store"/> is provided, the protocol write-throughs
    ///     every state change and hydrates the in-memory cache from
    ///     <see cref="IDelegatedTaskStore.ListPendingAsync"/> on startup.
    ///     A background timer periodically scans for expired tasks.
    /// </summary>
    public TaskLifecycleProtocol(
        ITransport? transport,
        string localAgentId,
        ILogger<TaskLifecycleProtocol> logger,
        MeshAuditService? auditService = null,
        IMeshObservabilityService? observability = null,
        IDelegatedTaskStore? store = null,
        TimeSpan? expiryScanInterval = null)
    {
        _transport = transport;
        _localAgentId = localAgentId;
        _logger = logger;
        _auditService = auditService;
        _observability = observability;
        _store = store;
        _expiryScanInterval = expiryScanInterval ?? TimeSpan.FromMinutes(5);

        if (_store is not null)
        {
            // Hydrate cache from persisted state. Best-effort: if the store is
            // unhealthy at startup we log and continue with an empty cache —
            // the next state transition will surface a fresh error.
            try
            {
                var pending = _store.ListPendingAsync().GetAwaiter().GetResult();
                foreach (var t in pending)
                    _tasks[t.TaskId] = t;
                if (pending.Count > 0)
                    _logger.LogInformation(
                        "[TaskLifecycle] Recovered {Count} pending delegated task(s) from store at startup",
                        pending.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TaskLifecycle] Startup recovery from store failed; starting with empty cache");
            }

            _expiryTimer = new Timer(
                _ => _ = ExpiryScanTickAsync(),
                state: null,
                dueTime: _expiryScanInterval,
                period: _expiryScanInterval);
        }
    }

    /// <summary>Constructor without IntentTransport (for testing / no-callback mode, in-memory only).</summary>
    public TaskLifecycleProtocol(string localAgentId, ILogger<TaskLifecycleProtocol> logger)
        : this(null, localAgentId, logger, null, null, null, null)
    {
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _expiryTimer?.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
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
        PersistAsync(task, ct).GetAwaiter().GetResult();
        _logger.LogInformation(
            "[TaskLifecycle] Accepted delegated task {TaskId} (parent={ParentRequestId}, caller={CallerAgentId}, intent={Intent})",
            taskId, parentRequestId, callerAgentId, intent);

        // Audit: log inbound delegation acceptance (fire-and-forget)
        _ = LogTaskStateChangeAsync(task, fromState: null, toState: DelegatedTaskState.Accepted,
            outcome: DelegationOutcome.Ok, error: null);

        return Task.FromResult(task);
    }

    public Task<DelegatedTask> UpdateStateAsync(string taskId, DelegatedTaskState newState, CancellationToken ct = default)
    {
        if (!_tasks.TryGetValue(taskId, out var task))
            throw new InvalidOperationException($"Delegated task '{taskId}' not found.");

        ValidateTransition(task.State, newState, taskId);

        var updated = task with { State = newState, UpdatedAt = DateTimeOffset.UtcNow };
        _tasks[taskId] = updated;
        PersistAsync(updated, ct).GetAwaiter().GetResult();

        _logger.LogInformation("[TaskLifecycle] Task {TaskId} state: {OldState} → {NewState}",
            taskId, task.State, newState);

        // Audit: log state transition
        _ = LogTaskStateChangeAsync(updated, fromState: task.State, toState: newState,
            outcome: DelegationOutcome.Ok, error: null);

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
        PersistAsync(updated, ct).GetAwaiter().GetResult();
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
        PersistAsync(updated, ct).GetAwaiter().GetResult();
        _logger.LogInformation("[TaskLifecycle] Task {TaskId} completed", taskId);

        // Resolve waiting pollers
        ResolvePollers(taskId, updated);

        // Audit
        var latencyMs = updated.CompletedAt.HasValue
            ? (updated.CompletedAt.Value - updated.CreatedAt).TotalMilliseconds
            : (double?)null;
        _ = LogTaskStateChangeAsync(updated, fromState: task.State, toState: DelegatedTaskState.Completed,
            outcome: DelegationOutcome.Ok, error: null, latencyMs, ct);

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
        PersistAsync(updated, ct).GetAwaiter().GetResult();
        _logger.LogWarning("[TaskLifecycle] Task {TaskId} failed: {Error}", taskId, error);

        ResolvePollers(taskId, updated);

        var latencyMs = (updated.CompletedAt.HasValue && task.CreatedAt != default)
            ? (updated.CompletedAt.Value - task.CreatedAt).TotalMilliseconds
            : (double?)null;
        _ = LogTaskStateChangeAsync(updated, fromState: task.State, toState: DelegatedTaskState.Failed,
            outcome: DelegationOutcome.Error, error: error, latencyMs, ct);

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
        PersistAsync(updated, ct).GetAwaiter().GetResult();
        _logger.LogInformation("[TaskLifecycle] Task {TaskId} cancelled: {Reason}", taskId, reason ?? "unknown");

        ResolvePollers(taskId, updated);

        _ = LogTaskStateChangeAsync(updated, fromState: task.State, toState: DelegatedTaskState.Cancelled,
            outcome: DelegationOutcome.Rejected, error: reason, ct: ct);

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
            PersistAsync(updated, ct).GetAwaiter().GetResult();
            _logger.LogWarning("[TaskLifecycle] Task {TaskId} expired (deadline={Deadline})", taskId, task.ExpiresAt);

            ResolvePollers(taskId, updated);

            _ = LogTaskStateChangeAsync(updated, fromState: task.State, toState: DelegatedTaskState.Expired,
                outcome: DelegationOutcome.Expired, error: "Task deadline exceeded", ct: ct);

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
        PersistAsync(updated, ct).GetAwaiter().GetResult();
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
        PersistAsync(updated, ct).GetAwaiter().GetResult();

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
            var result = await _transport.SendAsync(task.CallerAgentId, envelope, ct);
            if (result.IsSuccess)
            {
                _logger.LogDebug("[TaskLifecycle] Callback sent for task {TaskId} to {CallerAgentId}", taskId, task.CallerAgentId);
            }
            else
            {
                _logger.LogWarning("[TaskLifecycle] Callback failed for task {TaskId}: {Error}", taskId, result.ErrorMessage);
            }
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

    /// <summary>
    ///     Fire-and-forget persistence. Logs but does not throw — a transient
    ///     store failure must not corrupt the in-memory state machine. The next
    ///     state change will retry the write, and a periodic reconciler (or
    ///     restart) can recover the cache from the store's last successful row.
    /// </summary>
    private async Task PersistAsync(DelegatedTask task, CancellationToken ct)
    {
        if (_store is null) return;
        try
        {
            await _store.SaveAsync(task, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TaskLifecycle] Persist failed for task {TaskId} (state={State})", task.TaskId, task.State);
        }
    }

    /// <summary>
    ///     Background tick that finds pending tasks past their deadline and
    ///     transitions them to Expired. Errors are logged and swallowed.
    /// </summary>
    private async Task ExpiryScanTickAsync()
    {
        if (Volatile.Read(ref _disposed) != 0) return;

        try
        {
            var now = DateTimeOffset.UtcNow;
            var candidates = _tasks.Values
                .Where(t => t.ExpiresAt.HasValue
                            && t.ExpiresAt.Value <= now
                            && t.State != DelegatedTaskState.Completed
                            && t.State != DelegatedTaskState.Failed
                            && t.State != DelegatedTaskState.Cancelled
                            && t.State != DelegatedTaskState.Expired)
                .Select(t => t.TaskId)
                .ToList();

            foreach (var id in candidates)
            {
                try { await CheckExpiredAsync(id).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[TaskLifecycle] Expiry scan failed for task {TaskId}", id);
                }
            }

            // Opportunistic prune of long-since-terminal rows to keep the table small.
            if (_store is not null)
            {
                try
                {
                    // 1 hour grace after the deadline before the row is removed.
                    var pruneCutoff = now.AddHours(-1);
                    var removed = await _store.PruneExpiredAsync(pruneCutoff).ConfigureAwait(false);
                    if (removed > 0)
                        _logger.LogInformation("[TaskLifecycle] Pruned {Count} expired delegated task row(s)", removed);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[TaskLifecycle] Prune of expired rows failed");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TaskLifecycle] Expiry scan tick failed");
        }
    }

    /// <summary>
    ///     Fire-and-forget audit log for a delegated task state change.
    ///     Called from task state transition methods (Accept, Complete, Fail, Cancel, Expire).
    /// </summary>
    private async Task LogTaskStateChangeAsync(
        DelegatedTask task,
        DelegatedTaskState? fromState,
        DelegatedTaskState toState,
        DelegationOutcome outcome,
        string? error,
        double? latencyMs = null,
        CancellationToken ct = default)
    {
        if (_auditService is null) return;

        try
        {
            await _auditService.LogTaskStateChangeAsync(
                taskId: task.TaskId,
                requestId: task.ParentRequestId,
                callerAgentId: task.CallerAgentId,
                localAgentId: _localAgentId,
                intent: task.Intent,
                fromState: fromState?.ToString() ?? "null",
                toState: toState.ToString(),
                outcome: outcome,
                error: error,
                latencyMs: latencyMs,
                ct: ct);
        }
        catch
        {
            // Best-effort audit; do not propagate
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
