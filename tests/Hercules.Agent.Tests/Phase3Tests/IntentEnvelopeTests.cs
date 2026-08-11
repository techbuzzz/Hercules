using Hercules.Mesh;
using Xunit;

namespace Hercules.Agent.Tests.Phase3Tests;

public class IntentEnvelopeTests
{
    [Fact]
    public void ToJson_And_FromJson_RoundTrip()
    {
        var envelope = new IntentEnvelope(
            RequestId: "req-001",
            Sender: "agent-a",
            Intent: "code-review",
            Payload: """{"message":"check this code"}""",
            ReplyTo: "http://localhost:5000/api/mesh/callback",
            TimeoutMs: 15_000,
            TraceId: "trace-123");

        var json = envelope.ToJson();
        Assert.Contains("req-001", json);
        Assert.Contains("code-review", json);

        var restored = IntentEnvelope.FromJson(json);
        Assert.NotNull(restored);
        Assert.Equal("req-001", restored.RequestId);
        Assert.Equal("agent-a", restored.Sender);
        Assert.Equal("code-review", restored.Intent);
        Assert.Equal(15_000, restored.TimeoutMs);
        Assert.Equal("trace-123", restored.TraceId);
    }

    [Fact]
    public void IntentResponse_Ok_Has_Success_Status()
    {
        var resp = IntentResponse.Ok("req-001", "agent-a", "result text", "skill", "code-review", 0.9, "trace-123");

        Assert.Equal("ok", resp.Status);
        Assert.True(resp.IsSuccess);
        Assert.Equal("result text", resp.Result);
        Assert.Equal(0.9, resp.Confidence);
        Assert.Equal("skill", resp.Mode);
        Assert.Equal("code-review", resp.Skill);
    }

    [Fact]
    public void IntentResponse_Failed_Has_Error_Status()
    {
        var resp = IntentResponse.Failed("req-002", "agent-b", "Something went wrong", "trace-456");

        Assert.Equal("error", resp.Status);
        Assert.False(resp.IsSuccess);
        Assert.Equal("Something went wrong", resp.Error);
    }

    [Fact]
    public void IntentResponse_TimedOut_Has_Timeout_Status()
    {
        var resp = IntentResponse.TimedOut("req-003", "agent-c");

        Assert.Equal("timeout", resp.Status);
        Assert.False(resp.IsSuccess);
        Assert.Contains("timed out", resp.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IntentResponse_Rejected_Has_Rejected_Status()
    {
        var resp = IntentResponse.Rejected("req-004", "agent-d", "Not supported");

        Assert.Equal("rejected", resp.Status);
        Assert.False(resp.IsSuccess);
        Assert.Equal("Not supported", resp.Error);
    }

    [Fact]
    public void IntentIds_NewRequestId_Generates_NonEmpty_String()
    {
        var id = IntentIds.NewRequestId();
        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.True(id.Length >= 20); // ULID = 26 chars
    }

    [Fact]
    public void IntentIds_NewRequestId_Generates_Unique_Values()
    {
        var ids = new HashSet<string>();
        for (int i = 0; i < 100; i++)
        {
            ids.Add(IntentIds.NewRequestId());
        }
        Assert.Equal(100, ids.Count); // Все 100 уникальны
    }

    [Fact]
    public void IntentResponse_ToJson_And_FromJson_RoundTrip()
    {
        var resp = IntentResponse.Ok("req-001", "agent-a", "result", "skill", "cap-1", 0.85, "trace-1");

        var json = resp.ToJson();
        var restored = IntentResponse.FromJson(json);

        Assert.NotNull(restored);
        Assert.Equal("ok", restored.Status);
        Assert.Equal("agent-a", restored.Agent);
        Assert.Equal(0.85, restored.Confidence);
        Assert.Equal("trace-1", restored.TraceId);
    }
}