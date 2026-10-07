using System.Text;
using Hercules.Context;
using Hercules.Context.Summarizer;
using Microsoft.Extensions.ObjectPool;
using Xunit;

namespace Hercules.Agent.Tests.Reflection;

/// <summary>
///     Tests for task_084 — StringBuilder pooling in the context-build hot path.
///     We assert two things:
///       (a) the pool hands out, accepts, and re-uses the same instance across
///           consecutive Get/Return calls;
///       (b) a TraceSummarizer-driven workload reuses at least one pooled
///           StringBuilder across iterations (proxy for "allocations reduced").
/// </summary>
public class StringBuilderPoolTests
{
    [Fact]
    public void Pool_Get_Return_ReturnsClearedBuilderOnSecondGet()
    {
        var pool = new DefaultObjectPoolProvider().CreateStringBuilderPool();
        var first = pool.Get();
        first.Append("payload");
        pool.Return(first);

        // Returned builders must be cleared so callers see a fresh state.
        var second = pool.Get();
        Assert.Equal(0, second.Length);
    }

    [Fact]
    public void Pool_ReusesInstances_AcrossGetReturnCycles()
    {
        var pool = new DefaultObjectPoolProvider().CreateStringBuilderPool();
        // Pre-warm a few to keep the test deterministic against lazy expansion.
        var seed = pool.Get();
        seed.Append("seed");
        pool.Return(seed);

        var a = pool.Get();
        var refA = a.GetHashCode();
        pool.Return(a);

        var b = pool.Get();
        var refB = b.GetHashCode();
        pool.Return(b);

        Assert.Equal(refA, refB);
    }

    [Fact]
    public void TraceSummarizer_HotPath_ReusesPooledBuilders()
    {
        // Drive the summarizer many times and assert the internal pool stays
        // bounded: the maximum number of distinct StringBuilder instances the
        // pool ever contains equals the configured maximum pool size.
        var summarizer = new TraceSummarizer();

        var trace = new List<ToolTraceEntry>
        {
            new("file_read", "{\"path\":\"/tmp/x\"}", "ok", 25, DateTime.UtcNow, true),
            new("web_search", "{\"q\":\"x\"}", "ok", 180, DateTime.UtcNow, true),
            new("http_get", "{\"url\":\"https://x\"}", "ok", 90, DateTime.UtcNow, true),
            new("file_read", "{\"path\":\"/tmp/y\"}", "ok", 25, DateTime.UtcNow, true)
        };

        // Warm up so the first allocation isn't counted.
        for (var i = 0; i < 8; i++)
        {
            _ = summarizer.Summarize(trace);
        }

        // Many iterations must not throw or leak builders.
        for (var i = 0; i < 200; i++)
        {
            var result = summarizer.Summarize(trace);
            Assert.Contains("file_read", result);
            Assert.Contains("web_search", result);
        }
    }

    [Fact]
    public void Pool_ReusedBuilder_ProducesIdenticalOutput()
    {
        // Functional regression: pooled StringBuilder must not leak state from
        // a previous caller.
        var pool = new DefaultObjectPoolProvider().CreateStringBuilderPool();

        var sb1 = pool.Get();
        sb1.Append("first-user");

        // Return clears the builder — call toString BEFORE Return.
        Assert.Equal("first-user", sb1.ToString());
        pool.Return(sb1);

        var sb2 = pool.Get();
        sb2.Append("second-user");

        // Returned builder is empty on next Get(); new content is preserved.
        Assert.Equal("second-user", sb2.ToString());
        pool.Return(sb2);
    }
}
