using Hercules.Mesh;
using Hercules.Mesh.Aggregation;
using Hercules.Mesh.Router;
using Hercules.Mesh.Schema;
using Hercules.Mesh.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Phase4Tests;

/// <summary>
///     Unit tests for Phase 4 Fan-Out / Fan-In (task_045).
///     Tests: FanOutOptions defaults, ResponseAggregator (schema validation, deterministic selection,
///     voting, LLM-judge fallback), FanOutOrchestrator (concurrency, budget, no-peers).
/// </summary>
public class FanOutTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FanOutOptions _options;

    public FanOutTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-fanout-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _options = new FanOutOptions
        {
            Enabled = true,
            MaxConcurrency = 3,
            BudgetCeilingUsd = 1.00m,
            DefaultTimeoutMs = 30_000,
            Strategy = FanOutSelectionStrategy.Deterministic,
            DeterministicCriterion = DeterministicCriterion.HighestConfidence,
            MinResponsesForVoting = 3,
            VotingThreshold = 0.51,
            EnableSchemaValidation = true,
            EnableLlmJudgeFallback = true,
            MinPeersForFanOut = 2
        };
    }

    // ─── FanOutOptions defaults ────────────────────────────────────────────────

    [Fact]
    public void FanOutOptions_Defaults_AreSensible()
    {
        var opts = new FanOutOptions();
        Assert.True(opts.Enabled);
        Assert.Equal(5, opts.MaxConcurrency);
        Assert.Equal(1.00m, opts.BudgetCeilingUsd);
        Assert.Equal(30_000, opts.DefaultTimeoutMs);
        Assert.Equal(FanOutSelectionStrategy.Deterministic, opts.Strategy);
        Assert.Equal(DeterministicCriterion.HighestConfidence, opts.DeterministicCriterion);
        Assert.Equal(3, opts.MinResponsesForVoting);
        Assert.Equal(0.51, opts.VotingThreshold);
        Assert.True(opts.EnableSchemaValidation);
        Assert.True(opts.EnableLlmJudgeFallback);
        Assert.Equal(2, opts.MinPeersForFanOut);
    }

    // ─── ResponseAggregator: schema validation ──────────────────────────────────

    [Fact]
    public void ValidateSchema_NullSchema_ReturnsNull()
    {
        var aggregator = new ResponseAggregator(_options, null, NullLogger<ResponseAggregator>.Instance);
        var response = IntentResponse.Ok("req1", "peer1", "{}", "direct");

        string? result = aggregator.ValidateSchema(response, null);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateSchema_ValidJson_ReturnsNull()
    {
        var aggregator = new ResponseAggregator(_options, null, NullLogger<ResponseAggregator>.Instance);
        var schema = ResponseSchema.Json();
        var response = IntentResponse.Ok("req1", "peer1", "{\"answer\": \"42\"}", "direct");

        string? result = aggregator.ValidateSchema(response, schema);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateSchema_InvalidJson_ReturnsViolation()
    {
        var aggregator = new ResponseAggregator(_options, null, NullLogger<ResponseAggregator>.Instance);
        var schema = ResponseSchema.Json();
        var response = IntentResponse.Ok("req1", "peer1", "not-json", "direct");

        string? result = aggregator.ValidateSchema(response, schema);
        Assert.NotNull(result);
        Assert.Contains("not valid JSON", result);
    }

    [Fact]
    public void ValidateSchema_TypeMismatch_ReturnsViolation()
    {
        var aggregator = new ResponseAggregator(_options, null, NullLogger<ResponseAggregator>.Instance);
        var schema = ResponseSchema.Json("{\"type\": \"object\"}");
        var response = IntentResponse.Ok("req1", "peer1", "42", "direct"); // number, not object

        string? result = aggregator.ValidateSchema(response, schema);
        Assert.NotNull(result);
        Assert.Contains("Type mismatch", result);
    }

    [Fact]
    public void ValidateSchema_MissingRequiredProperty_ReturnsViolation()
    {
        var aggregator = new ResponseAggregator(_options, null, NullLogger<ResponseAggregator>.Instance);
        var schema = ResponseSchema.Json("{\"type\": \"object\", \"required\": [\"answer\"]}");
        var response = IntentResponse.Ok("req1", "peer1", "{\"other\": \"x\"}", "direct");

        string? result = aggregator.ValidateSchema(response, schema);
        Assert.NotNull(result);
        Assert.Contains("Missing required property", result);
    }

    [Fact]
    public void ValidateSchema_TextSchema_AcceptsAnyString()
    {
        var aggregator = new ResponseAggregator(_options, null, NullLogger<ResponseAggregator>.Instance);
        var schema = ResponseSchema.Text();
        var response = IntentResponse.Ok("req1", "peer1", "any text here", "direct");

        string? result = aggregator.ValidateSchema(response, schema);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateSchema_SchemaValidationDisabled_AlwaysReturnsNull()
    {
        var opts = new FanOutOptions { EnableSchemaValidation = false };
        var aggregator = new ResponseAggregator(opts, null, NullLogger<ResponseAggregator>.Instance);
        var schema = ResponseSchema.Json();
        var response = IntentResponse.Ok("req1", "peer1", "invalid-json-!", "direct");

        string? result = aggregator.ValidateSchema(response, schema);
        Assert.Null(result); // no validation when disabled
    }

    // ─── ResponseAggregator: deterministic selection ─────────────────────────────

    [Fact]
    public async Task AggregateAsync_OnlyOneValid_ReturnsIt()
    {
        var aggregator = new ResponseAggregator(_options, null, NullLogger<ResponseAggregator>.Instance);
        var envelope = IntentEnvelope.Create("req1", "hercules", "test", "payload");
        var responses = new[] { IntentResponse.Ok("req1", "peer1", "result1", "direct") };
        var violations = new Dictionary<string, string>();

        var (winner, method, _) = await aggregator.AggregateAsync(envelope, responses, responses, violations);

        Assert.NotNull(winner);
        Assert.Equal("peer1", winner.Agent);
        Assert.Equal("only-valid-response", method);
    }

    [Fact]
    public async Task AggregateAsync_HighestConfidence_ReturnsBest()
    {
        var aggregator = new ResponseAggregator(_options, null, NullLogger<ResponseAggregator>.Instance);
        var envelope = IntentEnvelope.Create("req1", "hercules", "test", "payload");
        var responses = new[]
        {
            IntentResponse.Ok("req1", "peer1", "low", "direct", confidence: 0.3),
            IntentResponse.Ok("req1", "peer2", "high", "direct", confidence: 0.9),
            IntentResponse.Ok("req1", "peer3", "mid", "direct", confidence: 0.6),
        };
        var violations = new Dictionary<string, string>();

        var (winner, method, _) = await aggregator.AggregateAsync(envelope, responses, responses, violations);

        Assert.NotNull(winner);
        Assert.Equal("peer2", winner.Agent);
        Assert.Equal("deterministic-highestconfidence", method);
    }

    [Fact]
    public async Task AggregateAsync_FirstSuccess_ReturnsFirstInOrder()
    {
        var opts = new FanOutOptions { DeterministicCriterion = DeterministicCriterion.FirstSuccess };
        var aggregator = new ResponseAggregator(opts, null, NullLogger<ResponseAggregator>.Instance);
        var envelope = IntentEnvelope.Create("req1", "hercules", "test", "payload");
        var responses = new[]
        {
            IntentResponse.Ok("req1", "peer1", "first", "direct", confidence: 0.1),
            IntentResponse.Ok("req1", "peer2", "second", "direct", confidence: 0.9),
        };
        var violations = new Dictionary<string, string>();

        var (winner, method, _) = await aggregator.AggregateAsync(envelope, responses, responses, violations);

        Assert.NotNull(winner);
        Assert.Equal("peer1", winner.Agent);
        Assert.Equal("deterministic-firstsuccess", method);
    }

    [Fact]
    public async Task AggregateAsync_Latest_ReturnsMostRecent()
    {
        var opts = new FanOutOptions { DeterministicCriterion = DeterministicCriterion.Latest };
        var aggregator = new ResponseAggregator(opts, null, NullLogger<ResponseAggregator>.Instance);
        var envelope = IntentEnvelope.Create("req1", "hercules", "test", "payload");
        var responses = new[]
        {
            new IntentResponse("req1", "ok", "peer1", "direct", Timestamp: DateTimeOffset.UtcNow.AddMinutes(-5)),
            new IntentResponse("req1", "ok", "peer2", "direct", Timestamp: DateTimeOffset.UtcNow),
        };
        var violations = new Dictionary<string, string>();

        var (winner, method, _) = await aggregator.AggregateAsync(envelope, responses, responses, violations);

        Assert.NotNull(winner);
        Assert.Equal("peer2", winner.Agent);
        Assert.Equal("deterministic-latest", method);
    }

    [Fact]
    public async Task AggregateAsync_NoValidResponses_ReturnsNull()
    {
        var aggregator = new ResponseAggregator(_options, null, NullLogger<ResponseAggregator>.Instance);
        var envelope = IntentEnvelope.Create("req1", "hercules", "test", "payload");
        var responses = new[] { IntentResponse.Failed("req1", "peer1", "error") };
        var violations = new Dictionary<string, string>();
        var empty = Array.Empty<IntentResponse>();

        var (winner, method, _) = await aggregator.AggregateAsync(envelope, responses, empty, violations);

        Assert.Null(winner);
        Assert.Equal("no-valid-responses", method);
    }

    [Fact]
    public async Task AggregateAsync_FailedResponses_Excluded()
    {
        var aggregator = new ResponseAggregator(_options, null, NullLogger<ResponseAggregator>.Instance);
        var envelope = IntentEnvelope.Create("req1", "hercules", "test", "payload");
        var all = new[]
        {
            IntentResponse.Failed("req1", "peer1", "error"),
            IntentResponse.Ok("req1", "peer2", "ok", "direct"),
        };
        var valid = new[] { all[1] };
        var violations = new Dictionary<string, string>();

        var (winner, _, _) = await aggregator.AggregateAsync(envelope, all, valid, violations);

        Assert.NotNull(winner);
        Assert.Equal("peer2", winner.Agent);
    }

    // ─── ResponseAggregator: voting ───────────────────────────────────────────

    [Fact]
    public async Task AggregateAsync_VotingMajority_SelectsConsensus()
    {
        var opts = new FanOutOptions { Strategy = FanOutSelectionStrategy.Voting, VotingThreshold = 0.5, MinResponsesForVoting = 2 };
        var aggregator = new ResponseAggregator(opts, null, NullLogger<ResponseAggregator>.Instance);
        var envelope = IntentEnvelope.Create("req1", "hercules", "test", "payload");
        var responses = new[]
        {
            IntentResponse.Ok("req1", "peer1", "answer A", "direct"),
            IntentResponse.Ok("req1", "peer2", "answer A", "direct"),
            IntentResponse.Ok("req1", "peer3", "answer B", "direct"),
        };
        var violations = new Dictionary<string, string>();

        var (winner, method, _) = await aggregator.AggregateAsync(envelope, responses, responses, violations);

        Assert.NotNull(winner);
        Assert.Equal("answer A", winner.Result);
        Assert.StartsWith("voting-", method);
    }

    [Fact]
    public async Task AggregateAsync_VotingNoMajority_FallsBackToDeterministic()
    {
        var opts = new FanOutOptions
        {
            Strategy = FanOutSelectionStrategy.Voting,
            VotingThreshold = 0.8, // needs 80% — not met
            MinResponsesForVoting = 2,
            DeterministicCriterion = DeterministicCriterion.HighestConfidence
        };
        var aggregator = new ResponseAggregator(opts, null, NullLogger<ResponseAggregator>.Instance);
        var envelope = IntentEnvelope.Create("req1", "hercules", "test", "payload");
        var responses = new[]
        {
            IntentResponse.Ok("req1", "peer1", "A", "direct", confidence: 0.3),
            IntentResponse.Ok("req1", "peer2", "B", "direct", confidence: 0.9),
            IntentResponse.Ok("req1", "peer3", "C", "direct", confidence: 0.4),
        };
        var violations = new Dictionary<string, string>();

        var (winner, method, _) = await aggregator.AggregateAsync(envelope, responses, responses, violations);

        Assert.NotNull(winner);
        Assert.Equal("peer2", winner.Agent); // highest confidence wins in fallback
        Assert.Contains("deterministic", method);
    }

    [Fact]
    public async Task AggregateAsync_VotingBelowMinResponses_FallsBackToDeterministic()
    {
        var opts = new FanOutOptions { Strategy = FanOutSelectionStrategy.Voting, MinResponsesForVoting = 3 };
        var aggregator = new ResponseAggregator(opts, null, NullLogger<ResponseAggregator>.Instance);
        var envelope = IntentEnvelope.Create("req1", "hercules", "test", "payload");
        var responses = new[]
        {
            IntentResponse.Ok("req1", "peer1", "A", "direct"),
            IntentResponse.Ok("req1", "peer2", "A", "direct"),
        }; // only 2 responses, min=3
        var violations = new Dictionary<string, string>();

        var (winner, method, _) = await aggregator.AggregateAsync(envelope, responses, responses, violations);

        Assert.NotNull(winner);
        Assert.Contains("deterministic", method);
    }

    // ─── ResponseAggregator: LLM-judge ─────────────────────────────────────────

    [Fact]
    public async Task AggregateAsync_LlmJudgeNoClient_FallsBackToDeterministic()
    {
        // ILLMClient = null → LLM-judge path falls back
        var opts = new FanOutOptions { Strategy = FanOutSelectionStrategy.LlmJudge };
        var aggregator = new ResponseAggregator(opts, null, NullLogger<ResponseAggregator>.Instance);
        var envelope = IntentEnvelope.Create("req1", "hercules", "test", "payload");
        var responses = new[]
        {
            IntentResponse.Ok("req1", "peer1", "A", "direct", confidence: 0.5),
            IntentResponse.Ok("req1", "peer2", "B", "direct", confidence: 0.9),
        };
        var violations = new Dictionary<string, string>();

        var (winner, method, rationale) = await aggregator.AggregateAsync(envelope, responses, responses, violations);

        Assert.NotNull(winner);
        Assert.Contains("deterministic", method);
        // When LLM is unavailable, fallback uses deterministic directly with warning
        Assert.NotNull(rationale);
    }

    // ─── AggregationResult factories ─────────────────────────────────────────────

    [Fact]
    public void AggregationResult_Local_SetsCorrectFields()
    {
        var response = IntentResponse.Ok("req1", "local", "local-result", "direct");
        var result = AggregationResult.Local(response, TimeSpan.FromMilliseconds(100));

        Assert.NotNull(result.Winner);
        Assert.Equal("local-result", result.Winner.Result);
        Assert.Single(result.AllResponses);
        Assert.Equal("local", result.SelectionMethod);
        Assert.Equal(0, result.PeersContacted);
        Assert.Equal(1, result.SuccessCount);
        Assert.True(result.HasWinner);
    }

    [Fact]
    public void AggregationResult_NoPeers_SetsCorrectFields()
    {
        var result = AggregationResult.NoPeers(TimeSpan.FromMilliseconds(50));

        Assert.Null(result.Winner);
        Assert.Empty(result.AllResponses);
        Assert.Equal("no-peers", result.SelectionMethod);
        Assert.Equal(0, result.PeersContacted);
        Assert.False(result.HasWinner);
    }

    [Fact]
    public void AggregationResult_SinglePeer_SetsCorrectFields()
    {
        var response = IntentResponse.Ok("req1", "peer1", "ok", "direct");
        var result = AggregationResult.SinglePeer(response, TimeSpan.FromMilliseconds(200));

        Assert.NotNull(result.Winner);
        Assert.Equal("single-peer", result.SelectionMethod);
        Assert.Equal(1, result.PeersContacted);
        Assert.Equal(1, result.SuccessCount);
    }

    [Fact]
    public void AggregationResult_HasWinner_TrueForSuccess()
    {
        var ok = IntentResponse.Ok("req1", "peer", "result", "direct");
        var noWinner = AggregationResult.NoPeers(TimeSpan.Zero);
        var localWinner = AggregationResult.Local(ok, TimeSpan.Zero);

        Assert.True(localWinner.HasWinner);
        Assert.False(noWinner.HasWinner);
    }

    // ─── FanOutOrchestrator ─────────────────────────────────────────────────────

    [Fact]
    public async Task OrchestrateFanOut_Disabled_ReturnsNoPeers()
    {
        var opts = new FanOutOptions { Enabled = false };
        var mockRouter = new Mock<IMeshRouter>();
        var mockTransport = new Mock<ITransport>();
        var aggregator = new ResponseAggregator(opts, null, NullLogger<ResponseAggregator>.Instance);
        var orchestrator = new FanOutOrchestrator(
            mockRouter.Object, mockTransport.Object, aggregator, opts,
            new CircuitBreaker(),
            NullLogger<FanOutOrchestrator>.Instance);

        var envelope = IntentEnvelope.Create("req1", "hercules", "test", "payload");
        var result = await orchestrator.OrchestrateFanOutAsync(envelope, null, null, null);

        Assert.Equal("no-peers", result.SelectionMethod);
        Assert.Empty(result.AllResponses);
        mockRouter.Verify(r => r.RouteAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OrchestrateFanOut_NoPeersAvailable_ReturnsNoPeers()
    {
        var mockRouter = new Mock<IMeshRouter>();
        mockRouter.Setup(r => r.RouteAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PeerCandidate>());

        var mockTransport = new Mock<ITransport>();
        var aggregator = new ResponseAggregator(_options, null, NullLogger<ResponseAggregator>.Instance);
        var orchestrator = new FanOutOrchestrator(
            mockRouter.Object, mockTransport.Object, aggregator, _options,
            new CircuitBreaker(),
            NullLogger<FanOutOrchestrator>.Instance);

        var envelope = IntentEnvelope.Create("req1", "hercules", "test", "payload");
        var result = await orchestrator.OrchestrateFanOutAsync(envelope, null, null, null);

        Assert.Equal("no-peers", result.SelectionMethod);
        Assert.Empty(result.AllResponses);
    }

    [Fact]
    public async Task OrchestrateFanOut_SinglePeerBelowMinThreshold_UsesSinglePeer()
    {
        var peer = new PeerCandidate
        {
            AgentId = "peer1",
            DisplayName = "peer1",
            Endpoint = "http://localhost:9001",
            CompositeScore = 0.9,
            CostHintUsd = 0.01m,
            CircuitState = CircuitState.Closed
        };

        var mockRouter = new Mock<IMeshRouter>();
        mockRouter.Setup(r => r.RouteAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { peer });

        var mockTransport = new Mock<ITransport>();
        mockTransport.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransportResult.Ok(IntentResponse.Ok("req1", "peer1", "ok", "direct"), 0, TransportKind.Http));

        var aggregator = new ResponseAggregator(_options, null, NullLogger<ResponseAggregator>.Instance);
        var orchestrator = new FanOutOrchestrator(
            mockRouter.Object, mockTransport.Object, aggregator, _options,
            new CircuitBreaker(),
            NullLogger<FanOutOrchestrator>.Instance);

        var envelope = IntentEnvelope.Create("req1", "hercules", "test", "payload");
        var result = await orchestrator.OrchestrateFanOutAsync(envelope, null, null, null);

        Assert.Equal("single-peer", result.SelectionMethod);
        Assert.NotNull(result.Winner);
        Assert.Equal("peer1", result.Winner.Agent);
    }

    [Fact]
    public async Task OrchestrateFanOut_MultiplePeers_FansOutToAll()
    {
        var peers = new[]
        {
            new PeerCandidate { AgentId = "peer1", CompositeScore = 0.9, CostHintUsd = 0.01m, CircuitState = CircuitState.Closed },
            new PeerCandidate { AgentId = "peer2", CompositeScore = 0.8, CostHintUsd = 0.01m, CircuitState = CircuitState.Closed },
            new PeerCandidate { AgentId = "peer3", CompositeScore = 0.7, CostHintUsd = 0.01m, CircuitState = CircuitState.Closed },
        };

        var mockRouter = new Mock<IMeshRouter>();
        mockRouter.Setup(r => r.RouteAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(peers);

        var mockTransport = new Mock<ITransport>();
        var callCount = 0;
        mockTransport.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref callCount);
                return TransportResult.Ok(IntentResponse.Ok($"req1", $"peer{callCount}", $"result-{callCount}", "direct", confidence: 0.5 + callCount * 0.1), 0, TransportKind.Http);
            });

        var aggregator = new ResponseAggregator(_options, null, NullLogger<ResponseAggregator>.Instance);
        var orchestrator = new FanOutOrchestrator(
            mockRouter.Object, mockTransport.Object, aggregator, _options,
            new CircuitBreaker(),
            NullLogger<FanOutOrchestrator>.Instance);

        var envelope = IntentEnvelope.Create("req1", "hercules", "test", "payload");
        var result = await orchestrator.OrchestrateFanOutAsync(envelope, null, null, null);

        Assert.Equal(3, result.PeersContacted);
        Assert.Equal(3, result.AllResponses.Count);
        Assert.Equal(3, result.SuccessCount);
        Assert.True(result.HasWinner);
    }

    [Fact]
    public async Task OrchestrateFanOut_BudgetExceedsPeerCost_ExcludesPeer()
    {
        var peers = new[]
        {
            new PeerCandidate { AgentId = "cheap", CompositeScore = 0.9, CostHintUsd = 0.01m, CircuitState = CircuitState.Closed },
            new PeerCandidate { AgentId = "expensive", CompositeScore = 0.8, CostHintUsd = 10.00m, CircuitState = CircuitState.Closed },
        };

        var mockRouter = new Mock<IMeshRouter>();
        mockRouter.Setup(r => r.RouteAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(peers);

        var mockTransport = new Mock<ITransport>();
        mockTransport.Setup(t => t.SendAsync("cheap", It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransportResult.Ok(IntentResponse.Ok("req1", "cheap", "ok", "direct"), 0, TransportKind.Http));

        var aggregator = new ResponseAggregator(_options, null, NullLogger<ResponseAggregator>.Instance);
        var orchestrator = new FanOutOrchestrator(
            mockRouter.Object, mockTransport.Object, aggregator, _options,
            new CircuitBreaker(),
            NullLogger<FanOutOrchestrator>.Instance);

        var envelope = IntentEnvelope.Create("req1", "hercules", "test", "payload");
        var result = await orchestrator.OrchestrateFanOutAsync(envelope, null, null, 0.50m); // budget 50 cents

        // expensive peer should be excluded; cheap peer + min threshold = single peer mode
        Assert.Equal("single-peer", result.SelectionMethod);
        Assert.Equal("cheap", result.Winner?.Agent);
    }

    [Fact]
    public async Task OrchestrateFanOut_SchemaValidation_FlagsViolations()
    {
        var peers = new[]
        {
            new PeerCandidate { AgentId = "good", CompositeScore = 0.9, CostHintUsd = 0.01m, CircuitState = CircuitState.Closed },
            new PeerCandidate { AgentId = "bad", CompositeScore = 0.8, CostHintUsd = 0.01m, CircuitState = CircuitState.Closed },
        };

        var mockRouter = new Mock<IMeshRouter>();
        mockRouter.Setup(r => r.RouteAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(peers);

        var mockTransport = new Mock<ITransport>();
        mockTransport.Setup(t => t.SendAsync("good", It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransportResult.Ok(IntentResponse.Ok("req1", "good", "{\"answer\": 42}", "direct"), 0, TransportKind.Http));
        mockTransport.Setup(t => t.SendAsync("bad", It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransportResult.Ok(IntentResponse.Ok("req1", "bad", "not-json", "direct"), 0, TransportKind.Http));

        var aggregator = new ResponseAggregator(_options, null, NullLogger<ResponseAggregator>.Instance);
        var orchestrator = new FanOutOrchestrator(
            mockRouter.Object, mockTransport.Object, aggregator, _options,
            new CircuitBreaker(),
            NullLogger<FanOutOrchestrator>.Instance);

        var envelope = IntentEnvelope.Create("req1", "hercules", "test", "payload");
        var schema = ResponseSchema.Json();
        var result = await orchestrator.OrchestrateFanOutAsync(envelope, schema, null, null);

        Assert.Equal(2, result.AllResponses.Count);
        Assert.Equal(1, result.ValidResponses.Count);
        Assert.Single(result.SchemaViolations);
        Assert.True(result.SchemaViolations.ContainsKey("bad"));
    }

    [Fact]
    public async Task OrchestrateFanOut_StrategyOverride_UsesProvidedStrategy()
    {
        var peers = new[]
        {
            new PeerCandidate { AgentId = "peer1", CompositeScore = 0.9, CostHintUsd = 0.01m, CircuitState = CircuitState.Closed },
            new PeerCandidate { AgentId = "peer2", CompositeScore = 0.8, CostHintUsd = 0.01m, CircuitState = CircuitState.Closed },
        };

        var mockRouter = new Mock<IMeshRouter>();
        mockRouter.Setup(r => r.RouteAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(peers);

        var mockTransport = new Mock<ITransport>();
        mockTransport.Setup(t => t.SendAsync("peer1", It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransportResult.Ok(IntentResponse.Ok("req1", "peer1", "A", "direct", confidence: 0.3), 0, TransportKind.Http));
        mockTransport.Setup(t => t.SendAsync("peer2", It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransportResult.Ok(IntentResponse.Ok("req1", "peer2", "A", "direct", confidence: 0.9), 0, TransportKind.Http));

        var opts = new FanOutOptions { Strategy = FanOutSelectionStrategy.Voting, VotingThreshold = 0.5, MinResponsesForVoting = 2 };
        var aggregator = new ResponseAggregator(opts, null, NullLogger<ResponseAggregator>.Instance);
        var orchestrator = new FanOutOrchestrator(
            mockRouter.Object, mockTransport.Object, aggregator, opts,
            new CircuitBreaker(),
            NullLogger<FanOutOrchestrator>.Instance);

        var envelope = IntentEnvelope.Create("req1", "hercules", "test", "payload");
        var result = await orchestrator.OrchestrateFanOutAsync(envelope, null, FanOutSelectionStrategy.Voting, null);

        // Both returned "A" — should select via voting
        Assert.StartsWith("voting-", result.SelectionMethod);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }
}
