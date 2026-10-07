namespace Hercules.SkillSdk;

/// <summary>
///     Safe HTTP client for file-based skills.
///     Only domains from the agent allow-list may be called.
/// </summary>
public interface IHttpClient
{
    /// <summary>
    ///     Send an HTTP request to an allowed domain.
    ///     Throws <see cref="InvalidOperationException"/> if the domain is not allow-listed.
    /// </summary>
    Task<SkillHttpResponse> SendAsync(SkillHttpRequest request, CancellationToken ct = default);

    /// <summary>Convenience GET helper.</summary>
    Task<SkillHttpResponse> GetAsync(string url, CancellationToken ct = default);

    /// <summary>Convenience POST helper.</summary>
    Task<SkillHttpResponse> PostAsync(string url, string? body = null, CancellationToken ct = default);
}
