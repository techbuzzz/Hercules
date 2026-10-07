using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Observability;

/// <summary>
///     In-memory mesh diagnostics aggregator (task_093 / Phase 7 web-mesh-observability).
///     Tracks:
///     - Counters since process start (totals + by capability + by peer)
///     - Ring buffer of the last N completed spans (for the UI trace table)
///     - Ring buffer of the last M log entries (for the UI log viewer)
///     All operations are lock-free or use a single short critical section per buffer;
///     readers see a stable snapshot via ToList() on the snapshot side.
///     Bounded memory: counters grow as new keys arrive, but trace/log buffers are
///     strictly capped to <see cref="MaxTraces"/> and <see cref="MaxLogs"/> entries.
/// </summary>
public sealed class MeshDiagnosticsService
{
    /// <summary>Maximum number of recent spans retained in memory (default: 100).</summary>
    public const int MaxTraces = 100;

    /// <summary>Maximum number of recent log entries retained in memory (default: 200).</summary>
    public const int MaxLogs = 200;

    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    // ── Counters (per metric name, with by-capability and by-peer maps) ─────
    private long _routingDecisionCount;
    private long _retryAttemptCount;
    private long _circuitBreakerStateChangeCount;
    private long _delegationCount;
    private long _meshBackendHealthCount;

    // Metric → capability → count. Bounded by number of registered capabilities.
    private readonly ConcurrentDictionary<string, long> _routingByCapability = new(StringComparer.Ordinal);

    // Metric → peer_agent_id → count. Bounded by number of peers.
    private readonly ConcurrentDictionary<string, long> _routingByPeer = new(StringComparer.OrdinalIgnoreCase);

    // ── Ring buffer of completed spans ──────────────────────────────────────
    private readonly TraceRingBuffer _traces = new(MaxTraces);

    // ── Ring buffer of log entries ──────────────────────────────────────────
    private readonly LogRingBuffer _logs = new(MaxLogs);

    /// <summary>Process-start timestamp (UTC).</summary>
    public DateTimeOffset StartedAt => _startedAt;

    /// <summary>Number of <c>routing_decision</c> events observed since start.</summary>
    public long RoutingDecisionCount => Interlocked.Read(ref _routingDecisionCount);

    /// <summary>Number of <c>retry_attempt</c> events observed since start.</summary>
    public long RetryAttemptCount => Interlocked.Read(ref _retryAttemptCount);

    /// <summary>Number of <c>circuit_breaker_state_change</c> events observed since start.</summary>
    public long CircuitBreakerStateChangeCount => Interlocked.Read(ref _circuitBreakerStateChangeCount);

    /// <summary>Number of <c>delegation</c> events observed since start.</summary>
    public long DelegationCount => Interlocked.Read(ref _delegationCount);

    /// <summary>Number of <c>mesh_backend_health</c> events observed since start.</summary>
    public long MeshBackendHealthCount => Interlocked.Read(ref _meshBackendHealthCount);

    /// <summary>Counter increments keyed by capability name (routing_decision only).</summary>
    public IReadOnlyDictionary<string, long> RoutingByCapability => _routingByCapability;

    /// <summary>Counter increments keyed by peer agent id (delegation only).</summary>
    public IReadOnlyDictionary<string, long> RoutingByPeer => _routingByPeer;

    /// <summary>Snapshot of recent completed spans (newest first).</summary>
    public IReadOnlyList<TraceSummary> RecentTraces(int limit)
    {
        if (limit <= 0) return Array.Empty<TraceSummary>();
        var cap = Math.Min(limit, MaxTraces);
        return _traces.Snapshot(cap);
    }

    /// <summary>Snapshot of recent log entries (newest first), filtered by minimum level.</summary>
    public IReadOnlyList<LogEntrySummary> RecentLogs(int limit, LogLevel? minLevel = null)
    {
        if (limit <= 0) return Array.Empty<LogEntrySummary>();
        var cap = Math.Min(limit, MaxLogs);
        return _logs.Snapshot(cap, minLevel);
    }

    // ── Mutators (called from MeshObservabilityService / InMemoryActivityListener / InMemoryLogSink) ──

    /// <summary>
    ///     Increment counter for a mesh metric. Best-effort: silently no-ops on unknown metrics.
    ///     Mirrors the dispatch in <see cref="MeshObservabilityService.RecordMeshMetric"/>.
    /// </summary>
    public void RecordMetric(string metricName, double value, string? intent = null, string? peerAgentId = null)
    {
        switch (metricName)
        {
            case "routing_decision":
                Interlocked.Increment(ref _routingDecisionCount);
                if (!string.IsNullOrWhiteSpace(intent))
                {
                    _routingByCapability.AddOrUpdate(intent, 1, (_, c) => c + 1);
                }
                break;
            case "retry_attempt":
                Interlocked.Increment(ref _retryAttemptCount);
                break;
            case "circuit_breaker_state_change":
                Interlocked.Increment(ref _circuitBreakerStateChangeCount);
                break;
            case "delegation":
                Interlocked.Increment(ref _delegationCount);
                if (!string.IsNullOrWhiteSpace(peerAgentId))
                {
                    _routingByPeer.AddOrUpdate(peerAgentId, 1, (_, c) => c + 1);
                }
                break;
            case "mesh_backend_health":
                Interlocked.Increment(ref _meshBackendHealthCount);
                break;
            // Latency/histogram metrics and unknown names are ignored on the counter side.
        }
    }

    /// <summary>Append a completed Activity to the traces ring buffer.</summary>
    internal void AppendTrace(TraceSummary summary) => _traces.Push(summary);

    /// <summary>Append a captured log entry to the logs ring buffer.</summary>
    internal void AppendLog(LogEntrySummary entry) => _logs.Push(entry);

    /// <summary>
    ///     Immutable snapshot of mesh diagnostics counters, exposed via the web API.
    ///     Computed at call time — callers can serialize it directly.
    /// </summary>
    public MeshDiagnosticsSnapshot Snapshot()
    {
        var now = DateTimeOffset.UtcNow;
        return new MeshDiagnosticsSnapshot
        {
            From = _startedAt,
            To = now,
            RoutingDecisionCount = RoutingDecisionCount,
            RetryAttemptCount = RetryAttemptCount,
            CircuitBreakerStateChangeCount = CircuitBreakerStateChangeCount,
            DelegationCount = DelegationCount,
            MeshBackendHealthCount = MeshBackendHealthCount,
            ByCapability = new Dictionary<string, long>(_routingByCapability),
            ByPeer = new Dictionary<string, long>(_routingByPeer, StringComparer.OrdinalIgnoreCase)
        };
    }
}

/// <summary>Snapshot of mesh diagnostics counters at a point in time.</summary>
public sealed record MeshDiagnosticsSnapshot
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public long RoutingDecisionCount { get; init; }
    public long RetryAttemptCount { get; init; }
    public long CircuitBreakerStateChangeCount { get; init; }
    public long DelegationCount { get; init; }
    public long MeshBackendHealthCount { get; init; }
    public IReadOnlyDictionary<string, long> ByCapability { get; init; } = new Dictionary<string, long>();
    public IReadOnlyDictionary<string, long> ByPeer { get; init; } = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Single completed-trace summary, aggregated across spans in the same trace.</summary>
public sealed record TraceSummary
{
    public required string TraceId { get; init; }
    public required string RootName { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required double DurationMs { get; init; }
    /// <summary>"Ok" | "Error" | "Unset".</summary>
    public required string Status { get; init; }
    public required int SpanCount { get; init; }
}

/// <summary>Single captured log entry.</summary>
public sealed record LogEntrySummary
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Level { get; init; }
    public required string Source { get; init; }
    public string? RequestId { get; init; }
    public string? TraceId { get; init; }
    public required string Message { get; init; }
    public IReadOnlyDictionary<string, object?> StructuredFields { get; init; } = new Dictionary<string, object?>();
}

/// <summary>
///     Bounded ring buffer of trace summaries. Single-writer, multi-reader.
///     When the buffer is full, the oldest entry is overwritten (FIFO eviction).
/// </summary>
internal sealed class TraceRingBuffer
{
    private readonly TraceSummary?[] _buffer;
    private readonly int _capacity;
    private long _writeIndex; // monotonically increasing; position = index % capacity

    public TraceRingBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _buffer = new TraceSummary?[capacity];
    }

    public void Push(TraceSummary summary)
    {
        var idx = Interlocked.Increment(ref _writeIndex) - 1;
        Volatile.Write(ref _buffer[idx % _capacity], summary);
    }

    public IReadOnlyList<TraceSummary> Snapshot(int limit)
    {
        var written = (int)Math.Min(_writeIndex, _capacity);
        if (written == 0) return Array.Empty<TraceSummary>();
        var take = Math.Min(limit, written);
        // Newest entries are at the highest indices. We walk backwards to
        // preserve newest-first ordering without allocating a sort.
        var result = new List<TraceSummary>(take);
        for (var i = 0; i < take; i++)
        {
            var ringIndex = (_writeIndex - 1 - i + _capacity) % _capacity;
            var entry = Volatile.Read(ref _buffer[ringIndex]);
            if (entry is not null) result.Add(entry);
        }
        return result;
    }
}

/// <summary>
///     Bounded ring buffer of log entries. Single-writer, multi-reader.
///     Filters by minimum level at snapshot time so the caller's filter takes
///     effect after the cap (entries below the threshold are still retained in
///     the buffer in case the caller raises the threshold later).
/// </summary>
internal sealed class LogRingBuffer
{
    private readonly LogEntrySummary?[] _buffer;
    private readonly int _capacity;
    private long _writeIndex;

    public LogRingBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _buffer = new LogEntrySummary?[capacity];
    }

    public void Push(LogEntrySummary entry)
    {
        var idx = Interlocked.Increment(ref _writeIndex) - 1;
        Volatile.Write(ref _buffer[idx % _capacity], entry);
    }

    public IReadOnlyList<LogEntrySummary> Snapshot(int limit, LogLevel? minLevel)
    {
        var written = (int)Math.Min(_writeIndex, _capacity);
        if (written == 0) return Array.Empty<LogEntrySummary>();

        // Build newest-first, respecting the min level. We walk the entire
        // written range so the filter can reject low-priority entries that
        // were already written (the buffer keeps the raw stream).
        var result = new List<LogEntrySummary>(Math.Min(limit, written));
        for (var i = 0; i < written && result.Count < limit; i++)
        {
            var ringIndex = (_writeIndex - 1 - i + _capacity) % _capacity;
            var entry = Volatile.Read(ref _buffer[ringIndex]);
            if (entry is null) continue;
            if (minLevel.HasValue && !MeetsMinLevel(entry.Level, minLevel.Value)) continue;
            result.Add(entry);
        }
        return result;
    }

    /// <summary>
    ///     LogLevel comparison for filtering. We compare on the canonical MS
    ///     severity scale so callers can pass any built-in or custom level.
    /// </summary>
    private static bool MeetsMinLevel(string entryLevel, LogLevel min)
    {
        if (!TryParseLevel(entryLevel, out var actual)) return true; // unknown level passes filter
        return actual >= min;
    }

    private static bool TryParseLevel(string s, out LogLevel level)
    {
        switch ((s ?? "").Trim().ToLowerInvariant())
        {
            case "trace": level = LogLevel.Trace; return true;
            case "debug": level = LogLevel.Debug; return true;
            case "info":
            case "information": level = LogLevel.Information; return true;
            case "warn":
            case "warning": level = LogLevel.Warning; return true;
            case "error": level = LogLevel.Error; return true;
            case "critical":
            case "fatal": level = LogLevel.Critical; return true;
            default: level = LogLevel.None; return false;
        }
    }
}
