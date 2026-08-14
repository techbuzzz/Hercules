namespace Hercules.Mesh.Policy;

/// <summary>
///     Trust level of a peer agent.
///     Derived from identity verification and optional external attestation.
/// </summary>
public enum TrustLevel
{
    /// <summary>Agent identity has not been verified — treat with caution.</summary>
    Unverified = 0,

    /// <summary>Agent identity verified but not yet audited — limited trust.</summary>
    ProvisionallyTrusted = 1,

    /// <summary>Agent is trusted within the mesh based on peer attestation.</summary>
    Trusted = 2,

    /// <summary>Agent verified via external attestation (CA, mTLS, etc.).</summary>
    Verified = 3,
}

/// <summary>
///     Data sensitivity classification for inter-agent requests.
///     Inherited from the caller's trust metadata and payload hints.
/// </summary>
public enum DataClassification
{
    /// <summary>No sensitive data — safe to delegate broadly.</summary>
    Public = 0,

    /// <summary>Internal business data — restricted to trusted agents.</summary>
    Internal = 1,

    /// <summary>Confidential data — restricted to verified/trusted agents only.</summary>
    Confidential = 2,

    /// <summary>Restricted data — no delegation allowed.</summary>
    Restricted = 3,
}

/// <summary>
///     Reasons for denying an inter-agent request at the admission gate.
/// </summary>
public enum TrustDenialReason
{
    /// <summary>Trust level of the caller is below the configured threshold.</summary>
    TrustLevelTooLow,

    /// <summary>Caller intent is not in the allowed-intent list.</summary>
    IntentNotAllowed,

    /// <summary>Data classification exceeds the allowed level for this target.</summary>
    ClassificationTooHigh,

    /// <summary>Protocol/schema version mismatch between caller and target.</summary>
    SchemaVersionMismatch,

    /// <summary>Request budget (tokens, cost, wall-clock) exceeds target limits.</summary>
    BudgetLimitExceeded,

    /// <summary>Risk level of the requested operation exceeds allowed threshold.</summary>
    RiskLevelMismatch,

    /// <summary>Caller is not allow-listed by identity.</summary>
    IdentityNotAllowListed,
}
