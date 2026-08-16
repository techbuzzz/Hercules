using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Hercules.Observability;

/// <summary>
///     Metric instruments for Hercules.
///     All counters and histograms are pre-created once at static initialization.
///     Zero-overhead no-op when OTel is disabled (ActivitySource returns NoopActivity).
/// </summary>
public static class OtelMetrics
{
    // ── Explicit bucket boundaries (task_085) ────────────────────────────────
    // Default OpenTelemetry histograms use a small set of generic buckets that
    // produce poor resolution in the SLO-relevant ranges. The boundaries below
    // are tuned for typical LLM/tool latencies and token counts we observe in
    // production dashboards.

    /// <summary>Latency (ms) buckets: 5ms … 10s. Tuned for LLM/tool/handler latency.</summary>
    public static readonly double[] LatencyBucketsMs =
    [
        5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000
    ];

    /// <summary>Token count buckets: 10 … 32k. Tuned for prompt/completion sizing.</summary>
    public static readonly double[] TokenBuckets =
    [
        10, 50, 100, 500, 1000, 2000, 4000, 8000, 16000, 32000
    ];

    // ── Counters ────────────────────────────────────────────────────────────

    /// <summary>Total number of HandleAsync calls.</summary>
    public static readonly Counter<long> HandleCounter =
        OtelSetup.Meter.CreateCounter<long>(
            "hercules.handle.count",
            unit: "{call}",
            description: "Total number of agent HandleAsync invocations");

    /// <summary>Total number of LLM API calls.</summary>
    public static readonly Counter<long> LlmCallCounter =
        OtelSetup.Meter.CreateCounter<long>(
            "hercules.llm.call.count",
            unit: "{call}",
            description: "Total number of LLM completion calls");

    /// <summary>Total number of tool executions (success + failure).</summary>
    public static readonly Counter<long> ToolCallCounter =
        OtelSetup.Meter.CreateCounter<long>(
            "hercules.tool.call.count",
            unit: "{call}",
            description: "Total number of tool executions");

    /// <summary>Total number of skill routing hits.</summary>
    public static readonly Counter<long> SkillHitCounter =
        OtelSetup.Meter.CreateCounter<long>(
            "hercules.skill.hit.count",
            unit: "{hit}",
            description: "Total number of skill routing matches");

    /// <summary>
    ///     Total number of LLM retry attempts (task_085).
    ///     Incremented on every retryable failure — never sampled, unlike the
    ///     structured warning log which is sampled 1-in-N to avoid log flooding.
    /// </summary>
    public static readonly Counter<long> LlmRetryCounter =
        OtelSetup.Meter.CreateCounter<long>(
            "hercules.llm.retry.count",
            unit: "{retry}",
            description: "Total number of LLM retry attempts (per-provider/per-attempt)");

    /// <summary>
    ///     Total number of messages dropped because a bounded in-memory event bus
    ///     channel was full and backpressure timed out (task_086).
    /// </summary>
    public static readonly Counter<long> BusChannelDropCounter =
        OtelSetup.Meter.CreateCounter<long>(
            "hercules.bus.channel.drop.count",
            unit: "{message}",
            description: "Number of bus messages dropped due to bounded channel overflow");

    /// <summary>
    ///     Total number of mesh handler invocations dropped because the
    ///     in-process mesh bus handler semaphore was exhausted (task_086).
    /// </summary>
    public static readonly Counter<long> MeshHandlerDropCounter =
        OtelSetup.Meter.CreateCounter<long>(
            "hercules.mesh.handler.drop.count",
            unit: "{message}",
            description: "Number of mesh envelopes dropped because handler concurrency limit was reached");

    // ── Histograms ───────────────────────────────────────────────────────────

    /// <summary>Duration of HandleAsync in milliseconds.</summary>
    public static readonly Histogram<double> HandleDurationHistogram =
        OtelSetup.Meter.CreateHistogram<double>(
            "hercules.handle.duration_ms",
            unit: "ms",
            description: "HandleAsync duration in milliseconds");

    /// <summary>Duration of a single LLM call in milliseconds.</summary>
    public static readonly Histogram<double> LlmCallDurationHistogram =
        OtelSetup.Meter.CreateHistogram<double>(
            "hercules.llm.call.duration_ms",
            unit: "ms",
            description: "LLM completion call duration in milliseconds");

    /// <summary>Duration of a single tool execution in milliseconds.</summary>
    public static readonly Histogram<double> ToolCallDurationHistogram =
        OtelSetup.Meter.CreateHistogram<double>(
            "hercules.tool.call.duration_ms",
            unit: "ms",
            description: "Tool execution duration in milliseconds");

    /// <summary>Number of input tokens per LLM call.</summary>
    public static readonly Histogram<long> LlmInputTokensHistogram =
        OtelSetup.Meter.CreateHistogram<long>(
            "hercules.llm.input_tokens",
            unit: "{token}",
            description: "Number of input tokens per LLM call");

    /// <summary>Number of output tokens per LLM call.</summary>
    public static readonly Histogram<long> LlmOutputTokensHistogram =
        OtelSetup.Meter.CreateHistogram<long>(
            "hercules.llm.output_tokens",
            unit: "{token}",
            description: "Number of output tokens per LLM call");

    // ── Sampled logging helper (task_085) ────────────────────────────────────

    /// <summary>
    ///     Returns true when the caller should emit a sampled warning log for
    ///     a high-frequency event (retries, quota warnings, guardrail hits).
    ///     Use <see cref="LlmRetryCounter"/> and similar counters to track the
    ///     real volume.
    /// </summary>
    /// <param name="counter">Monotonic counter (Interlocked.Increment).</param>
    /// <param name="sampleRate">
    ///     1 = log every event. N = log every Nth event.
    ///     Values &lt;=0 are treated as "never log".
    /// </param>
    /// <returns>
    ///     True if the structured warning should be emitted for this occurrence.
    /// </returns>
    public static bool ShouldLogSampledWarning(ref long counter, int sampleRate)
    {
        if (sampleRate <= 0)
        {
            // Still bump the counter so volume metrics remain accurate.
            Interlocked.Increment(ref counter);
            return false;
        }

        var count = Interlocked.Increment(ref counter);
        return sampleRate == 1 || (count % sampleRate) == 0;
    }
}
