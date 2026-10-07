using Hercules.SkillSdk;

namespace Hercules.CodeExecution.SkillSdkAdapters;

/// <summary>
///     Agent-side implementation of <see cref="ISessionContext"/> for file-based skills.
/// </summary>
public sealed class SessionContext : ISessionContext
{
    public SessionContext(string sessionId, string? userId, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken)
    {
        SessionId = sessionId;
        UserId = userId;
        Metadata = metadata;
        CancellationToken = cancellationToken;
    }

    public string SessionId { get; }
    public string? UserId { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public CancellationToken CancellationToken { get; }
}
