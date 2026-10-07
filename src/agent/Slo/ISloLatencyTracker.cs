namespace Hercules.Slo;

/// <summary>
///     In-process latency tracker that feeds the SLO service with real
///     response-time measurements (task_087). Previously the SLO service
///     synthesised a P95 by extrapolating from the audit row count — which
///     had no correlation to actual handler latency. The tracker is a
///     cheap, lock-free ring buffer keyed by intent, fed by the
///     <see cref="Hercules.Mesh.Resilience.ResilientTransport"/> on every
///     completed send (success and failure alike).
/// </summary>
public interface ISloLatencyTracker
{
    /// <summary>Record one latency sample.</summary>
    /// <param name="intent">Capability or intent name. Empty falls into a global bucket.</param>
    /// <param name="latencyMs">Measured end-to-end latency in milliseconds.</param>
    void RecordSample(string intent, double latencyMs);

    /// <summary>
    ///     Compute the 95th-percentile latency over the recent window
    ///     (default 24h, sample-count capped). Returns 0 when no samples
    ///     are available so the caller can fall back to its default.
    /// </summary>
    /// <param name="intent">Optional intent filter. Null/empty aggregates across all intents.</param>
    /// <param name="window">Time window. Samples older than this are ignored.</param>
    double GetP95Ms(string? intent = null, TimeSpan? window = null);

    /// <summary>Number of samples currently retained (sum across all intents).</summary>
    int SampleCount { get; }

    /// <summary>Reset the tracker (used by tests).</summary>
    void Reset();
}
