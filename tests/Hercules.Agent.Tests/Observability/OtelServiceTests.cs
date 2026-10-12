using System.Diagnostics;
using Hercules.Config;
using Hercules.Observability;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hercules.Agent.Tests.Observability;

/// <summary>
///     R3b: each test runs under a scoped <see cref="OtelActivityListenerScope"/> so the
///     enabled-path tests receive a non-null <see cref="Activity"/>. Without a registered
///     <see cref="ActivityListener"/>, <c>ActivitySource.StartActivity</c> returns null and
///     these five tests fail — they were asserting against a NoopActivity that does not
///     exist in .NET (see the corrected note in OtelMetrics).
/// </summary>
[Collection(nameof(OpenTelemetryCollection))]
public class OtelServiceTests : IDisposable
{
    private readonly OtelActivityListenerScope _listenerScope;

    public OtelServiceTests() => _listenerScope = new OtelActivityListenerScope();

    public void Dispose() => _listenerScope.Dispose();

    [Fact]
    public void IsEnabled_ReturnsTrue_WhenConfigEnabled()
    {
        var config = new OtelConfig { Enabled = true };
        var service = new OtelService(config);
        Assert.True(service.IsEnabled);
    }

    [Fact]
    public void IsEnabled_ReturnsFalse_WhenConfigDisabled()
    {
        var config = new OtelConfig { Enabled = false };
        var service = new OtelService(config);
        Assert.False(service.IsEnabled);
    }

    [Fact]
    public void StartActivity_ReturnsNull_WhenDisabled()
    {
        var config = new OtelConfig { Enabled = false };
        var service = new OtelService(config);
        var activity = service.StartActivity("test.operation");
        Assert.Null(activity);
    }

    [Fact]
    public void StartActivity_ReturnsActivity_WhenEnabled()
    {
        var config = new OtelConfig { Enabled = true };
        var service = new OtelService(config);
        using var activity = service.StartActivity("test.operation");
        Assert.NotNull(activity);
        Assert.Equal("test.operation", activity!.OperationName);
    }

    [Fact]
    public void StartActivity_WithParent_ReturnsChild()
    {
        var config = new OtelConfig { Enabled = true };
        var service = new OtelService(config);
        using var parent = service.StartActivity("parent.operation");
        Assert.NotNull(parent);
        using var child = service.StartActivity("child.operation", parent!.Context);
        Assert.NotNull(child);
        // R3b: the child must inherit the trace and record the parent SPAN.
        // ParentId is serialised in W3C hierarchical form ("00-{traceId}-{spanId}-{flags}"),
        // so the previous assertion comparing it to parent.Context.TraceId could never match,
        // and comparing it to the bare SpanId was format-dependent.
        Assert.Equal(parent.TraceId, child!.TraceId);
        Assert.Contains(parent.SpanId.ToString(), child.ParentId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(parent.SpanId.ToString(), child.SpanId.ToString());
    }

    [Fact]
    public void SetTag_DoesNotThrow_WhenActivityNull()
    {
        var config = new OtelConfig { Enabled = false };
        var service = new OtelService(config);
        var exception = Record.Exception(() => service.SetTag(null, "key", "value"));
        Assert.Null(exception);
    }

    [Fact]
    public void SetTag_SetsTag_WhenActivityNotNull()
    {
        var config = new OtelConfig { Enabled = true };
        var service = new OtelService(config);
        using var activity = service.StartActivity("test");
        service.SetTag(activity, "test.key", "test.value");
        Assert.Equal("test.value", activity!.GetTagItem("test.key"));
    }

    [Fact]
    public void SetTags_DoesNotThrow_WhenActivityNull()
    {
        var config = new OtelConfig { Enabled = false };
        var service = new OtelService(config);
        var tags = new[] { new KeyValuePair<string, object?>("k", "v") };
        var exception = Record.Exception(() => service.SetTags(null, tags));
        Assert.Null(exception);
    }

    [Fact]
    public void AddEvent_DoesNotThrow_WhenActivityNull()
    {
        var config = new OtelConfig { Enabled = false };
        var service = new OtelService(config);
        var exception = Record.Exception(() => service.AddEvent(null, "test_event"));
        Assert.Null(exception);
    }

    [Fact]
    public void SetErrorStatus_DoesNotThrow_WhenActivityNull()
    {
        var config = new OtelConfig { Enabled = false };
        var service = new OtelService(config);
        var exception = Record.Exception(() => service.SetErrorStatus(null, "error"));
        Assert.Null(exception);
    }

    [Fact]
    public void SetErrorStatus_SetsErrorStatus()
    {
        var config = new OtelConfig { Enabled = true };
        var service = new OtelService(config);
        using var activity = service.StartActivity("test");
        service.SetErrorStatus(activity, "something went wrong");
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("something went wrong", activity!.StatusDescription);
    }

    [Fact]
    public void StopActivity_DoesNotThrow_WhenActivityNull()
    {
        var config = new OtelConfig { Enabled = false };
        var service = new OtelService(config);
        var exception = Record.Exception(() => service.StopActivity(null));
        Assert.Null(exception);
    }

    [Fact]
    public void StopActivity_SetsOkStatus()
    {
        var config = new OtelConfig { Enabled = true };
        var service = new OtelService(config);
        using var activity = service.StartActivity("test");
        service.StopActivity(activity, ActivityStatusCode.Ok);
        Assert.Equal(ActivityStatusCode.Ok, activity!.Status);
    }
}
