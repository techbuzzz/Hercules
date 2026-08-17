using System.Collections.Concurrent;
using System.Diagnostics;
using Hercules.Config;
using Hercules.Mesh.Observability;
using Hercules.Mesh.Transport;
using Hercules.Slo;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Resilience;

/// <summary>
///     Resilient transport wrapper: wraps an inner <see cref="ITransport"/> with
///     per-peer circuit breaker, exponential backoff + jitter retry, and bulkhead isolation.
///     Thread-safe; all state is lock-free via ConcurrentDictionary.
///     Specification: docs/ROADMAP-RU.md Phase 4 task_047.
/// </summary>
public sealed class ResilientTransport : ITransport
{
    private readonly ITransport _inner;
    private readonly CircuitBreaker _circuitBreaker;
    private readonly RetryPolicy _retryPolicy;
    private readonly ResilienceConfig _config;
    private readonly ILogger<ResilientTransport> _logger;
    private readonly IMeshObservabilityService? _observability;
    private readonly ISloLatencyTracker? _latencyTracker;

    // Per-peer bulkhead: limits concurrent calls to each peer
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _peerSemaphores = new(StringComparer.OrdinalIgnoreCase);

    // Last time the per-peer semaphore was acquired (for idle-based trimming, task_087).
    // Without this, _peerSemaphores grows unbounded as the mesh sees more agents over
    // its lifetime, even when most peers are long gone.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _peerLastUsed = new(StringComparer.OrdinalIgnoreCase);

    // Global bulkhead: limits total concurrent outbound calls
    private readonly SemaphoreSlim _globalSemaphore;

    // Idempotency-safe intent prefixes (lowercase for case-insensitive comparison)
    private readonly HashSet<string> _safeIntentPrefixes;

    public TransportKind Kind => _inner.Kind;
    public bool SupportsBidirectionalStreaming => _inner.SupportsBidirectionalStreaming;
    public DeliveryGuarantee DeliveryGuarantee => _inner.DeliveryGuarantee;

    public ResilientTransport(
        ITransport inner,
        CircuitBreaker circuitBreaker,
        RetryPolicy retryPolicy,
        ResilienceConfig config,
        ILogger<ResilientTransport> logger,
        IMeshObservabilityService? observability = null,
        ISloLatencyTracker? latencyTracker = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _circuitBreaker = circuitBreaker ?? throw new ArgumentNullException(nameof(circuitBreaker));
        _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _observability = observability;
        _latencyTracker = latencyTracker;

        _globalSemaphore = new SemaphoreSlim(_config.MaxConcurrentTotal, _config.MaxConcurrentTotal);

        // Build lowercase-safe prefix set for fast lookup
        _safeIntentPrefixes = new HashSet<string>(
            _config.IdempotencyKey.SafeIntents.Select(p => p.ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<TransportResult> SendAsync(string targetAgentId, IntentEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetAgentId);
        ArgumentNullException.ThrowIfNull(envelope);

        if (!_config.Enabled)
        {
            return await _inner.SendAsync(targetAgentId, envelope, ct);
        }

        // 1. Circuit breaker check — reject fast if peer is down
        if (!_circuitBreaker.CanSend(targetAgentId))
        {
            _logger.LogDebug(
                "[ResilientTransport] Circuit breaker open for {AgentId} — rejecting request {RequestId}",
                targetAgentId, envelope.RequestId);

            // Emit circuit-breaker rejection metric
            _observability?.RecordMeshEvent(null, "resilience.circuit_open",
                intent: envelope.Intent, senderAgentId: envelope.Sender,
                receiverAgentId: targetAgentId);

            return new TransportResult(
                IntentResponse.Rejected(envelope.RequestId, targetAgentId,
                    "Circuit breaker open — peer temporarily unavailable", envelope.TraceId),
                false,
                TransportErrorKind.Rejected,
                "Circuit breaker open",
                0,
                _inner.Kind);
        }

        // 2. Idempotency key: auto-generate for non-idempotent operations
        EnsureIdempotencyKey(envelope);

        // 3. Acquire per-peer bulkhead
        var peerSemaphore = _peerSemaphores.GetOrAdd(
            targetAgentId,
            _ => new SemaphoreSlim(_config.MaxConcurrentPerPeer, _config.MaxConcurrentPerPeer));
        // task_087: stamp last-used so TrimIdlePeers() can prune dead peers.
        _peerLastUsed[targetAgentId] = DateTimeOffset.UtcNow;

        // 4. Acquire global bulkhead
        await _globalSemaphore.WaitAsync(ct);

        // Start resilience span
        var span = _observability?.StartMeshSpan("ResilientTransport.Send",
            peerAgentId: targetAgentId, intent: envelope.Intent);
        var totalSw = Stopwatch.StartNew();

        try
        {
            // 5. Acquire per-peer bulkhead slot
            await peerSemaphore.WaitAsync(ct);

            try
            {
                var result = await SendWithRetryAsync(targetAgentId, envelope, span, ct);
                return result;
            }
            finally
            {
                peerSemaphore.Release();
            }
        }
        finally
        {
            totalSw.Stop();
            _globalSemaphore.Release();
            _observability?.RecordMeshEvent(span, "resilience.send_completed",
                intent: envelope.Intent, senderAgentId: envelope.Sender,
                receiverAgentId: targetAgentId);

            // task_087: feed the SLO latency tracker with the real end-to-end
            // duration. We record regardless of success/failure so the P95
            // reflects what the user actually experienced, not only happy paths.
            try
            {
                _latencyTracker?.RecordSample(envelope.Intent, totalSw.Elapsed.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[ResilientTransport] Latency tracker record failed");
            }
        }
    }

    /// <summary>
    ///     Auto-generate idempotency key for non-idempotent intents (unless disabled).
    ///     Safe intents (read/query/search/get/list) are skipped — they are naturally idempotent.
    /// </summary>
    private void EnsureIdempotencyKey(IntentEnvelope envelope)
    {
        if (!string.IsNullOrEmpty(envelope.IdempotencyKey))
        {
            return; // Already set
        }

        if (!_config.IdempotencyKey.AutoGenerate)
        {
            return;
        }

        // Check if intent is idempotent-safe
        var intentLower = envelope.Intent.ToLowerInvariant();
        if (_safeIntentPrefixes.Any(prefix => intentLower.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return; // Safe — no idempotency key needed
        }

        // Generate: {agentId}:{requestId}:{attempt}
        envelope.IdempotencyKey = $"{envelope.Sender ?? "unknown"}:{envelope.RequestId}";
    }

    /// <summary>
    ///     Retry loop with exponential backoff + jitter.
    ///     Records circuit breaker success/failure after each attempt.
    /// </summary>
    private async Task<TransportResult> SendWithRetryAsync(
        string targetAgentId, IntentEnvelope envelope, Activity? span, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (var attempt = 0; attempt < _retryPolicy.MaxAttempts; attempt++)
        {
            // Re-check circuit breaker on every attempt (it may have opened during retry)
            if (!_circuitBreaker.CanSend(targetAgentId))
            {
                _logger.LogDebug(
                    "[ResilientTransport] Circuit breaker opened mid-retry for {AgentId} on attempt {Attempt}",
                    targetAgentId, attempt + 1);

                sw.Stop();
                _observability?.RecordMeshEvent(span, "resilience.circuit_open",
                    intent: envelope.Intent, senderAgentId: envelope.Sender,
                    receiverAgentId: targetAgentId, hopCount: attempt + 1);
                return TransportResult.Rejected(targetAgentId, envelope.TraceId,
                    "Circuit breaker open", sw.ElapsedMilliseconds, _inner.Kind);
            }

            // Start per-attempt child span
            using var attemptSpan = _observability?.StartMeshSpan(
                $"ResilientTransport.Retry.{attempt + 1}",
                peerAgentId: targetAgentId, intent: envelope.Intent);

            var result = await _inner.SendAsync(targetAgentId, envelope, ct);
            sw.Stop();

            // Record in circuit breaker
            if (result.Response?.IsSuccess == true)
            {
                var prevState = _circuitBreaker.GetState(targetAgentId);
                _circuitBreaker.RecordSuccess(targetAgentId);
                EmitCircuitStateChangeIfChanged(targetAgentId, prevState, _circuitBreaker.GetState(targetAgentId), envelope.Intent);
                _observability?.RecordMeshEvent(attemptSpan, "transport.success",
                    intent: envelope.Intent, senderAgentId: envelope.Sender,
                    receiverAgentId: targetAgentId, hopCount: attempt + 1,
                    latencyMs: sw.ElapsedMilliseconds);
                return new TransportResult(
                    result.Response, true, TransportErrorKind.None,
                    result.ErrorMessage, sw.ElapsedMilliseconds, _inner.Kind);
            }

            var prevFailureState = _circuitBreaker.GetState(targetAgentId);
            _circuitBreaker.RecordFailure(targetAgentId);
            EmitCircuitStateChangeIfChanged(targetAgentId, prevFailureState, _circuitBreaker.GetState(targetAgentId), envelope.Intent);
            _observability?.RecordMeshEvent(attemptSpan, "transport.failure",
                intent: envelope.Intent, senderAgentId: envelope.Sender,
                receiverAgentId: targetAgentId, hopCount: attempt + 1,
                error: result.ErrorMessage ?? result.Response?.Error);

            // Check deadline: do not retry if time is up
            var deadline = envelope.GetDeadlineOrDefault(30_000);
            if (DateTimeOffset.UtcNow >= deadline)
            {
                _logger.LogDebug(
                    "[ResilientTransport] Deadline expired for {AgentId} on attempt {Attempt}",
                    targetAgentId, attempt + 1);

                return new TransportResult(
                    result.Response, false, result.Kind,
                    result.ErrorMessage, sw.ElapsedMilliseconds, _inner.Kind);
            }

            // Decide whether to retry
            if (!_retryPolicy.ShouldRetry(result.Response!, attempt))
            {
                _logger.LogDebug(
                    "[ResilientTransport] Non-retryable response from {AgentId} on attempt {Attempt}: {Status}",
                    targetAgentId, attempt + 1, result.Response?.Status);

                return new TransportResult(
                    result.Response, false, result.Kind,
                    result.ErrorMessage, sw.ElapsedMilliseconds, _inner.Kind);
            }

            // Calculate delay with jitter
            var delay = _retryPolicy.GetDelay(attempt + 1);

            // Adjust delay so we don't exceed deadline
            var remaining = deadline - DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(200); // 200ms safety margin
            if (remaining < TimeSpan.Zero)
            {
                _logger.LogDebug("[ResilientTransport] No time left for retry for {AgentId}", targetAgentId);
                return new TransportResult(
                    result.Response, false, result.Kind,
                    result.ErrorMessage, sw.ElapsedMilliseconds, _inner.Kind);
            }

            if (delay > remaining)
            {
                delay = remaining;
            }

            _logger.LogDebug(
                "[ResilientTransport] Retry {Attempt}/{Max} for {AgentId} after {Delay}ms. Error: {Error}",
                attempt + 2, _retryPolicy.MaxAttempts, targetAgentId, delay.TotalMilliseconds,
                result.ErrorMessage ?? result.Response?.Error);

            // Emit retry metric
            _observability?.RecordMeshMetric("retry_attempt", attempt + 1,
                peerAgentId: targetAgentId, intent: envelope.Intent,
                outcome: "retrying");

            try
            {
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                return new TransportResult(
                    result.Response, false, result.Kind,
                    result.ErrorMessage, sw.ElapsedMilliseconds, _inner.Kind);
            }

            sw.Start(); // Resume timing for next attempt
        }

        sw.Stop();
        _observability?.RecordMeshEvent(span, "resilience.exhausted",
            intent: envelope.Intent, senderAgentId: envelope.Sender,
            receiverAgentId: targetAgentId, error: $"Exhausted {_retryPolicy.MaxAttempts} attempts");
        return new TransportResult(
            IntentResponse.Failed(envelope.RequestId, targetAgentId,
                $"Exhausted {_retryPolicy.MaxAttempts} attempts", envelope.TraceId),
            false,
            TransportErrorKind.TransportError,
            $"Exhausted {_retryPolicy.MaxAttempts} retry attempts",
            sw.ElapsedMilliseconds,
            _inner.Kind);
    }

    public void Dispose()
    {
        _globalSemaphore.Dispose();
        foreach (var sem in _peerSemaphores.Values)
        {
            sem.Dispose();
        }
        _peerSemaphores.Clear();
        _peerLastUsed.Clear();
    }

    // ── Circuit-breaker state-change metric (task_093) ─────────────────────
    //
    // The circuit breaker transitions between Closed / Open / HalfOpen but does
    // not itself emit telemetry. We capture the state before and after each
    // RecordSuccess / RecordFailure call and, when it changed, fire a
    // `circuit_breaker_state_change` counter so the diagnostics service (and
    // the web UI) can show a meaningful "circuit flipped" signal.

    private void EmitCircuitStateChangeIfChanged(string agentId, CircuitState prev, CircuitState next, string intent)
    {
        if (prev == next) return;
        try
        {
            _observability?.RecordMeshMetric(
                "circuit_breaker_state_change",
                1,
                peerAgentId: agentId,
                intent: intent,
                outcome: $"{prev}->{next}");
        }
        catch (Exception ex)
        {
            // Diagnostics is best-effort: never let telemetry failure break the transport.
            _logger.LogDebug(ex, "[ResilientTransport] circuit_breaker_state_change metric failed");
        }
    }

    // ── Peer-semaphore lifecycle (task_087) ─────────────────────────────────
    //
    // Without explicit pruning the per-peer semaphore map grows monotonically as
    // the mesh discovers (and forgets) peers over its lifetime. Each entry holds
    // a SemaphoreSlim backed by a kernel object — not free even when idle.
    // The hooks below let callers (and periodic sweepers) reclaim that memory.

    /// <summary>Number of peer semaphores currently held.</summary>
    public int TrackedPeerCount => _peerSemaphores.Count;

    /// <summary>
    ///     Snapshot of the peer IDs currently holding a per-peer semaphore.
    ///     Returned in stable ordinal order to make assertions deterministic.
    /// </summary>
    public IReadOnlyList<string> GetTrackedPeers()
        => _peerSemaphores.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    ///     Remove (and dispose) the per-peer semaphore for <paramref name="agentId"/>.
    ///     Returns true if a semaphore was actually removed; false if the peer was
    ///     not tracked. Safe to call from peer-removed event handlers.
    ///     No-op when the semaphore is currently held (WaitAsync in progress) — the
    ///     pending call completes against the still-allocated instance and a new
    ///     semaphore is allocated on the next SendAsync, avoiding use-after-free.
    /// </summary>
    public bool RemovePeer(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        if (!_peerSemaphores.TryRemove(agentId, out var sem))
        {
            _peerLastUsed.TryRemove(agentId, out _);
            return false;
        }

        _peerLastUsed.TryRemove(agentId, out _);

        // Only dispose when the semaphore is free. SemaphoreSlim doesn't expose
        // CurrentCount without a race, so we attempt a zero-wait acquire. If
        // someone is mid-call, they will finish against this instance (still
        // referenced by their local) and the next SendAsync will allocate a
        // fresh one — at worst we leak one extra semaphore until the next
        // RemovePeer call for the same agent.
        try
        {
            if (sem.Wait(0))
            {
                sem.Release();
                sem.Dispose();
            }
            else
            {
                _logger.LogDebug(
                    "[ResilientTransport] RemovePeer({AgentId}) — semaphore in use, deferring dispose",
                    agentId);
            }
        }
        catch (ObjectDisposedException)
        {
            // Already disposed elsewhere.
        }

        return true;
    }

    /// <summary>
    ///     Remove per-peer semaphores that have not been used in
    ///     <paramref name="idleThreshold"/>. Returns the number of entries trimmed.
    ///     Idempotent and safe to call from a periodic timer.
    /// </summary>
    public int TrimIdlePeers(TimeSpan idleThreshold)
    {
        if (idleThreshold <= TimeSpan.Zero)
        {
            return 0;
        }

        var cutoff = DateTimeOffset.UtcNow - idleThreshold;
        var trimmed = 0;

        foreach (var kvp in _peerLastUsed.ToArray())
        {
            if (kvp.Value >= cutoff)
            {
                continue; // recently used
            }

            if (RemovePeer(kvp.Key))
            {
                trimmed++;
            }
        }

        if (trimmed > 0)
        {
            _logger.LogDebug(
                "[ResilientTransport] Trimmed {Count} idle peer semaphores (idleThreshold={IdleMinutes:F1}m)",
                trimmed, idleThreshold.TotalMinutes);
        }

        return trimmed;
    }
}
