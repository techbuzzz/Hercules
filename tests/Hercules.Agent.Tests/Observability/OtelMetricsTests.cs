using Hercules.Observability;
using Xunit;

namespace Hercules.Agent.Tests.Observability;

public class OtelMetricsTests
{
    [Fact]
    public void HandleCounter_IsNotNull()
    {
        Assert.NotNull(OtelMetrics.HandleCounter);
    }

    [Fact]
    public void LlmCallCounter_IsNotNull()
    {
        Assert.NotNull(OtelMetrics.LlmCallCounter);
    }

    [Fact]
    public void ToolCallCounter_IsNotNull()
    {
        Assert.NotNull(OtelMetrics.ToolCallCounter);
    }

    [Fact]
    public void SkillHitCounter_IsNotNull()
    {
        Assert.NotNull(OtelMetrics.SkillHitCounter);
    }

    [Fact]
    public void HandleDurationHistogram_IsNotNull()
    {
        Assert.NotNull(OtelMetrics.HandleDurationHistogram);
    }

    [Fact]
    public void LlmCallDurationHistogram_IsNotNull()
    {
        Assert.NotNull(OtelMetrics.LlmCallDurationHistogram);
    }

    [Fact]
    public void ToolCallDurationHistogram_IsNotNull()
    {
        Assert.NotNull(OtelMetrics.ToolCallDurationHistogram);
    }

    [Fact]
    public void LlmInputTokensHistogram_IsNotNull()
    {
        Assert.NotNull(OtelMetrics.LlmInputTokensHistogram);
    }

    [Fact]
    public void LlmOutputTokensHistogram_IsNotNull()
    {
        Assert.NotNull(OtelMetrics.LlmOutputTokensHistogram);
    }

    [Fact]
    public void HandleCounter_Add_DoesNotThrow()
    {
        var exception = Record.Exception(() => OtelMetrics.HandleCounter.Add(1));
        Assert.Null(exception);
    }

    [Fact]
    public void HandleCounter_Add_WithTags_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
            OtelMetrics.HandleCounter.Add(1,
                new KeyValuePair<string, object?>("session_id", "test-session")));
        Assert.Null(exception);
    }

    [Fact]
    public void LlmCallCounter_Add_WithTags_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
            OtelMetrics.LlmCallCounter.Add(1,
                new KeyValuePair<string, object?>("provider", "yandexgpt"),
                new KeyValuePair<string, object?>("model", "yandexgpt")));
        Assert.Null(exception);
    }

    [Fact]
    public void ToolCallCounter_Add_WithTags_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
            OtelMetrics.ToolCallCounter.Add(1,
                new KeyValuePair<string, object?>("tool", "http_fetch"),
                new KeyValuePair<string, object?>("status", "ok")));
        Assert.Null(exception);
    }

    [Fact]
    public void HandleDurationHistogram_Record_DoesNotThrow()
    {
        var exception = Record.Exception(() => OtelMetrics.HandleDurationHistogram.Record(42.5));
        Assert.Null(exception);
    }

    [Fact]
    public void LlmCallDurationHistogram_Record_DoesNotThrow()
    {
        var exception = Record.Exception(() => OtelMetrics.LlmCallDurationHistogram.Record(100.0));
        Assert.Null(exception);
    }

    [Fact]
    public void LlmInputTokensHistogram_Record_DoesNotThrow()
    {
        var exception = Record.Exception(() => OtelMetrics.LlmInputTokensHistogram.Record(500));
        Assert.Null(exception);
    }

    [Fact]
    public void LlmOutputTokensHistogram_Record_DoesNotThrow()
    {
        var exception = Record.Exception(() => OtelMetrics.LlmOutputTokensHistogram.Record(200));
        Assert.Null(exception);
    }
}
