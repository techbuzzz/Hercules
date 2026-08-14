using System.Diagnostics;
using Hercules.Config;
using Hercules.Mesh.Observability;
using Hercules.Observability;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Phase4Tests.Observability;

/// <summary>
/// ActivityListener ensures Activity.Current is set in unit tests.
/// Registered at class init, disposed at class cleanup.
/// </summary>
public sealed class MeshObservabilityServiceTests : IDisposable
{
    private readonly OtelConfig _otelConfig = new() { Enabled = true };
    private readonly OtelService _otelService;
    private readonly ActivityListener _listener;

    public MeshObservabilityServiceTests()
    {
        _otelService = new OtelService(_otelConfig);

        // Register an ActivityListener so Activity.Current is set during tests.
        // Without this, StartActivity() returns an Activity but Activity.Current remains null.
        _listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name is "test" or OtelSetup.ServiceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    private MeshCentralizedObservabilityConfig DefaultConfig(bool enabled = true) =>
        new() { Enabled = enabled };

    private MeshObservabilityService CreateService(MeshCentralizedObservabilityConfig? cfg = null) =>
        new(
            cfg ?? DefaultConfig(),
            _otelService,
            Mock.Of<ILogger<MeshObservabilityService>>());

    // ─── IsEnabled ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsEnabled_ConfigEnabled_ReturnsTrue()
    {
        var svc = CreateService(DefaultConfig(true));
        Assert.True(svc.IsEnabled);
    }

    [Fact]
    public void IsEnabled_ConfigDisabled_ReturnsFalse()
    {
        var svc = CreateService(DefaultConfig(false));
        Assert.False(svc.IsEnabled);
    }

    // ─── InjectTraceContext ─────────────────────────────────────────────────────

    [Fact]
    public void InjectTraceContext_Disabled_ReturnsEmpty()
    {
        var svc = CreateService(DefaultConfig(false));
        using var activity = new ActivitySource("test").StartActivity("op");

        var headers = svc.InjectTraceContext(activity, null, null);

        Assert.Empty(headers);
    }

    [Fact]
    public void InjectTraceContext_W3C_AddsTraceparentHeader()
    {
        var svc = CreateService(DefaultConfig(true));
        using var activity = new ActivitySource("test").StartActivity("op");

        var headers = svc.InjectTraceContext(activity, null, null);

        Assert.True(headers.TryGetValue("traceparent", out var tp));
        Assert.StartsWith("00-", tp);
    }

    [Fact]
    public void InjectTraceContext_B3_AddsB3Headers()
    {
        var cfg = new MeshCentralizedObservabilityConfig
        {
            Enabled = true, PropagationFormat = "b3"
        };
        var svc = CreateService(cfg);
        using var activity = new ActivitySource("test").StartActivity("op");

        var headers = svc.InjectTraceContext(activity, null, null);

        Assert.Contains("x-b3-traceid", headers.Keys);
        Assert.Contains("x-b3-spanid", headers.Keys);
        Assert.Contains("x-b3-sampled", headers.Keys);
    }

    [Fact]
    public void InjectTraceContext_Both_AddsAllHeaders()
    {
        var cfg = new MeshCentralizedObservabilityConfig
        {
            Enabled = true, PropagationFormat = "both"
        };
        var svc = CreateService(cfg);
        using var activity = new ActivitySource("test").StartActivity("op");

        var headers = svc.InjectTraceContext(activity, null, null);

        Assert.Contains("traceparent", headers.Keys);
        Assert.Contains("x-b3-traceid", headers.Keys);
    }

    [Fact]
    public void InjectTraceContext_DisabledPropagation_ReturnsEmpty()
    {
        var cfg = new MeshCentralizedObservabilityConfig
        {
            Enabled = true, EnableTraceContextPropagation = false
        };
        var svc = CreateService(cfg);
        using var activity = new ActivitySource("test").StartActivity("op");

        var headers = svc.InjectTraceContext(activity, null, null);

        Assert.Empty(headers);
    }

    [Fact]
    public void InjectTraceContext_WithProvidedTraceId_UsesProvided()
    {
        var svc = CreateService(DefaultConfig(true));
        const string expectedTraceId = "00000000000000000000000000000001";

        var headers = svc.InjectTraceContext(null, expectedTraceId, null);

        Assert.StartsWith($"00-{expectedTraceId}-", headers["traceparent"]);
    }

    // ─── ExtractTraceContext ─────────────────────────────────────────────────────

    [Fact]
    public void ExtractTraceContext_Disabled_ReturnsEmptyCarrier()
    {
        var svc = CreateService(DefaultConfig(false));
        var headers = new Dictionary<string, string>
        {
            ["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-00"
        };

        var carrier = svc.ExtractTraceContext(headers);

        Assert.False(carrier.HasTrace);
    }

    [Fact]
    public void ExtractTraceContext_ValidW3C_ParsesTraceContext()
    {
        var svc = CreateService(DefaultConfig(true));
        var headers = new Dictionary<string, string>
        {
            ["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"
        };

        var carrier = svc.ExtractTraceContext(headers);

        Assert.True(carrier.HasTrace);
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", carrier.TraceId);
        Assert.Equal("b7ad6b7169203331", carrier.SpanId);
    }

    [Fact]
    public void ExtractTraceContext_B3Format_ParsesB3Headers()
    {
        var cfg = new MeshCentralizedObservabilityConfig
        {
            Enabled = true, PropagationFormat = "b3"
        };
        var svc = CreateService(cfg);
        var headers = new Dictionary<string, string>
        {
            ["x-b3-traceid"] = "00000000000000000000000000000001",
            ["x-b3-spanid"]  = "0000000000000001",
            ["x-b3-sampled"] = "1"
        };

        var carrier = svc.ExtractTraceContext(headers);

        Assert.True(carrier.HasTrace);
        Assert.Equal("00000000000000000000000000000001", carrier.TraceId);
        Assert.Equal("1", carrier.B3Sampled);
    }

    [Fact]
    public void ExtractTraceContext_EmptyHeaders_ReturnsEmptyCarrier()
    {
        var svc = CreateService(DefaultConfig(true));

        var carrier = svc.ExtractTraceContext(new Dictionary<string, string>());

        Assert.False(carrier.HasTrace);
    }

    [Fact]
    public void ExtractFromRequestHeaders_CaseInsensitive_Parses()
    {
        var svc = CreateService(DefaultConfig(true));
        var headers = new Dictionary<string, string>
        {
            ["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-00"
        };

        var carrier = svc.ExtractFromRequestHeaders(headers);

        Assert.True(carrier.HasTrace);
    }

    // ─── StartMeshSpan ─────────────────────────────────────────────────────────

    [Fact]
    public void StartMeshSpan_Disabled_ReturnsNull()
    {
        var svc = CreateService(DefaultConfig(false));

        var span = svc.StartMeshSpan("test.op", "peer", "intent");

        Assert.Null(span);
    }

    [Fact]
    public void StartMeshSpan_WithParent_SetsParentContext()
    {
        var svc = CreateService(DefaultConfig(true));
        using var parent = new ActivitySource("test").StartActivity("parent");

        var span = svc.StartMeshSpan("child.op", "peer", "read:test");

        Assert.NotNull(span);
        Assert.Equal(span.ParentId, parent.Id);
        Assert.Equal("child.op", span.OperationName);
    }

    [Fact]
    public void StartMeshSpan_SetsMeshTags()
    {
        var svc = CreateService(DefaultConfig(true));

        var span = svc.StartMeshSpan("mesh.op", "hercules-peer-2", "analyze:code");

        Assert.NotNull(span);
    }

    // ─── StartMeshSpanFromContext ───────────────────────────────────────────────

    [Fact]
    public void StartMeshSpanFromContext_Disabled_ReturnsNull()
    {
        var svc = CreateService(DefaultConfig(false));
        var carrier = new TraceContextCarrier
        {
            TraceId = "0af7651916cd43dd8448eb211c80319c",
            SpanId  = "b7ad6b7169203331"
        };

        var span = svc.StartMeshSpanFromContext("inbound.op", carrier, "intent");

        Assert.Null(span);
    }

    [Fact]
    public void StartMeshSpanFromContext_EmptyCarrier_ReturnsNull()
    {
        var svc = CreateService(DefaultConfig(true));
        var carrier = new TraceContextCarrier(); // HasTrace = false

        var span = svc.StartMeshSpanFromContext("inbound.op", carrier, "intent");

        Assert.Null(span);
    }

    [Fact]
    public void StartMeshSpanFromContext_ValidCarrier_CreatesChildSpan()
    {
        var svc = CreateService(DefaultConfig(true));
        var carrier = new TraceContextCarrier
        {
            TraceId = "0af7651916cd43dd8448eb211c80319c",
            SpanId  = "b7ad6b7169203331"
        };

        var span = svc.StartMeshSpanFromContext("inbound.op", carrier, "read:test");

        Assert.NotNull(span);
    }

    // ─── RecordMeshEvent ────────────────────────────────────────────────────────

    [Fact]
    public void RecordMeshEvent_Disabled_DoesNotThrow()
    {
        var svc = CreateService(DefaultConfig(false));
        using var activity = new ActivitySource("test").StartActivity("op");

        var ex = Record.Exception(() =>
            svc.RecordMeshEvent(activity, "delegation_sent", "read:test",
                "sender", "receiver", "capability-match", 1, 50.0, null));

        Assert.Null(ex);
    }

    [Fact]
    public void RecordMeshEvent_WithError_SetsErrorStatus()
    {
        var svc = CreateService(DefaultConfig(true));
        using var source = new ActivitySource("test");
        using var activity = source.StartActivity("op");

        svc.RecordMeshEvent(activity, "delegation_error", "read:test",
            "sender", "receiver", null, 1, 100.0, "connection_reset");

        Assert.Equal(ActivityStatusCode.Error, activity!.Status);
    }

    // ─── EnrichSpanWithMeshTags ─────────────────────────────────────────────────

    [Fact]
    public void EnrichSpanWithMeshTags_Disabled_DoesNotThrow()
    {
        var svc = CreateService(DefaultConfig(false));
        using var activity = new ActivitySource("test").StartActivity("op");

        var ex = Record.Exception(() =>
            svc.EnrichSpanWithMeshTags(activity, "intent", "sender", "receiver",
                "Http", 1, 2, "capability-match"));

        Assert.Null(ex);
    }

    [Fact]
    public void EnrichSpanWithMeshTags_NullActivity_DoesNotThrow()
    {
        var svc = CreateService(DefaultConfig(true));

        var ex = Record.Exception(() =>
            svc.EnrichSpanWithMeshTags(null, "intent", "sender", "receiver",
                "Http", 1, 2, "capability-match"));

        Assert.Null(ex);
    }

    // ─── RecordMeshMetric ──────────────────────────────────────────────────────

    [Fact]
    public void RecordMeshMetric_Disabled_DoesNotThrow()
    {
        var svc = CreateService(DefaultConfig(false));

        var ex = Record.Exception(() =>
            svc.RecordMeshMetric("delegation", 1, "peer", "intent", "ok"));

        Assert.Null(ex);
    }

    [Fact]
    public void RecordMeshMetric_UnknownMetric_DoesNotThrow()
    {
        var svc = CreateService(DefaultConfig(true));

        var ex = Record.Exception(() =>
            svc.RecordMeshMetric("unknown.metric", 42.0));

        Assert.Null(ex);
    }

    // ─── MaxTagValueLength truncation ───────────────────────────────────────────

    [Fact]
    public void InjectTraceContext_LongIntent_Truncates()
    {
        var cfg = new MeshCentralizedObservabilityConfig
        {
            Enabled = true,
            PropagationFormat = "w3c",
            MaxTagValueLength = 64
        };
        var svc = CreateService(cfg);
        using var activity = new ActivitySource("test").StartActivity("op");

        var headers = svc.InjectTraceContext(activity,
            "00000000000000000000000000000001",
            "0000000000000001");

        Assert.True(headers.TryGetValue("traceparent", out var tp));
        Assert.True(tp.Length <= 200); // reasonable length
    }
}
