namespace Hercules.SkillSdk;

/// <summary>
///     Safe HTTP request model exposed to file-based skills.
///     Only allowed domains (from agent Http.AllowedDomains config) may be called.
/// </summary>
public sealed class SkillHttpRequest
{
    /// <summary>HTTP verb (<c>GET</c>, <c>POST</c>, …). Defaults to <c>GET</c>.</summary>
    public string Method { get; set; } = "GET";

    /// <summary>Absolute request URL. The host must appear in the agent's allow-list.</summary>
    public string Url { get; set; } = "";

    /// <summary>Optional request headers. Sensitive headers may be stripped by the host.</summary>
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>Optional request body for verbs that take one.</summary>
    public string? Body { get; set; }
}

/// <summary>
///     Safe HTTP response model returned to file-based skills.
/// </summary>
public sealed class SkillHttpResponse
{
    /// <summary>HTTP status code returned by the remote host.</summary>
    public int StatusCode { get; set; }

    /// <summary>Response body as text. Truncated by the agent's output limit.</summary>
    public string Body { get; set; } = "";

    /// <summary>Response headers exposed to the skill.</summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary><c>true</c> when the status code is in the 2xx range and no transport error occurred.</summary>
    public bool IsSuccess { get; set; }

    /// <summary>Transport or policy error message, or <c>null</c> when the call completed.</summary>
    public string? Error { get; set; }
}