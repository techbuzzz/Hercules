using System.Collections.Concurrent;
using System.Diagnostics;
using Hercules.Observability;
using Microsoft.Extensions.Hosting;

namespace Hercules.Mesh.Observability;

/// <summary>
///     In-memory ActivityListener that aggregates completed spans from
///     <see cref="OtelSetup.Source"/> and pushes a single <see cref="TraceSummary"/>
///     per <c>trace_id</c> into <see cref="MeshDiagnosticsService"/> (task_093).
///     Aggregation strategy: when the LAST span in a trace completes, emit one
///     summary. We detect "last span" by tracking the number of in-flight spans
///     per trace id; when the count drops to zero, the trace is closed.
///     Spans with no parent (i.e. root spans) carry the root operation name; child
///     spans only update <c>SpanCount</c> on the same trace id.
///     Registered as <see cref="IHostedService"/> so the listener is attached
///     at process startup, before any spans are produced.
/// </summary>
public sealed class InMemoryActivityListener : IHostedService, IDisposable
{
    private readonly MeshDiagnosticsService _diagnostics;
    private readonly ConcurrentDictionary<string, TraceAggregate> _traces = new(StringComparer.Ordinal);
    private ActivityListener? _listener;

    public InMemoryActivityListener(MeshDiagnosticsService diagnostics)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == OtelSetup.ServiceName,
            // We want the full span payload so we can read tags (root name, peer agent id, intent).
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = OnActivityStarted,
            ActivityStopped = OnActivityStopped
        };
        ActivitySource.AddActivityListener(_listener);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    private void OnActivityStarted(Activity activity)
    {
        if (activity is null) return;
        var traceId = activity.TraceId.ToString();
        if (string.IsNullOrEmpty(traceId) || traceId == "00000000000000000000000000000000") return;

        var agg = _traces.GetOrAdd(traceId, _ => new TraceAggregate());
        Interlocked.Increment(ref agg.InFlightCount);
        if (string.IsNullOrEmpty(agg.RootName))
        {
            // First span in this trace is the root until proven otherwise.
            agg.RootName = activity.OperationName;
            agg.RootStart = activity.StartTimeUtc;
        }
        agg.LastTouched = DateTimeOffset.UtcNow;
    }

    private void OnActivityStopped(Activity activity)
    {
        if (activity is null) return;
        var traceId = activity.TraceId.ToString();
        if (string.IsNullOrEmpty(traceId) || traceId == "00000000000000000000000000000000") return;

        var agg = _traces.GetOrAdd(traceId, _ => new TraceAggregate());
        Interlocked.Increment(ref agg.TotalSpans);
        var remaining = Interlocked.Decrement(ref agg.InFlightCount);

        // Promote error status up — the trace is "Error" if any span errored.
        if (activity.Status == ActivityStatusCode.Error)
        {
            agg.HasError = true;
        }
        agg.LastTouched = DateTimeOffset.UtcNow;

        if (remaining == 0)
        {
            // Trace closed: emit summary and remove aggregate. If a concurrent
            // span for the same trace id arrived after, it'll re-aggregate.
            if (_traces.TryRemove(traceId, out var snapshot))
            {
                var startedAt = snapshot.RootStart == default
                    ? DateTimeOffset.UtcNow
                    : new DateTimeOffset(snapshot.RootStart, TimeSpan.Zero);
                var durationMs = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
                var summary = new TraceSummary
                {
                    TraceId = traceId,
                    RootName = string.IsNullOrEmpty(snapshot.RootName) ? "unknown" : snapshot.RootName,
                    StartedAt = startedAt,
                    DurationMs = Math.Max(0, durationMs),
                    Status = snapshot.HasError ? "Error" : "Ok",
                    SpanCount = snapshot.TotalSpans
                };
                _diagnostics.AppendTrace(summary);
            }
        }
    }

    public void Dispose() => _listener?.Dispose();

    /// <summary>Mutable per-trace aggregate maintained while spans are in flight.</summary>
    private sealed class TraceAggregate
    {
        public int InFlightCount;
        public int TotalSpans;
        public string RootName = string.Empty;
        public DateTime RootStart;
        public bool HasError;
        public DateTimeOffset LastTouched;
    }
}
