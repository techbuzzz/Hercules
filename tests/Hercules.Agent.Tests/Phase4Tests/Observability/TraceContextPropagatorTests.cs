using System.Diagnostics;
using Hercules.Mesh.Observability;
using Xunit;

namespace Hercules.Agent.Tests.Phase4Tests.Observability;

public class TraceContextPropagatorTests
{
    // ─── Inject ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Inject_W3C_ProducesValidTraceparent()
    {
        var prop = new TraceContextPropagator("w3c");
        using var activity = new ActivitySource("test").StartActivity("op");

        var headers = prop.Inject(activity, null, null);

        Assert.True(headers.TryGetValue("traceparent", out var tp));
        Assert.StartsWith("00-", tp);
        var parts = tp.Split('-');
        Assert.Equal(4, parts.Length);
        Assert.Equal(32, parts[1].Length); // traceId
        Assert.Equal(16, parts[2].Length); // spanId
        Assert.Equal(2, parts[3].Length);  // flags
    }

    [Fact]
    public void Inject_W3C_Activity_HasValidTraceparent()
    {
        var prop = new TraceContextPropagator("w3c");
        using var source = new ActivitySource("test");
        using var activity = source.StartActivity("op");

        var headers = prop.Inject(activity, null, null);

        // W3C traceparent format: 00-{32 hex traceId}-{16 hex spanId}-{2 hex flags}
        var tp = headers["traceparent"];
        var parts = tp.Split('-');
        Assert.Equal(4, parts.Length);
        Assert.Equal("00", parts[0]);
        Assert.Equal(32, parts[1].Length);
        Assert.Equal(16, parts[2].Length);
        Assert.Equal(2, parts[3].Length);
        // Flags are "00" (not recorded) or "01" (recorded) — both are valid
        Assert.Matches("^[01]{2}$", parts[3]);
    }

    [Fact]
    public void Inject_B3_ProducesB3Headers()
    {
        var prop = new TraceContextPropagator("b3");
        using var activity = new ActivitySource("test").StartActivity("op");

        var headers = prop.Inject(activity, null, null);

        Assert.True(headers.TryGetValue("x-b3-traceid", out var tid));
        Assert.Equal(32, tid.Length);
        Assert.True(headers.TryGetValue("x-b3-spanid", out var sid));
        Assert.Equal(16, sid.Length);
        Assert.True(headers.ContainsKey("x-b3-sampled"));
    }

    [Fact]
    public void Inject_Both_ProducesAllHeaders()
    {
        var prop = new TraceContextPropagator("both");
        using var activity = new ActivitySource("test").StartActivity("op");

        var headers = prop.Inject(activity, null, null);

        Assert.Contains("traceparent", headers.Keys);
        Assert.Contains("x-b3-traceid", headers.Keys);
        Assert.Contains("x-b3-spanid", headers.Keys);
        Assert.Contains("x-b3-sampled", headers.Keys);
    }

    [Fact]
    public void Inject_WithProvidedTraceId_UsesProvidedValue()
    {
        var prop = new TraceContextPropagator("w3c");
        const string customTraceId = "00000000000000000000000000000001";
        const string customSpanId = "0000000000000001";

        var headers = prop.Inject(null, customTraceId, customSpanId);

        Assert.StartsWith($"00-{customTraceId}-{customSpanId}-", headers["traceparent"]);
    }

    [Fact]
    public void Inject_NoActivity_NoFallback_GeneratesNewIds()
    {
        var prop = new TraceContextPropagator("w3c");

        var headers = prop.Inject(null, null, null);

        Assert.True(headers.TryGetValue("traceparent", out var tp));
        Assert.StartsWith("00-", tp);
    }

    // ─── Extract ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_W3C_ParsesValidTraceparent()
    {
        var prop = new TraceContextPropagator("w3c");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"
        };

        var carrier = prop.Extract(headers);

        Assert.Equal("0af7651916cd43dd8448eb211c80319c", carrier.TraceId);
        Assert.Equal("b7ad6b7169203331", carrier.SpanId);
        Assert.Equal("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01", carrier.TraceParent);
        Assert.Equal("1", carrier.B3Sampled); // flags 01 = sampled
    }

    [Fact]
    public void Extract_B3_ParsesB3Headers()
    {
        var prop = new TraceContextPropagator("b3");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["x-b3-traceid"] = "00000000000000000000000000000001",
            ["x-b3-spanid"]  = "0000000000000001",
            ["x-b3-sampled"] = "1"
        };

        var carrier = prop.Extract(headers);

        Assert.Equal("00000000000000000000000000000001", carrier.TraceId);
        Assert.Equal("0000000000000001", carrier.SpanId);
        Assert.Equal("1", carrier.B3Sampled);
    }

    [Fact]
    public void Extract_Both_UsesW3C_AndAugmentsFromB3()
    {
        var prop = new TraceContextPropagator("both");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["traceparent"]   = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-00",
            ["x-b3-traceid"]  = "ignored-by-w3c",
            ["x-b3-sampled"]  = "d",
            ["tracestate"]    = "foo=bar"
        };

        var carrier = prop.Extract(headers);

        // W3C takes priority when both are available
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", carrier.TraceId);
        Assert.Equal("d", carrier.B3Sampled); // B3 augments sampled
        Assert.Equal("foo=bar", carrier.TraceState);
    }

    [Fact]
    public void Extract_EmptyHeaders_ReturnsEmptyCarrier()
    {
        var prop = new TraceContextPropagator("w3c");
        var carrier = prop.Extract(new Dictionary<string, string>());

        Assert.False(carrier.HasTrace);
        Assert.Null(carrier.TraceId);
    }

    [Fact]
    public void Extract_InvalidTraceparent_Ignores()
    {
        var prop = new TraceContextPropagator("w3c");
        var headers = new Dictionary<string, string>
        {
            ["traceparent"] = "invalid"
        };

        var carrier = prop.Extract(headers);

        Assert.False(carrier.HasTrace);
    }

    [Fact]
    public void Extract_FutureVersion_Ignores()
    {
        var prop = new TraceContextPropagator("w3c");
        var headers = new Dictionary<string, string>
        {
            // version "ff" is a future/invalid version
            ["traceparent"] = "ff-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"
        };

        var carrier = prop.Extract(headers);

        Assert.False(carrier.HasTrace);
    }

    [Fact]
    public void Extract_ExtractFromRequest_LowercasesKeys()
    {
        var prop = new TraceContextPropagator("w3c");
        // HttpRequest.Headers has lowercase keys
        var headers = new Dictionary<string, string>
        {
            ["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-00"
        };

        var carrier = prop.ExtractFromRequest(headers);

        Assert.True(carrier.HasTrace);
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", carrier.TraceId);
    }

    // ─── ToActivityContext ───────────────────────────────────────────────────────

    [Fact]
    public void ToActivityContext_ValidCarrier_CreatesValidContext()
    {
        var prop = new TraceContextPropagator("w3c");
        var carrier = new TraceContextCarrier
        {
            TraceId = "0af7651916cd43dd8448eb211c80319c",
            SpanId  = "b7ad6b7169203331"
        };

        var ctx = TraceContextPropagator.ToActivityContext(carrier);

        Assert.NotEqual(default, ctx);
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", ctx.TraceId.ToHexString());
        Assert.Equal("b7ad6b7169203331", ctx.SpanId.ToHexString());
    }

    [Fact]
    public void ToActivityContext_NullTraceId_ReturnsDefault()
    {
        var carrier = new TraceContextCarrier { TraceId = null };

        var ctx = TraceContextPropagator.ToActivityContext(carrier);

        Assert.Equal(default, ctx);
    }

    // ─── Disabled propagation format ─────────────────────────────────────────────

    [Fact]
    public void Constructor_UnknownFormat_DefaultsToW3C()
    {
        var prop = new TraceContextPropagator("unknown");
        using var activity = new ActivitySource("test").StartActivity("op");

        var headers = prop.Inject(activity, null, null);

        Assert.Contains("traceparent", headers.Keys);
    }

    [Fact]
    public void Inject_DisabledFormat_DefaultsToW3C()
    {
        // "none" is not a recognized format — safe fallback defaults to W3C
        var prop = new TraceContextPropagator("none");
        using var activity = new ActivitySource("test").StartActivity("op");

        var headers = prop.Inject(activity, null, null);

        Assert.Contains("traceparent", headers.Keys);
    }
}
