using System.Text.Json;
using Hercules.Mesh;
using Xunit;

namespace Hercules.Agent.Tests.Phase3Tests;

/// <summary>
/// Tests for handling malformed inter-agent messages.
/// Verifies graceful handling of invalid JSON, missing fields, wrong types, etc.
/// </summary>
public class MalformedMessageTests
{
    // IntentEnvelope uses snake_case JSON property names
    private static JsonSerializerOptions SnakeOpts => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string SnakeSer(object obj) => JsonSerializer.Serialize(obj, SnakeOpts);

    // IntentEnvelope.FromJson throws JsonException on invalid JSON

    [Fact]
    public void FromJson_GarbledJson_Throws()
    {
        var garbage = "this is not json at all {{{{{";
        Assert.ThrowsAny<JsonException>(() => IntentEnvelope.FromJson(garbage));
    }

    [Fact]
    public void FromJson_EmptyString_Throws()
    {
        Assert.ThrowsAny<JsonException>(() => IntentEnvelope.FromJson(""));
    }

    [Fact]
    public void FromJson_WhitespaceOnly_Throws()
    {
        Assert.ThrowsAny<JsonException>(() => IntentEnvelope.FromJson("   \t\n  "));
    }

    [Fact]
    public void FromJson_NullLiteral_ReturnsNull()
    {
        var result = IntentEnvelope.FromJson("null");
        Assert.Null(result);
    }

    [Fact]
    public void FromJson_ArrayInsteadOfObject_Throws()
    {
        Assert.ThrowsAny<JsonException>(() => IntentEnvelope.FromJson("[1, 2, 3]"));
    }

    [Fact]
    public void FromJson_MissingRequiredFields_ReturnsEnvelope()
    {
        var minimal = SnakeSer(new { request_id = "req-min" });
        var result = IntentEnvelope.FromJson(minimal);

        Assert.NotNull(result);
        Assert.Equal("req-min", result.RequestId);
        Assert.Equal("", result.Sender);
        Assert.Equal("", result.Intent);
    }

    [Fact]
    public void FromJson_WrongType_ForNumericField_Throws()
    {
        // request_id as number instead of string — deserialization fails
        var json = SnakeSer(new { request_id = 12345, sender = "agent", intent = "test", payload = "data", version = "1.0" });
        Assert.ThrowsAny<JsonException>(() => IntentEnvelope.FromJson(json));
    }

    [Fact]
    public void FromJson_OversizedPayload_Deserializes()
    {
        var largePayload = new string('x', 1024 * 1024);
        var envelope = SnakeSer(new { request_id = "req-large", sender = "agent", intent = "test", payload = largePayload, version = "1.0" });
        var result = IntentEnvelope.FromJson(envelope);

        Assert.NotNull(result);
        Assert.True(result.Payload.Length >= 1024 * 1024);
    }

    [Fact]
    public void FromJson_NullValues_Deserializes()
    {
        var json = SnakeSer(new { request_id = "req-null", sender = (string?)null, intent = (string?)null, payload = (string?)null, version = "1.0" });
        var result = IntentEnvelope.FromJson(json);

        Assert.NotNull(result);
        Assert.Equal("req-null", result.RequestId);
        Assert.Null(result.Sender);
        Assert.Null(result.Intent);
        Assert.Null(result.Payload);
    }

    [Fact]
    public void FromJson_InvalidUtf8_Handled()
    {
        var json = SnakeSer(new { request_id = "req-invalid", sender = "agent", intent = "test", payload = "\u0000\uFFFF", version = "1.0" });
        var result = IntentEnvelope.FromJson(json);

        Assert.NotNull(result);
        Assert.Equal("req-invalid", result.RequestId);
    }

    [Fact]
    public void FromJson_DeeplyNested_Deserializes()
    {
        var nested = new { deep = new { deep = new { deep = "value" } } };
        var envelope = IntentEnvelope.Create("req-deep", "agent", "test", nested);

        Assert.NotNull(envelope);
        Assert.Contains("deep", envelope.Payload);
    }

    [Fact]
    public void FromJson_ControlCharacters_InPayload_Preserved()
    {
        var payloadWithControls = "line1\nline2\ttab\rcarriage";
        var envelope = IntentEnvelope.Create("req-ctrl", "agent", "test", payloadWithControls);

        var json = envelope.ToJson();
        var restored = IntentEnvelope.FromJson(json);

        Assert.NotNull(restored);
        Assert.Contains("\n", restored.Payload);
        Assert.Contains("\t", restored.Payload);
    }

    [Fact]
    public void FromJson_MissingTimestamp_DoesNotCrash()
    {
        var json = SnakeSer(new { request_id = "req-no-ts", sender = "agent", intent = "test", payload = "", version = "1.0" });
        var result = IntentEnvelope.FromJson(json);
        Assert.NotNull(result);
    }

    [Fact]
    public void Response_FromJson_Garbled_Throws()
    {
        Assert.ThrowsAny<JsonException>(() => IntentResponse.FromJson("not json at all"));
    }

    [Fact]
    public void Response_FromJson_MissingStatus_SetsToNull()
    {
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
        var json = JsonSerializer.Serialize(new { requestId = "req-no-status", agent = "agent" }, opts);
        var result = IntentResponse.FromJson(json);

        Assert.NotNull(result);
        Assert.Equal("req-no-status", result.RequestId);
        Assert.Equal("agent", result.Agent);
        Assert.Null(result.Status);
    }

    [Fact]
    public void Envelope_ToJson_SpecialCharacters_Escaped()
    {
        // UnsafeRelaxedJsonEscaping does not escape < > but does escape control chars and quotes
        var payload = "line1\nline2";
        var envelope = IntentEnvelope.Create("req-xss", "agent", "test", payload);

        var json = envelope.ToJson();
        // Newline is escaped
        Assert.Contains("\\n", json);
        Assert.DoesNotContain("line1\nline2", json);
    }

    [Fact]
    public void Envelope_ToJson_UnicodeCharacters_Preserved()
    {
        var payload = "Hello \U0001F30D world";
        var envelope = IntentEnvelope.Create("req-unicode", "agent", "test", payload);

        var json = envelope.ToJson();
        var restored = IntentEnvelope.FromJson(json);

        Assert.NotNull(restored);
        Assert.Contains("Hello", restored.Payload);
        Assert.Contains("\U0001F30D", restored.Payload);
    }

    [Fact]
    public void FromJson_DuplicateKeys_UsesLastValue()
    {
        // System.Text.Json keeps last value for duplicate keys
        var json = "{\"request_id\":\"first\",\"request_id\":\"last\",\"sender\":\"agent\",\"intent\":\"test\",\"payload\":\"\",\"version\":\"1.0\"}";
        var result = IntentEnvelope.FromJson(json);

        Assert.NotNull(result);
        Assert.Equal("last", result.RequestId);
    }
}
