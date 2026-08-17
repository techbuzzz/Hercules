namespace Hercules.SkillSdk;

/// <summary>
///     Safe HTTP request model exposed to file-based skills.
///     Only allowed domains (from agent Http.AllowedDomains config) may be called.
/// </summary>
public sealed class SkillHttpRequest
{
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = "";
    public Dictionary<string, string>? Headers { get; set; }
    public string? Body { get; set; }
}

/// <summary>
///     Safe HTTP response model returned to file-based skills.
/// </summary>
public sealed class SkillHttpResponse
{
    public int StatusCode { get; set; }
    public string Body { get; set; } = "";
    public Dictionary<string, string> Headers { get; set; } = new();
    public bool IsSuccess { get; set; }
    public string? Error { get; set; }
}
