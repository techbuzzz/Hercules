using System.Text.Json;
using Hercules.Mesh;
using Hercules.Mesh.Schema;
using Xunit;

namespace Hercules.Agent.Tests.Phase3Tests;

/// <summary>
/// Backward compatibility tests for inter-agent protocol.
/// Verifies that old envelope versions are handled correctly and unknown fields are ignored.
/// </summary>
public class ProtocolCompatibilityTests
{
    // IntentEnvelope uses snake_case JSON property names (e.g. "request_id", "idempotency_key")
    private static JsonSerializerOptions SnakeOpts => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string SnakeSer(object obj) => JsonSerializer.Serialize(obj, SnakeOpts);

    [Fact]
    public void Envelope_V1_Deserialize_MissingNewFields_SetsDefaults()
    {
        // v1 envelope: serialize with only the fields that existed in v1
        var v1Json = SnakeSer(new { request_id = "req-v1", sender = "agent-v1", intent = "test", payload = "data", version = "1.0" });

        var restored = IntentEnvelope.FromJson(v1Json);

        Assert.NotNull(restored);
        Assert.Equal("req-v1", restored.RequestId);
        Assert.Equal("agent-v1", restored.Sender);
        Assert.Equal("test", restored.Intent);
        Assert.Equal("data", restored.Payload);
        Assert.Equal("1.0", restored.Version);
        Assert.Null(restored.IdempotencyKey);
        Assert.Null(restored.Recipient);
        Assert.Null(restored.Auth);
        Assert.Null(restored.ResponseSchema);
        Assert.Null(restored.TraceId);
        Assert.Null(restored.ReplyTo);
        Assert.Null(restored.Deadline);
    }

    [Fact]
    public void Envelope_V1_Deserialize_WithUnknownFields_IgnoresThem()
    {
        // Simulate a future envelope with fields this version does not know
        var futureJson = SnakeSer(new
        {
            request_id = "req-future",
            sender = "agent-future",
            intent = "test",
            payload = "data",
            version = "2.0",
            unknown_field = "should be ignored",
            new_future_feature = 12345,
            nested = new { a = 1 }
        });

        var envelope = IntentEnvelope.FromJson(futureJson);

        Assert.NotNull(envelope);
        Assert.Equal("req-future", envelope.RequestId);
        Assert.Equal("2.0", envelope.Version);
    }

    [Fact]
    public void Envelope_V1_Deserialize_MinimalFields_StillWorks()
    {
        var envelope = new IntentEnvelope { RequestId = "req-min", Sender = "sender", Intent = "intent", Payload = "" };
        var json = envelope.ToJson();

        Assert.Contains("request_id", json);
        Assert.Contains("sender", json);
        Assert.Contains("intent", json);

        var restored = IntentEnvelope.FromJson(json);

        Assert.NotNull(restored);
        Assert.Equal("req-min", restored.RequestId);
        Assert.Equal("sender", restored.Sender);
        Assert.Equal("intent", restored.Intent);
    }

    [Fact]
    public void Envelope_CurrentVersion_Deserialize_AllFields()
    {
        var envelope = IntentEnvelope.Create(
            requestId: "req-full",
            sender: "agent-full",
            intent: "full-test",
            payload: new { key = "value" },
            recipient: "agent-other",
            traceId: "trace-123",
            idempotencyKey: "idem-123",
            auth: AuthContext.Bearer("secret-token", delegationDepth: 1, rootRequestId: "root-001"),
            responseSchema: ResponseSchema.Json("{\"type\":\"object\"}"),
            replyTo: "http://callback.example.com",
            timeoutMs: 300_000);

        var json = envelope.ToJson();
        var restored = IntentEnvelope.FromJson(json);

        Assert.NotNull(restored);
        Assert.Equal("req-full", restored.RequestId);
        Assert.Equal("trace-123", restored.TraceId);
        Assert.Equal("idem-123", restored.IdempotencyKey);
        Assert.Equal("agent-other", restored.Recipient);
        Assert.NotNull(restored.ResponseSchema);
        Assert.Equal("json", restored.ResponseSchema.SchemaType);
        Assert.NotNull(restored.Auth);
        Assert.Equal("bearer", restored.Auth.AuthType);
        Assert.Equal("secret-token", restored.Auth.Token);
        Assert.Equal(1, restored.Auth.DelegationDepth);
        Assert.Equal("root-001", restored.Auth.RootRequestId);
    }

    [Fact]
    public void Envelope_OldTimestampFormat_Deserializes()
    {
        var envelope = new IntentEnvelope
        {
            RequestId = "req-ts",
            Sender = "agent",
            Intent = "test",
            Payload = "",
            Deadline = DateTimeOffset.UtcNow.AddMinutes(5)
        };
        var json = envelope.ToJson();

        var restored = IntentEnvelope.FromJson(json);

        Assert.NotNull(restored);
        Assert.NotNull(restored.Deadline);
    }

    [Fact]
    public void Envelope_MissingRequestId_FailsGracefully()
    {
        // Build JSON without request_id field
        var json = SnakeSer(new { sender = "agent", intent = "test", payload = "data", version = "1.0" });

        var result = IntentEnvelope.FromJson(json);

        Assert.NotNull(result);
        Assert.Equal("", result.RequestId);
    }

    [Fact]
    public void Response_V1_Deserialize_MissingNewFields_SetsDefaults()
    {
        // IntentResponse uses camelCase JSON (no [JsonPropertyName] attributes)
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
        var json = JsonSerializer.Serialize(new { requestId = "req-001", status = "ok", agent = "agent-v1" }, opts);

        var response = IntentResponse.FromJson(json);

        Assert.NotNull(response);
        Assert.Equal("ok", response.Status);
        Assert.Equal("agent-v1", response.Agent);
        Assert.Equal("direct", response.Mode);
        Assert.Null(response.Skill);
        Assert.Null(response.Confidence);
    }

    [Fact]
    public void Envelope_RoundTrip_PreservesAllFields()
    {
        var envelope = IntentEnvelope.Create(
            requestId: "req-round",
            sender: "agent-a",
            intent: "roundtrip-test",
            payload: new { data = "test" },
            recipient: "agent-b",
            traceId: "trace-round",
            idempotencyKey: "idem-round",
            auth: AuthContext.Bearer("token-round", delegationDepth: 1, rootRequestId: "root-round"));

        var json = envelope.ToJson();
        var restored = IntentEnvelope.FromJson(json);

        Assert.NotNull(restored);
        Assert.Equal(envelope.RequestId, restored.RequestId);
        Assert.Equal(envelope.Sender, restored.Sender);
        Assert.Equal(envelope.Recipient, restored.Recipient);
        Assert.Equal(envelope.Intent, restored.Intent);
        Assert.Equal(envelope.TraceId, restored.TraceId);
        Assert.Equal(envelope.IdempotencyKey, restored.IdempotencyKey);
        Assert.NotNull(restored.Auth);
        Assert.Equal(envelope.Auth?.Token, restored.Auth.Token);
        Assert.Equal(envelope.Auth?.DelegationDepth, restored.Auth.DelegationDepth);
    }

    [Fact]
    public void Envelope_CamelCaseSerialization_RoundTrips()
    {
        // Verify that envelope round-trips correctly (uses snake_case internally)
        var envelope = new IntentEnvelope
        {
            RequestId = "req-case",
            Sender = "agent-case",
            Intent = "test",
            Payload = "data"
        };
        var json = envelope.ToJson();

        Assert.Contains("request_id", json);

        var restored = IntentEnvelope.FromJson(json);

        Assert.NotNull(restored);
        Assert.Equal("agent-case", restored.Sender);
        Assert.Equal("test", restored.Intent);
    }
}
