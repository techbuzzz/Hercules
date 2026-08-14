using Hercules.Mesh;
using Hercules.Mesh.Transport;

namespace Hercules.Agent.Tests.Phase3Tests.TestAgents;

/// <summary>
/// Test transport with controllable latency, failure injection, and response queuing.
/// Used for protocol fault simulation tests.
/// </summary>
public sealed class TestTransport : ITransport
{
    private readonly ICapabilityLookup _registry;
    private int _simulatedLatencyMs;
    private bool _injectFailure;
    private string _failureMessage = "Simulated transport failure";
    private TransportErrorKind _failureKind = TransportErrorKind.TransportError;
    private bool _disposed;

    public TestTransport(ICapabilityLookup registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public TransportKind Kind => TransportKind.Http;
    public bool SupportsBidirectionalStreaming => false;
    public DeliveryGuarantee DeliveryGuarantee => DeliveryGuarantee.AtMostOnce;

    /// <summary>Configure simulated network latency.</summary>
    public void SetLatency(int milliseconds)
    {
        _simulatedLatencyMs = milliseconds;
    }

    /// <summary>Configure failure injection for all calls.</summary>
    public void InjectFailure(string message, TransportErrorKind kind = TransportErrorKind.TransportError)
    {
        _injectFailure = true;
        _failureMessage = message;
        _failureKind = kind;
    }

    /// <summary>Clear failure injection.</summary>
    public void ClearFailure()
    {
        _injectFailure = false;
        _failureMessage = "Simulated transport failure";
        _failureKind = TransportErrorKind.TransportError;
    }

    public async Task<TransportResult> SendAsync(string targetAgentId, IntentEnvelope envelope, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TestTransport));

        // Check cancellation before starting work
        if (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException();
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (_simulatedLatencyMs > 0)
        {
            await Task.Delay(_simulatedLatencyMs, ct);
        }

        sw.Stop();

        if (_injectFailure)
        {
            return _failureKind switch
            {
                TransportErrorKind.Timeout => TransportResult.TimedOut(targetAgentId, envelope.TraceId, sw.ElapsedMilliseconds, TransportKind.Http),
                TransportErrorKind.Unreachable => TransportResult.Unreachable(targetAgentId, envelope.TraceId, _failureMessage, sw.ElapsedMilliseconds, TransportKind.Http),
                TransportErrorKind.TransportError => TransportResult.TransportError(targetAgentId, envelope.TraceId, _failureMessage, sw.ElapsedMilliseconds, TransportKind.Http),
                TransportErrorKind.Rejected => TransportResult.Rejected(targetAgentId, envelope.TraceId, _failureMessage, sw.ElapsedMilliseconds, TransportKind.Http),
                _ => TransportResult.TransportError(targetAgentId, envelope.TraceId, _failureMessage, sw.ElapsedMilliseconds, TransportKind.Http)
            };
        }

        // Return a successful response from the registry (if agent found)
        var manifest = _registry.TryGet(targetAgentId);
        if (manifest == null)
        {
            return TransportResult.Unreachable(targetAgentId, envelope.TraceId, "Agent not found", sw.ElapsedMilliseconds, TransportKind.Http);
        }

        return TransportResult.Ok(
            IntentResponse.Ok(envelope.RequestId, targetAgentId, "{\"status\":\"ok\"}", "direct"),
            sw.ElapsedMilliseconds,
            TransportKind.Http);
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
