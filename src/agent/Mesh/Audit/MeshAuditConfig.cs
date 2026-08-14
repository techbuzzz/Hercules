namespace Hercules.Mesh.Audit;

/// <summary>
///     Configuration for inter-agent audit trail (task_041).
///     Wire via <c>MeshConfig.InterAgentAudit</c>.
/// </summary>
public sealed class MeshAuditConfig
{
    /// <summary>Enable inter-agent audit trail. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Enable Serilog structured log sink. Default: true.</summary>
    public bool SerilogSinkEnabled { get; set; } = true;

    /// <summary>Enable OpenTelemetry Activity span sink. Default: true.</summary>
    public bool OpenTelemetrySinkEnabled { get; set; } = true;

    /// <summary>Enable JSON Lines file sink. Default: false (structured log is the primary).</summary>
    public bool FileSinkEnabled { get; set; } = false;

    /// <summary>Directory for JSON Lines audit files. Default: "data/mesh-audit".</summary>
    public string FileSinkDirectory { get; set; } = "mesh-audit";

    /// <summary>
    ///     Compute and store SHA-256 hash of payloads and responses.
    ///     Raw payloads are never stored. Default: true.
    /// </summary>
    public bool PayloadHashEnabled { get; set; } = true;

    /// <summary>
    ///     Redact payloads entirely from audit records (hash as well).
    ///     Use when payloads may contain PII and even hashes are too sensitive.
    ///     Default: false (hash is stored).
    /// </summary>
    public bool RedactPayloads { get; set; } = false;

    /// <summary>
    ///     Maximum payload length (in characters) to hash.
    ///     Payloads longer than this are truncated before hashing.
    ///     Default: 10 000.
    /// </summary>
    public int MaxPayloadLengthForHash { get; set; } = 10_000;

    /// <summary>
    ///     Track estimated cost (USD) per delegation.
    ///     Default: true.
    /// </summary>
    public bool CostTrackingEnabled { get; set; } = true;

    /// <summary>
    ///     Sample rate for high-volume paths (0.0–1.0). 1.0 = log everything.
    ///     Default: 1.0.
    /// </summary>
    public double SampleRate { get; set; } = 1.0;
}
