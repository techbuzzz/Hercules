using System.Collections.Concurrent;
using System.Diagnostics;
using Hercules.Mesh.Aggregation;
using Hercules.Mesh.Schema;
using Hercules.Mesh.Transport;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Router;

/// <summary>
///     Fan-out orchestrator: sends an intent to multiple trusted peer agents in parallel,
///     validates responses against the requested schema, and aggregates results using
///     deterministic, voting, or LLM-judge strategy.
///     Specification: docs/ROADMAP-RU.md Phase 4 task_045.
/// </summary>
public sealed class FanOutOrchestrator : IFanOutOrchestrator
{
    private readonly IMeshRouter _meshRouter;
    private readonly ITransport _transport;
    private readonly ResponseAggregator _aggregator;
    private readonly FanOutOptions _options;
    private readonly ILogger<FanOutOrchestrator> _logger;

    public FanOutOrchestrator(
        IMeshRouter meshRouter,
        ITransport transport,
        ResponseAggregator aggregator,
        FanOutOptions options,
        ILogger<FanOutOrchestrator> logger)
    {
        _meshRouter = meshRouter ?? throw new ArgumentNullException(nameof(meshRouter));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _aggregator = aggregator ?? throw new ArgumentNullException(nameof(aggregator));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<AggregationResult> OrchestrateFanOutAsync(
        IntentEnvelope envelope,
        ResponseSchema? responseSchema,
        FanOutSelectionStrategy? strategy,
        decimal? budgetUsd,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        if (!_options.Enabled)
        {
            _logger.LogDebug("[FanOutOrchestrator] Fan-out disabled — returning no-peers result");
            return AggregationResult.NoPeers(sw.Elapsed);
        }

        // Resolve strategy
        FanOutSelectionStrategy resolvedStrategy = strategy ?? _options.Strategy;
        decimal budget = budgetUsd ?? _options.BudgetCeilingUsd;

        // 1. Query mesh router for ranked peer candidates
        IReadOnlyList<PeerCandidate> peers = await _meshRouter.RouteAsync(envelope.Intent, budget, ct);

        if (peers.Count == 0)
        {
            _logger.LogDebug(
                "[FanOutOrchestrator] No peers available for intent '{Intent}' within budget {Budget:C}",
                envelope.Intent, budget);
            return AggregationResult.NoPeers(sw.Elapsed);
        }

        // 2. Filter by budget ceiling
        peers = peers.Where(p => p.CostHintUsd <= budget).ToList();

        if (peers.Count == 0)
        {
            _logger.LogDebug("[FanOutOrchestrator] All peers exceed budget {Budget:C}", budget);
            return AggregationResult.NoPeers(sw.Elapsed);
        }

        // 3. Determine fan-out vs single-peer
        if (peers.Count < _options.MinPeersForFanOut)
        {
            _logger.LogDebug(
                "[FanOutOrchestrator] Only {Count} peers available (< MinPeersForFanOut={Min}) — single peer",
                peers.Count, _options.MinPeersForFanOut);

            IntentResponse single = await SendToSinglePeerAsync(peers[0], envelope, responseSchema, ct);
            sw.Stop();
            return AggregationResult.SinglePeer(single, sw.Elapsed);
        }

        // 4. Fan-out: limited concurrency
        var deadline = envelope.GetDeadlineOrDefault(_options.DefaultTimeoutMs);
        int concurrency = Math.Min(_options.MaxConcurrency, peers.Count);

        _logger.LogDebug(
            "[FanOutOrchestrator] Fan-out: {Count} peers, concurrency={Concurrency}, strategy={Strategy}, budget={Budget:C}",
            peers.Count, concurrency, resolvedStrategy, budget);

        List<IntentResponse> responses = await FanOutParallelAsync(peers, envelope, responseSchema, deadline, concurrency, ct);

        // 5. Validate against schema
        var validResponses = new List<IntentResponse>();
        var schemaViolations = new ConcurrentDictionary<string, string>();

        foreach (var response in responses)
        {
            string? violation = _aggregator.ValidateSchema(response, responseSchema);
            if (violation == null)
            {
                validResponses.Add(response);
            }
            else
            {
                schemaViolations.TryAdd(response.Agent, violation);
                _logger.LogDebug("[FanOutOrchestrator] Schema violation from {Agent}: {Violation}", response.Agent, violation);
            }
        }

        // 6. Apply selection strategy
        (IntentResponse? winner, string method, string? rationale) = await _aggregator.AggregateAsync(
            envelope, responses, validResponses, schemaViolations, ct);

        sw.Stop();

        return new AggregationResult
        {
            Winner = winner,
            AllResponses = responses,
            ValidResponses = validResponses,
            SelectionMethod = method,
            Duration = sw.Elapsed,
            JudgeRationale = rationale,
            SchemaViolations = schemaViolations,
            PeersContacted = peers.Count,
            SuccessCount = responses.Count(r => r.IsSuccess)
        };
    }

    /// <summary>
    ///     Send intent to a single peer (fallback when fan-out conditions not met).
    /// </summary>
    private async Task<IntentResponse> SendToSinglePeerAsync(
        PeerCandidate peer,
        IntentEnvelope envelope,
        ResponseSchema? responseSchema,
        CancellationToken ct)
    {
        var deadline = envelope.GetDeadlineOrDefault(_options.DefaultTimeoutMs);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(deadline - DateTimeOffset.UtcNow);

        try
        {
            var result = await _transport.SendAsync(peer.AgentId, envelope, linked.Token);
            var response = result.Response ?? IntentResponse.Failed(
                envelope.RequestId, peer.AgentId,
                result.ErrorMessage ?? "Transport error",
                envelope.TraceId);

            string? violation = _aggregator.ValidateSchema(response, responseSchema);
            if (violation != null)
            {
                _logger.LogWarning(
                    "[FanOutOrchestrator] Single-peer response from {Agent} failed schema validation: {Violation}",
                    peer.AgentId, violation);
                return IntentResponse.SchemaMismatch(
                    envelope.RequestId, peer.AgentId,
                    responseSchema?.JsonSchema ?? "unknown",
                    envelope.TraceId);
            }

            return response;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return IntentResponse.TimedOut(envelope.RequestId, peer.AgentId, envelope.TraceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[FanOutOrchestrator] Single-peer call to {Agent} failed", peer.AgentId);
            return IntentResponse.Failed(envelope.RequestId, peer.AgentId,
                $"{ex.GetType().Name}: {ex.Message}", envelope.TraceId);
        }
        finally
        {
            linked.Dispose();
        }
    }

    /// <summary>
    ///     Fan out to multiple peers in parallel with limited concurrency.
    /// </summary>
    private async Task<List<IntentResponse>> FanOutParallelAsync(
        IReadOnlyList<PeerCandidate> peers,
        IntentEnvelope envelope,
        ResponseSchema? responseSchema,
        DateTimeOffset deadline,
        int concurrency,
        CancellationToken ct)
    {
        var results = new ConcurrentBag<IntentResponse>();
        var deadlineMs = Math.Max(1, (int)(deadline - DateTimeOffset.UtcNow).TotalMilliseconds);

        using var semaphore = new SemaphoreSlim(concurrency, concurrency);
        var tasks = peers.Select(async peer =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                IntentResponse response = await SendToSinglePeerAsync(peer, envelope, responseSchema, ct);
                results.Add(response);
            }
            finally
            {
                semaphore.Release();
            }
        });

        using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        overallCts.CancelAfter(deadlineMs);

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("[FanOutOrchestrator] Fan-out deadline exceeded — some responses may be missing");
        }
        finally
        {
            overallCts.Dispose();
        }

        return results.ToList();
    }
}
