namespace Hercules.Mesh.Audit;

/// <summary>
///     Audit sink abstraction — allows multiple backends for inter-agent audit records (task_041).
///     Implementations: SerilogMeshAuditSink (ILogger), OpenTelemetryMeshAuditSink (Activity),
///     FileMeshAuditSink (JSON Lines).
/// </summary>
public interface IAuditSink : IDisposable
{
    /// <summary>Human-readable name of this sink for observability.</summary>
    string Name { get; }

    /// <summary>Whether this sink is enabled and operational.</summary>
    bool IsEnabled { get; }

    /// <summary>
    ///     Write a single audit record asynchronously.
    ///     Implementations should be non-blocking and fault-tolerant.
    /// </summary>
    Task WriteAsync(InterAgentAuditRecord record, CancellationToken ct = default);
}
