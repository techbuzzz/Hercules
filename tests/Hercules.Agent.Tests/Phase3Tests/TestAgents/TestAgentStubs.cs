using Hercules.Mesh;
using Hercules.Mesh.Schema;
using Hercules.Mesh.Transport;

namespace Hercules.Agent.Tests.Phase3Tests.TestAgents;

/// <summary>
/// Slow test agent — simulates delayed responses.
/// </summary>
public sealed class SlowAgent
{
    public int ResponseDelayMs { get; set; } = 5000;

    public async Task<IntentResponse> HandleAsync(IntentEnvelope envelope)
    {
        await Task.Delay(ResponseDelayMs);
        return IntentResponse.Ok(
            envelope.RequestId,
            "slow-agent",
            """{"simulated": "slow response"}""",
            "direct");
    }
}

/// <summary>
/// Failing test agent — always returns error responses.
/// </summary>
public sealed class FailingAgent
{
    public string ErrorMessage { get; set; } = "Simulated failure";
    public bool ThrowException { get; set; } = false;

    public async Task<IntentResponse> HandleAsync(IntentEnvelope envelope)
    {
        await Task.Yield(); // Simulate async processing
        if (ThrowException)
        {
            throw new InvalidOperationException(ErrorMessage);
        }
        return IntentResponse.Failed(envelope.RequestId, "failing-agent", ErrorMessage, envelope.TraceId);
    }
}

/// <summary>
/// Schema mismatch test agent — returns responses with mismatched schema.
/// </summary>
public sealed class SchemaMismatchAgent
{
    /// <summary>Return a response that doesn't match the expected schema.</summary>
    public IntentResponse Handle(IntentEnvelope envelope)
    {
        // If expected schema is JSON, return wrong type
        if (envelope.ResponseSchema?.SchemaType == "json")
        {
            // Return plain text instead of JSON
            return IntentResponse.Ok(
                envelope.RequestId,
                "schema-mismatch-agent",
                "Plain text response instead of JSON",
                "direct");
        }

        // Return a response with extra/missing fields
        return IntentResponse.Ok(
            envelope.RequestId,
            "schema-mismatch-agent",
            """{"unexpectedField": "value"}""",
            "direct");
    }
}

/// <summary>
/// Mock agent — returns configured responses deterministically.
/// </summary>
public sealed class MockAgent
{
    public IntentResponse ConfiguredResponse { get; set; } = IntentResponse.Ok("mock", "mock-agent", "mock result", "direct");
    public int CallCount { get; private set; }
    public List<IntentEnvelope> ReceivedEnvelopes { get; } = new();

    public IntentResponse Handle(IntentEnvelope envelope)
    {
        CallCount++;
        ReceivedEnvelopes.Add(envelope);
        return ConfiguredResponse;
    }

    public void Reset()
    {
        CallCount = 0;
        ReceivedEnvelopes.Clear();
    }
}

/// <summary>
/// Duplicate-tracking agent — tracks duplicate requests by idempotency key.
/// </summary>
public sealed class IdempotentAgent
{
    public HashSet<string> ProcessedKeys { get; } = new();
    public List<IntentEnvelope> ReceivedRequests { get; } = new();

    public IntentResponse Handle(IntentEnvelope envelope)
    {
        ReceivedRequests.Add(envelope);
        var key = envelope.IdempotencyKey ?? envelope.RequestId;

        if (ProcessedKeys.Contains(key))
        {
            return IntentResponse.Failed(
                envelope.RequestId,
                "idempotent-agent",
                "Duplicate request detected",
                envelope.TraceId);
        }

        ProcessedKeys.Add(key);
        var resultPayload = "{\"processed_key\":\"" + key + "\"}";
        return IntentResponse.Ok(
            envelope.RequestId,
            "idempotent-agent",
            resultPayload,
            "direct");
    }

    public void Reset()
    {
        ProcessedKeys.Clear();
        ReceivedRequests.Clear();
    }
}
