namespace Hercules.Mesh.Policy;

/// <summary>
///     Policy engine that evaluates inter-agent requests at the admission gate.
///     Enforces trust level, intent allow-lists, data classification, schema
///     version compatibility, and resource budget constraints before delegating
///     or processing a request.
///     Specification: docs/ROADMAP-RU.md Phase 3 task_040.
/// </summary>
public interface ITrustAdmissionPolicy
{
    /// <summary>
    ///     Evaluate an incoming inter-agent request.
    ///     Returns <see cref="TrustAdmissionResult.Allowed"/> if the request passes all checks,
    ///     or <see cref="TrustAdmissionResult.Denied"/> with a reason otherwise.
    /// </summary>
    TrustAdmissionResult Evaluate(TrustAdmissionContext ctx);

    /// <summary>Current policy enforcement mode.</summary>
    PolicyMode Mode { get; }
}

/// <summary>
///     Enforcement mode for the trust admission policy.
/// </summary>
public enum PolicyMode
{
    /// <summary>Policy is fully enforced — denied requests receive HTTP 403.</summary>
    Enforce,

    /// <summary>Policy decisions are logged but not enforced.</summary>
    DryRun,

    /// <summary>Policy is disabled — all requests are allowed.</summary>
    Disabled,
}
