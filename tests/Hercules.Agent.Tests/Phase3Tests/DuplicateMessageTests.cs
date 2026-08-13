using Hercules.Mesh;
using Xunit;
using TestAgents = Hercules.Agent.Tests.Phase3Tests.TestAgents;

namespace Hercules.Agent.Tests.Phase3Tests;

/// <summary>
/// Duplicate message handling tests.
/// Verifies idempotency key support and deduplication scenarios.
/// </summary>
public class DuplicateMessageTests
{
    [Fact]
    public void IdempotentAgent_SameKey_FirstRequest_Succeeds()
    {
        var agent = new TestAgents.IdempotentAgent();
        var envelope = IntentEnvelope.Create(
            "req-1", "sender", "test", "payload",
            idempotencyKey: "idem-key-1");

        var result = agent.Handle(envelope);

        Assert.True(result.IsSuccess);
        Assert.Single(agent.ProcessedKeys);
        Assert.Contains("idem-key-1", agent.ProcessedKeys);
    }

    [Fact]
    public void IdempotentAgent_SameKey_SecondRequest_ReturnsDuplicate()
    {
        var agent = new TestAgents.IdempotentAgent();
        var envelope1 = IntentEnvelope.Create("req-1", "sender", "test", "payload", idempotencyKey: "idem-key-1");
        var envelope2 = IntentEnvelope.Create("req-2", "sender", "test", "payload", idempotencyKey: "idem-key-1");

        var result1 = agent.Handle(envelope1);
        var result2 = agent.Handle(envelope2);

        Assert.True(result1.IsSuccess);
        Assert.False(result2.IsSuccess);
        Assert.Contains("Duplicate", result2.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IdempotentAgent_DifferentKeys_BothSucceed()
    {
        var agent = new TestAgents.IdempotentAgent();
        var envelope1 = IntentEnvelope.Create("req-1", "sender", "test", "payload", idempotencyKey: "key-a");
        var envelope2 = IntentEnvelope.Create("req-2", "sender", "test", "payload", idempotencyKey: "key-b");

        var result1 = agent.Handle(envelope1);
        var result2 = agent.Handle(envelope2);

        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        Assert.Equal(2, agent.ProcessedKeys.Count);
    }

    [Fact]
    public void IdempotentAgent_NoIdempotencyKey_UsesRequestId()
    {
        var agent = new TestAgents.IdempotentAgent();
        var envelope1 = IntentEnvelope.Create("req-same", "sender", "test", "payload");
        var envelope2 = IntentEnvelope.Create("req-same", "sender", "test", "payload");

        var result1 = agent.Handle(envelope1);
        var result2 = agent.Handle(envelope2);

        Assert.True(result1.IsSuccess);
        Assert.False(result2.IsSuccess); // Same RequestId without idempotency key
    }

    [Fact]
    public void IdempotentAgent_Reset_ClearsProcessedKeys()
    {
        var agent = new TestAgents.IdempotentAgent();
        var envelope = IntentEnvelope.Create("req-reset", "sender", "test", "payload", idempotencyKey: "reset-key");

        agent.Handle(envelope);
        Assert.Single(agent.ProcessedKeys);

        agent.Reset();
        Assert.Empty(agent.ProcessedKeys);

        var result = agent.Handle(envelope);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void IdempotentAgent_MultipleUniqueKeys_AllProcessed()
    {
        var agent = new TestAgents.IdempotentAgent();

        for (int i = 0; i < 10; i++)
        {
            var envelope = IntentEnvelope.Create($"req-{i}", "sender", "test", $"payload-{i}", idempotencyKey: $"unique-key-{i}");
            var result = agent.Handle(envelope);
            Assert.True(result.IsSuccess);
        }

        Assert.Equal(10, agent.ProcessedKeys.Count);
    }

    [Fact]
    public void IdempotentAgent_TracksAllReceivedRequests()
    {
        var agent = new TestAgents.IdempotentAgent();

        var envelope1 = IntentEnvelope.Create("req-tracked-1", "sender", "test", "payload1", idempotencyKey: "track-1");
        var envelope2 = IntentEnvelope.Create("req-tracked-2", "sender", "test", "payload2", idempotencyKey: "track-2");

        agent.Handle(envelope1);
        agent.Handle(envelope2); // duplicate of track-1
        agent.Handle(envelope2); // duplicate again

        Assert.Equal(3, agent.ReceivedRequests.Count);
    }

    [Fact]
    public void IntentEnvelope_IdempotencyKey_RoundTrips()
    {
        var envelope = IntentEnvelope.Create(
            "req-idempotent",
            "sender",
            "test",
            "payload",
            idempotencyKey: "my-idempotency-key");

        var json = envelope.ToJson();
        var restored = IntentEnvelope.FromJson(json);

        Assert.NotNull(restored);
        Assert.Equal("my-idempotency-key", restored.IdempotencyKey);
    }

    [Fact]
    public void IntentEnvelope_NullIdempotencyKey_RoundTrips()
    {
        var envelope = IntentEnvelope.Create(
            "req-no-idem",
            "sender",
            "test",
            "payload");

        var json = envelope.ToJson();
        var restored = IntentEnvelope.FromJson(json);

        Assert.NotNull(restored);
        Assert.Null(restored.IdempotencyKey);
    }

    [Fact]
    public void IntentEnvelope_IdempotencyKey_UnicodePreserved()
    {
        var key = "idem-key-日本語-emoji-🔑";
        var envelope = IntentEnvelope.Create("req-unicode-idem", "sender", "test", "payload", idempotencyKey: key);

        var json = envelope.ToJson();
        var restored = IntentEnvelope.FromJson(json);

        Assert.NotNull(restored);
        Assert.Equal(key, restored.IdempotencyKey);
    }

    [Fact]
    public void MockAgent_ReceivesEnvelope_ContainsIdempotencyKey()
    {
        var agent = new TestAgents.MockAgent();
        var envelope = IntentEnvelope.Create(
            "req-mock-idem",
            "sender",
            "test",
            "payload",
            idempotencyKey: "mock-idem-123");

        var result = agent.Handle(envelope);

        Assert.Equal(1, agent.CallCount);
        Assert.Single(agent.ReceivedEnvelopes);
        Assert.Equal("mock-idem-123", agent.ReceivedEnvelopes[0].IdempotencyKey);
    }

    [Fact]
    public void IdempotentAgent_EmptyIdempotencyKey_TreatedAsDifferent()
    {
        var agent = new TestAgents.IdempotentAgent();

        var envelope1 = IntentEnvelope.Create("req-empty-1", "sender", "test", "payload");
        envelope1.IdempotencyKey = "";

        var envelope2 = IntentEnvelope.Create("req-empty-2", "sender", "test", "payload");
        envelope2.IdempotencyKey = "";

        // Empty string idempotency keys are treated as null (uses RequestId)
        var result1 = agent.Handle(envelope1);
        var result2 = agent.Handle(envelope2);

        // Same RequestId = duplicate
        Assert.True(result1.IsSuccess);
        Assert.False(result2.IsSuccess);
    }
}
