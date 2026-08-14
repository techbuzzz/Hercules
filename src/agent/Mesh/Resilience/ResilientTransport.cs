using System.Collections.Concurrent;
using System.Diagnostics;
using Hercules.Config;
using Hercules.Mesh.Observability;
using Hercules.Mesh.Transport;
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

    // Per-peer bulkhead: limits concurrent calls to each peer
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _peerSemaphores = new(StringComparer.OrdinalIgnoreCase);

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
        IMeshObservabilityService? observability = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _circuitBreaker = circuitBreaker ?? throw new ArgumentNullException(nameof(circuitBreaker));
        _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _observability = observability;

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

        // 4. Acquire global bulkhead
        await _globalSemaphore.WaitAsync(ct);

        // Start resilience span
        var span = _observability?.StartMeshSpan("ResilientTransport.Send",
            peerAgentId: targetAgentId, intent: envelope.Intent);

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
            _globalSemaphore.Release();
            _observability?.RecordMeshEvent(span, "resilience.send_completed",
                intent: envelope.Intent, senderAgentId: envelope.Sender,
                receiverAgentId: targetAgentId);
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
                _circuitBreaker.RecordSuccess(targetAgentId);
                _observability?.RecordMeshEvent(attemptSpan, "transport.success",
                    intent: envelope.Intent, senderAgentId: envelope.Sender,
                    receiverAgentId: targetAgentId, hopCount: attempt + 1,
                    latencyMs: sw.ElapsedMilliseconds);
                return new TransportResult(
                    result.Response, true, TransportErrorKind.None,
                    result.ErrorMessage, sw.ElapsedMilliseconds, _inner.Kind);
            }

            _circuitBreaker.RecordFailure(targetAgentId);
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
    }
}
