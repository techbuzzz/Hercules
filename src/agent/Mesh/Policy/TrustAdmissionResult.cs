namespace Hercules.Mesh.Policy;

/// <summary>
///     Result of a trust admission policy evaluation.
/// </summary>
public sealed class TrustAdmissionResult
{
    /// <summary>Whether the request is allowed.</summary>
    public bool IsAllowed { get; }

    /// <summary>Human-readable reason for denial (null if allowed).</summary>
    public string? DenialReason { get; }

    /// <summary>
    ///     Primary reason for denial (null if allowed).
    ///     Useful for structured logging and audit.
    /// </summary>
    public TrustDenialReason? DenialCode { get; }

    /// <summary>Whether this was a dry-run evaluation (no enforcement).</summary>
    public bool DryRun { get; }

    private TrustAdmissionResult(bool isAllowed, string? denialReason, TrustDenialReason? denialCode, bool dryRun)
    {
        IsAllowed = isAllowed;
        DenialReason = denialReason;
        DenialCode = denialCode;
        DryRun = dryRun;
    }

    /// <summary>Request is allowed to proceed.</summary>
    public static TrustAdmissionResult Allowed(bool dryRun = false) =>
        new(isAllowed: true, denialReason: null, denialCode: null, dryRun);

    /// <summary>Request is denied.</summary>
    public static TrustAdmissionResult Denied(string reason, TrustDenialReason code, bool dryRun = false) =>
        new(isAllowed: false, denialReason: reason, denialCode: code, dryRun);
}
