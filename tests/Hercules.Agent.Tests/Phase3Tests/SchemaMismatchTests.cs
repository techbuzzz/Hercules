using System.Text.Json;
using Hercules.Mesh;
using Hercules.Mesh.Schema;
using Hercules.Mesh.Transport;
using Xunit;
using TestAgents = Hercules.Agent.Tests.Phase3Tests.TestAgents;

namespace Hercules.Agent.Tests.Phase3Tests;

/// <summary>
/// Schema mismatch handling tests.
/// Verifies that unknown fields, missing optional fields, and type coercion edge cases
/// are handled gracefully.
/// </summary>
public class SchemaMismatchTests
{
    [Fact]
    public void SchemaMismatchAgent_ReturnsPlainText_WhenJsonExpected()
    {
        var agent = new TestAgents.SchemaMismatchAgent();
        var schema = new ResponseSchema { SchemaType = "json", JsonSchema = "{\"type\":\"object\"}" };
        var envelope = IntentEnvelope.Create(
            "req-json-schema",
            "sender",
            "test",
            "data",
            responseSchema: schema);

        var response = agent.Handle(envelope);

        Assert.True(response.IsSuccess);
        Assert.DoesNotContain("{", response.Result);
        Assert.Contains("Plain text", response.Result);
    }

    [Fact]
    public void IntentResponse_SchemaMismatch_Factory_CreatesCorrectStatus()
    {
        var response = IntentResponse.SchemaMismatch("req-schema", "agent-x", "{\"type\":\"object\"}");

        Assert.Equal("schema_mismatch", response.Status);
        Assert.False(response.IsSuccess);
        Assert.Contains("schema", response.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Envelope_WithJsonSchema_RoundTrips()
    {
        var schema = new ResponseSchema
        {
            SchemaType = "json",
            JsonSchema = "{\"type\":\"object\",\"properties\":{\"answer\":{\"type\":\"string\"}}}"
        };
        var envelope = IntentEnvelope.Create(
            "req-schema-json",
            "sender",
            "test",
            new { question = "what?" },
            responseSchema: schema);

        var json = envelope.ToJson();
        var restored = IntentEnvelope.FromJson(json);

        Assert.NotNull(restored);
        Assert.NotNull(restored.ResponseSchema);
        Assert.Equal("json", restored.ResponseSchema.SchemaType);
        Assert.Contains("answer", restored.ResponseSchema.JsonSchema ?? "");
    }

    [Fact]
    public void Envelope_WithTextSchema_RoundTrips()
    {
        var schema = ResponseSchema.Text();
        var envelope = IntentEnvelope.Create(
            "req-schema-text",
            "sender",
            "test",
            "data",
            responseSchema: schema);

        var json = envelope.ToJson();
        var restored = IntentEnvelope.FromJson(json);

        Assert.NotNull(restored);
        Assert.NotNull(restored.ResponseSchema);
        Assert.Equal("text", restored.ResponseSchema.SchemaType);
        Assert.Equal("text/plain", restored.ResponseSchema.ContentType);
    }

    [Fact]
    public void ResponseSchema_DefaultValues_AreCorrect()
    {
        var schema = new ResponseSchema();

        Assert.Equal("json", schema.SchemaType);
        Assert.Null(schema.JsonSchema);
        Assert.Equal("application/json", schema.ContentType);
        Assert.Equal("1.0", schema.Version);
    }

    [Fact]
    public void Envelope_WithoutResponseSchema_RoundTrips()
    {
        var envelope = IntentEnvelope.Create(
            "req-no-schema",
            "sender",
            "test",
            "data");

        var json = envelope.ToJson();
        var restored = IntentEnvelope.FromJson(json);

        Assert.NotNull(restored);
        Assert.Null(restored.ResponseSchema);
    }

    [Fact]
    public void Envelope_ResponseSchema_WithContentType_RoundTrips()
    {
        var schema = new ResponseSchema
        {
            SchemaType = "json",
            JsonSchema = "{\"type\":\"array\"}",
            ContentType = "application/json; charset=utf-8"
        };
        var envelope = IntentEnvelope.Create(
            "req-content-type",
            "sender",
            "test",
            "data",
            responseSchema: schema);

        var json = envelope.ToJson();
        var restored = IntentEnvelope.FromJson(json);

        Assert.NotNull(restored);
        Assert.NotNull(restored.ResponseSchema);
        Assert.Equal("application/json; charset=utf-8", restored.ResponseSchema.ContentType);
    }

    [Fact]
    public void SchemaMismatchAgent_ReturnsExtraFields()
    {
        var agent = new TestAgents.SchemaMismatchAgent();
        var envelope = IntentEnvelope.Create(
            "req-extra-fields",
            "sender",
            "test",
            "data");

        var response = agent.Handle(envelope);

        Assert.True(response.IsSuccess);
        Assert.Contains("unexpectedField", response.Result);
    }

    [Fact]
    public void Envelope_UnknownResponseSchemaType_Deserializes()
    {
        // IntentEnvelope uses snake_case JSON; ResponseSchema also uses snake_case: schema_type, content_type
        var json = "{\"request_id\":\"req-unknown-schema\",\"sender\":\"sender\",\"intent\":\"test\",\"payload\":\"data\",\"response_schema\":{\"schema_type\":\"xml\",\"content_type\":\"application/xml\"}}";

        var envelope = IntentEnvelope.FromJson(json);

        Assert.NotNull(envelope);
        Assert.NotNull(envelope.ResponseSchema);
        Assert.Equal("xml", envelope.ResponseSchema.SchemaType);
        Assert.Equal("application/xml", envelope.ResponseSchema.ContentType);
    }

    [Fact]
    public void Envelope_NullResponseSchema_Deserializes()
    {
        var json = "{\"requestId\":\"req-null-schema\",\"sender\":\"sender\",\"intent\":\"test\",\"payload\":\"data\",\"responseSchema\":null}";

        var envelope = IntentEnvelope.FromJson(json);

        Assert.NotNull(envelope);
        Assert.Null(envelope.ResponseSchema);
    }

    [Fact]
    public void TransportResult_SchemaMismatch_Kind()
    {
        var result = TransportResult.TransportError(
            "agent-x",
            "trace-1",
            "Response schema mismatch",
            latencyMs: 50,
            TransportKind.Http);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Response);
        Assert.Contains("mismatch", result.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
