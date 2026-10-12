namespace Hercules.SkillSdk;

/// <summary>
///     Current execution session info exposed to file-based skills.
/// </summary>
public interface ISessionContext
{
    /// <summary>Current session identifier.</summary>
    string SessionId { get; }

    /// <summary>Current user identifier (if known).</summary>
    string? UserId { get; }

    /// <summary>Agent-scoped metadata dictionary (read-only snapshot).</summary>
    IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>Cancellation token for the current skill execution.</summary>
    CancellationToken CancellationToken { get; }
}
