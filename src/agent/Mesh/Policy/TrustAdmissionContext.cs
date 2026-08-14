namespace Hercules.Mesh.Policy;

/// <summary>
///     Context for evaluating an inter-agent admission request.
///     Passed to <see cref="ITrustAdmissionPolicy.Evaluate"/>.
/// </summary>
public sealed class TrustAdmissionContext
{
    /// <summary>
    ///     Verified identity of the caller agent.
    ///     Null means no authentication was performed (anonymous/unauthenticated).
    /// </summary>
    public required Hercules.Mesh.Auth.IdentityResult? CallerIdentity { get; init; }

    /// <summary>
    ///     The incoming intent envelope from the caller.
    /// </summary>
    public required IntentEnvelope Envelope { get; init; }

    /// <summary>
    ///     AgentId of the target (receiving) agent being evaluated.
    /// </summary>
    public required string TargetAgentId { get; init; }

    /// <summary>
    ///     Capabilities declared by the target agent (from registry or manifest).
    /// </summary>
    public IReadOnlyList<ManifestCapability>? TargetCapabilities { get; init; }

    /// <summary>
    ///     Resource limits of the target agent (from manifest).
    /// </summary>
    public ManifestResourceLimits? TargetResourceLimits { get; init; }

    /// <summary>
    ///     Minimum schema version the target supports (from manifest).
    /// </summary>
    public string? TargetMinSchemaVersion { get; init; }

    /// <summary>
    ///     Trust level of the caller agent (from registry or manifest trust metadata).
    ///     If not set, defaults to <see cref="TrustLevel.Unverified"/>.
    /// </summary>
    public TrustLevel CallerTrustLevel { get; init; } = TrustLevel.Unverified;

    /// <summary>
    ///     Data classification hint from the caller's manifest or request.
    ///     If not set, defaults to <see cref="DataClassification.Public"/>.
    /// </summary>
    public DataClassification DataClassification { get; init; } = DataClassification.Public;

    /// <summary>
    ///     Risk level of the requested capability/intent.
    ///     Derived from target capability metadata.
    /// </summary>
    public string? RequestedRiskLevel { get; init; }

    /// <summary>
    ///     Schema version claimed by the caller (from envelope version field).
    /// </summary>
    public string? CallerSchemaVersion { get; init; }
}
