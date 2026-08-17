using Hercules.Mesh;
using Hercules.Mesh.Schema;
using Xunit;

namespace Hercules.Agent.Tests.Phase3Tests;

public class IntentEnvelopeTests
{
   [Fact]
   public void ToJson_And_FromJson_RoundTrip()
   {
      var envelope = new IntentEnvelope(
         "req-001",
         "agent-a",
         "code-review",
         """{"message":"check this code"}""",
         "http://localhost:8421/api/mesh/callback",
         15_000,
         "trace-123");

      var json = envelope.ToJson();
      Assert.Contains("req-001", json);
      Assert.Contains("code-review", json);

      var restored = IntentEnvelope.FromJson(json);
      Assert.NotNull(restored);
      Assert.Equal("req-001", restored.RequestId);
      Assert.Equal("agent-a", restored.Sender);
      Assert.Equal("code-review", restored.Intent);
      Assert.Equal("http://localhost:8421/api/mesh/callback", restored.ReplyTo);
      Assert.Equal("trace-123", restored.TraceId);
      Assert.NotNull(restored.Deadline);
      Assert.Equal("1.0", restored.Version);
   }

   [Fact]
   public void Envelope_With_AllNewFields_SerializesCorrectly()
   {
      var auth = AuthContext.Bearer("token123", delegationDepth: 1, rootRequestId: "root-001");
      auth.Claims["scope"] = "delegate:read";
      var schema = ResponseSchema.Json("""{"type":"object","properties":{"answer":{"type":"string"}}}""");

      var envelope = new IntentEnvelope
      {
         RequestId = "req-002",
         Sender = "agent-x",
         Recipient = "agent-y",
         Intent = "csharp-refactor",
         Payload = """{"code":"void Foo() {}"}""",
         IdempotencyKey = "idem-key-123",
         TraceId = "trace-456",
         ReplyTo = "http://callback.example.com",
         Deadline = DateTimeOffset.UtcNow.AddMinutes(5),
         Auth = auth,
         ResponseSchema = schema,
         Version = IntentEnvelope.CurrentVersion
      };

      var json = envelope.ToJson();
      Assert.Contains("req-002", json);
      Assert.Contains("agent-x", json);
      Assert.Contains("agent-y", json);
      Assert.Contains("idem-key-123", json);
      Assert.Contains("bearer", json);
      Assert.Contains("token123", json);
      Assert.Contains("1.0", json); // version

      var restored = IntentEnvelope.FromJson(json);
      Assert.NotNull(restored);
      Assert.Equal("req-002", restored.RequestId);
      Assert.Equal("agent-y", restored.Recipient);
      Assert.Equal("idem-key-123", restored.IdempotencyKey);
      Assert.NotNull(restored.Auth);
      Assert.Equal("bearer", restored.Auth.AuthType);
      Assert.Equal("token123", restored.Auth.Token);
      Assert.Equal(1, restored.Auth.DelegationDepth);
      Assert.Equal("root-001", restored.Auth.RootRequestId);
      Assert.Equal("delegate:read", restored.Auth.GetClaim("scope"));
      Assert.NotNull(restored.ResponseSchema);
      Assert.Equal("json", restored.ResponseSchema.SchemaType);
   }

   [Fact]
   public void Envelope_Create_WithTypedPayload_SerializesCorrectly()
   {
      var payload = new { code = "int x = 42;", language = "csharp" };
      var envelope = IntentEnvelope.Create(
         requestId: "req-003",
         sender: "agent-a",
         intent: "code-analysis",
         payload: payload,
         recipient: "agent-b",
         traceId: "trace-789",
         idempotencyKey: "idem-003");

      Assert.Equal("req-003", envelope.RequestId);
      Assert.Equal("agent-a", envelope.Sender);
      Assert.Equal("agent-b", envelope.Recipient);
      Assert.Equal("idem-003", envelope.IdempotencyKey);
      Assert.Contains("\"code\"", envelope.Payload);
      Assert.Contains("\"language\"", envelope.Payload);
   }

   [Fact]
   public void Envelope_Deadline_Expires_Correctly()
   {
      var pastDeadline = DateTimeOffset.UtcNow.AddMinutes(-5);
      var envelope = new IntentEnvelope { Deadline = pastDeadline };
      Assert.True(envelope.IsExpired);

      var futureDeadline = DateTimeOffset.UtcNow.AddMinutes(5);
      var envelope2 = new IntentEnvelope { Deadline = futureDeadline };
      Assert.False(envelope2.IsExpired);
   }

   [Fact]
   public void Envelope_GetDeadlineOrDefault_ReturnsDeadline()
   {
      var deadline = DateTimeOffset.UtcNow.AddMinutes(10);
      var envelope = new IntentEnvelope { Deadline = deadline };
      var result = envelope.GetDeadlineOrDefault(30_000);

      Assert.Equal(deadline, result);
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
   public void IntentResponse_SchemaMismatch_Has_SchemaMismatch_Status()
   {
      var resp = IntentResponse.SchemaMismatch("req-005", "agent-e", "{ \"type\": \"object\" }");

      Assert.Equal("schema_mismatch", resp.Status);
      Assert.False(resp.IsSuccess);
      Assert.Contains("schema", resp.Error, StringComparison.OrdinalIgnoreCase);
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
      for (var i = 0; i < 100; i++) ids.Add(IntentIds.NewRequestId());
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