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
}
