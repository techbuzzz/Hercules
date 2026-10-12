using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Hercules.CodeExecution;

namespace Hercules.WebApi.CodeRuns;

/// <summary>Lifecycle of a sandbox run.</summary>
public enum RunStatus
{
    Queued,
    Running,
    Completed,
    Failed
}

/// <summary>A single SSE event. <see cref="Seq"/> lets a reconnecting client resume.</summary>
public sealed record RunEvent(
    long Seq,
    string Type,
    object? Data,
    [property: JsonPropertyName("at")] DateTimeOffset At = default);

/// <summary>
/// A sandbox execution started by <c>POST /api/code/run</c> and observed over SSE.
/// </summary>
public sealed class CodeRun
{
    private readonly List<RunEvent> _events = [];
    private readonly object _gate = new();
    private readonly TaskCompletionSource _finished =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private long _seq;

    public required string Id { get; init; }
    public string? SkillId { get; init; }
    public string Language { get; init; } = "csharp";
    public RunStatus Status { get; private set; } = RunStatus.Queued;
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; private set; }
    public ExecutionResult? Result { get; private set; }
    public string? Error { get; private set; }

    /// <summary>True when older events were dropped and the client missed some.</summary>
    public bool Truncated { get; private set; }

    /// <summary>Completed when the run reaches a terminal state.</summary>
    public Task Finished => _finished.Task;

    public void MarkRunning()
    {
        lock (_gate) Status = RunStatus.Running;
    }

    public void Complete(ExecutionResult result)
    {
        lock (_gate)
        {
            Result = result;
            Status = RunStatus.Completed;
            CompletedAt = DateTimeOffset.UtcNow;
        }
        _finished.TrySetResult();
    }

    public void Fail(string error)
    {
        lock (_gate)
        {
            Error = error;
            Status = RunStatus.Failed;
            CompletedAt = DateTimeOffset.UtcNow;
        }
        _finished.TrySetResult();
    }

    /// <summary>Appends an event, honouring the replay bound.</summary>
    public RunEvent Append(string type, object? data)
    {
        lock (_gate)
        {
            var evt = new RunEvent(++_seq, type, data, DateTimeOffset.UtcNow);
            _events.Add(evt);
            if (_events.Count > SkillRunStore.MaxBufferedEvents)
            {
                _events.RemoveRange(0, _events.Count - SkillRunStore.MaxBufferedEvents);
                Truncated = true;
            }
            return evt;
        }
    }

    /// <summary>Snapshot of everything after <paramref name="afterSeq"/> for stream replay.</summary>
    public (IReadOnlyList<RunEvent> Events, bool Truncated, bool Terminal) Snapshot(long afterSeq)
    {
        lock (_gate)
        {
            var pending = _events.Where(e => e.Seq > afterSeq).ToArray();
            var terminal = Status is RunStatus.Completed or RunStatus.Failed;
            return (pending, Truncated, terminal);
        }
    }

    public object StatusSnapshot() => new
    {
        runId = Id,
        skillId = SkillId,
        language = Language,
        status = Status.ToString().ToLowerInvariant(),
        startedAt = StartedAt,
        completedAt = CompletedAt,
        error = Error,
        result = Result is null
            ? null
            : new
            {
                exitCode = Result.ExitCode,
                stdout = Result.Stdout,
                stderr = Result.Stderr,
                durationMs = Result.DurationMs,
                status = Result.Status,
                blockedPatterns = Result.BlockedPatterns,
                sessionDir = Result.SessionDir,
                success = Result.IsSuccess,
            }
    };
}

/// <summary>
/// In-memory registry of sandbox runs.
/// <para>
/// Deliberately in-memory: runs are ephemeral execution state, and an agent
/// restart invalidating them is the correct, safe behaviour. The event buffer is
/// bounded so a long-running agent cannot accumulate unbounded memory — a
/// reconnecting client is told the buffer was truncated instead of silently
/// receiving a gap.
/// </para>
/// </summary>
public sealed class SkillRunStore(ILogger<SkillRunStore> logger, TimeProvider? clock = null)
{
    /// <summary>Max events retained per run for SSE replay.</summary>
    public const int MaxBufferedEvents = 256;

    /// <summary>Completed runs are evicted this long after completion.</summary>
    public static readonly TimeSpan CompletedTtl = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, CodeRun> _runs = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public CodeRun Create(string? skillId, string language)
    {
        Sweep();
        var run = new CodeRun { Id = Guid.NewGuid().ToString("n"), SkillId = skillId, Language = language };
        _runs[run.Id] = run;
        return run;
    }

    public CodeRun? Get(string id) => _runs.TryGetValue(id, out var run) ? run : null;

    private void Sweep()
    {
        var cutoff = _clock.GetUtcNow() - CompletedTtl;
        foreach (var (id, run) in _runs)
        {
            if (run.CompletedAt is { } done && done < cutoff && _runs.TryRemove(id, out _))
            {
                logger.LogDebug("[CodeRun] Evicted completed run {RunId}", id);
            }
        }
    }
}