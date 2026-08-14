using Hercules.Context;
using Hercules.Context.Summarizer;
using Xunit;

namespace Hercules.Agent.Tests.Context;

public class TraceSummarizerTests
{
    private readonly TraceSummarizer _summarizer = new();

    [Fact]
    public void Summarize_EmptyTrace_ReturnsEmptyMessage()
    {
        var trace = new List<ToolTraceEntry>();

        var result = _summarizer.Summarize(trace);

        Assert.Contains("Empty trace", result);
    }

    [Fact]
    public void Summarize_SingleCall_ReturnsCorrectSummary()
    {
        var trace = new List<ToolTraceEntry>
        {
            new("file_read", "{\"path\":\"/tmp/test.txt\"}", "Hello World",
                150, DateTime.UtcNow, true)
        };

        var result = _summarizer.Summarize(trace);

        Assert.Contains("file_read", result);
        Assert.Contains("tool call(s)", result);
        Assert.Contains("150ms", result);
        Assert.Contains("1ok/0fail", result);
    }

    [Fact]
    public void Summarize_MultipleCalls_SameTool_GroupsByTool()
    {
        var now = DateTime.UtcNow;
        var trace = new List<ToolTraceEntry>
        {
            new("http_get", "{}", "result1", 100, now, true),
            new("http_get", "{}", "result2", 200, now.AddSeconds(1), true),
            new("http_get", "{}", "result3", 150, now.AddSeconds(2), false)
        };

        var result = _summarizer.Summarize(trace);

        Assert.Contains("http_get", result);
        Assert.Contains("3x", result);
        Assert.Contains("2ok/1fail", result);
    }

    [Fact]
    public void Summarize_MixedTools_ReportsBothTools()
    {
        var now = DateTime.UtcNow;
        var trace = new List<ToolTraceEntry>
        {
            new("read_file", "{}", "content", 50, now, true),
            new("write_file", "{}", "", 75, now, true),
            new("read_file", "{}", "more", 60, now, false)
        };

        var result = _summarizer.Summarize(trace);

        Assert.Contains("read_file", result);
        Assert.Contains("write_file", result);
    }

    [Fact]
    public void Summarize_FailedCalls_ReportsCorrectFailureCount()
    {
        var now = DateTime.UtcNow;
        var trace = new List<ToolTraceEntry>
        {
            new("tool_a", "{}", "ok", 10, now, true),
            new("tool_a", "{}", "fail", 10, now, false),
            new("tool_a", "{}", "fail", 10, now, false)
        };

        var result = _summarizer.Summarize(trace);

        Assert.Contains("1ok/2fail", result);
    }

    [Fact]
    public void Summarize_LongOutput_Truncated()
    {
        var longOutput = new string('a', 200);
        var trace = new List<ToolTraceEntry>
        {
            new("tool", "{}", longOutput, 10, DateTime.UtcNow, true)
        };

        var result = _summarizer.Summarize(trace);

        // After truncation marker, output should be truncated to 60 chars + "..."
        var arrowIdx = result.IndexOf("→");
        Assert.True(arrowIdx >= 0, "Expected → in output");
        var afterArrow = result[(arrowIdx + 1)..];
        Assert.True(afterArrow.Length <= 63, "Output should be truncated to ≤63 chars (60 + '...')");
        Assert.EndsWith("...", result);
    }
}
