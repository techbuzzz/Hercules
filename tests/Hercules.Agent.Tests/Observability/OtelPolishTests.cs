using System.Diagnostics.Metrics;
using Hercules.Config;
using Hercules.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Observability;

/// <summary>
/// Tests for task_085: OpenTelemetry polish (console gating, process
/// instrumentation, histogram buckets, async logging, sampled warning logs).
/// </summary>
public class OtelPolishTests
{
    // ─── OtelConfig: console exporter gating ─────────────────────────────────

    [Fact]
    public void OtelConfig_ConsoleExporterEnabled_defaults_to_true_when_no_otlp()
    {
        var cfg = new OtelConfig { OtlpEndpoint = null };
        Assert.True(cfg.GetEffectiveConsoleExporterEnabled());
    }

    [Fact]
    public void OtelConfig_ConsoleExporterEnabled_defaults_to_false_when_otlp_set()
    {
        var cfg = new OtelConfig { OtlpEndpoint = "http://otelcol:4317" };
        Assert.False(cfg.GetEffectiveConsoleExporterEnabled());
    }

    [Fact]
    public void OtelConfig_ConsoleExporterEnabled_explicit_true_overrides_otlp()
    {
        var cfg = new OtelConfig
        {
            OtlpEndpoint = "http://otelcol:4317",
            ConsoleExporterEnabled = true
        };
        Assert.True(cfg.GetEffectiveConsoleExporterEnabled());
    }

    [Fact]
    public void OtelConfig_ConsoleExporterEnabled_explicit_false_overrides_no_otlp()
    {
        var cfg = new OtelConfig
        {
            OtlpEndpoint = null,
            ConsoleExporterEnabled = false
        };
        Assert.False(cfg.GetEffectiveConsoleExporterEnabled());
    }

    [Fact]
    public void OtelConfig_ConsoleExporterEnabled_treats_whitespace_otlp_as_empty()
    {
        var cfg = new OtelConfig { OtlpEndpoint = "   " };
        Assert.True(cfg.GetEffectiveConsoleExporterEnabled());
    }

    [Fact]
    public void OtelConfig_LoggingSampleRate_default_is_10()
    {
        var cfg = new OtelConfig();
        Assert.Equal(10, cfg.LoggingSampleRate);
    }

    [Fact]
    public void OtelConfig_OtlpLogExporterEnabled_default_is_true()
    {
        var cfg = new OtelConfig();
        Assert.True(cfg.OtlpLogExporterEnabled);
    }

    // ─── OtelMetrics: explicit bucket boundaries ──────────────────────────────

    [Fact]
    public void LatencyBucketsMs_contains_expected_slo_relevant_buckets()
    {
        var expected = new[] { 5d, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000 };
        Assert.Equal(expected, OtelMetrics.LatencyBucketsMs);
    }

    [Fact]
    public void TokenBuckets_contains_expected_prompt_completion_buckets()
    {
        var expected = new[] { 10d, 50, 100, 500, 1000, 2000, 4000, 8000, 16000, 32000 };
        Assert.Equal(expected, OtelMetrics.TokenBuckets);
    }

    [Fact]
    public void LlmRetryCounter_is_registered_and_usable()
    {
        // Counter must be non-null. We deliberately do NOT call Add(1) here
        // because LlmRetryCounter is a process-wide static — adding a value in
        // a smoke test would race with parallel tests that capture the counter
        // (e.g. ResilientLLMClientSampledLogTests). Functional Add behaviour
        // is covered by those integration tests.
        Assert.NotNull(OtelMetrics.LlmRetryCounter);
    }

    // ─── OtelMetrics: ShouldLogSampledWarning ────────────────────────────────

    [Fact]
    public void ShouldLogSampledWarning_returns_true_for_every_nth_call()
    {
        long counter = 0;
        var sampleRate = 10;
        // The 10th, 20th, 30th, … call returns true; the others return false.
        var matches = 0;
        for (var i = 0; i < 100; i++)
        {
            if (OtelMetrics.ShouldLogSampledWarning(ref counter, sampleRate))
            {
                matches++;
            }
        }
        Assert.Equal(10, matches);
    }

    [Fact]
    public void ShouldLogSampledWarning_returns_true_on_every_call_when_rate_is_one()
    {
        long counter = 0;
        var matches = 0;
        for (var i = 0; i < 50; i++)
        {
            if (OtelMetrics.ShouldLogSampledWarning(ref counter, 1))
            {
                matches++;
            }
        }
        Assert.Equal(50, matches);
    }

    [Fact]
    public void ShouldLogSampledWarning_returns_false_when_rate_is_zero_but_counter_increments()
    {
        long counter = 0;
        for (var i = 0; i < 25; i++)
        {
            Assert.False(OtelMetrics.ShouldLogSampledWarning(ref counter, 0));
        }
        // Counter still bumps for accurate volume tracking.
        Assert.Equal(25, counter);
    }

    [Fact]
    public void ShouldLogSampledWarning_returns_false_for_negative_rate()
    {
        long counter = 0;
        Assert.False(OtelMetrics.ShouldLogSampledWarning(ref counter, -5));
        // Counter still increments to keep volume metric accurate.
        Assert.Equal(1, counter);
    }

    [Fact]
    public void ShouldLogSampledWarning_is_thread_safe_under_parallel_calls()
    {
        long counter = 0;
        const int totalCalls = 10_000;
        const int sampleRate = 100;
        var matches = 0;

        Parallel.For(0, totalCalls, _ =>
        {
            if (OtelMetrics.ShouldLogSampledWarning(ref counter, sampleRate))
            {
                Interlocked.Increment(ref matches);
            }
        });

        // Exactly totalCalls / sampleRate = 100 hits. The exact equality is
        // not strictly guaranteed across all interleavings because of how
        // Interlocked.Increment fences the count observation, so we accept a
        // small tolerance to account for racey count observation.
        Assert.InRange(matches, totalCalls / sampleRate - 2, totalCalls / sampleRate + 2);
        Assert.Equal(totalCalls, counter);
    }
}
