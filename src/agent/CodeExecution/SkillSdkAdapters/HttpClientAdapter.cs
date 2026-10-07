using System.Text;
using System.Text.Json;
using Hercules.Config;
using Hercules.SkillSdk;
using Hercules.Tools;
using Microsoft.Extensions.Logging;

namespace Hercules.CodeExecution.SkillSdkAdapters;

/// <summary>
///     Agent-side implementation of <see cref="IHttpClient"/> for file-based skills.
///     Enforces the agent's Http.AllowedDomains allow-list.
/// </summary>
public sealed class HttpClientAdapter : IHttpClient
{
    private readonly HttpConfig _cfg;
    private readonly IHttpClientFactory? _httpFactory;
    private readonly ILogger _logger;

    public HttpClientAdapter(HttpConfig cfg, ILogger logger, IHttpClientFactory? httpFactory = null)
    {
        _cfg = cfg;
        _logger = logger;
        _httpFactory = httpFactory;
    }

    public Task<SkillHttpResponse> GetAsync(string url, CancellationToken ct = default)
    {
        return SendAsync(new SkillHttpRequest { Method = "GET", Url = url }, ct);
    }

    public Task<SkillHttpResponse> PostAsync(string url, string? body = null, CancellationToken ct = default)
    {
        return SendAsync(new SkillHttpRequest { Method = "POST", Url = url, Body = body }, ct);
    }

    public async Task<SkillHttpResponse> SendAsync(SkillHttpRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Url) || !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
        {
            return Fail("Invalid URL");
        }

        var host = uri.Host;

        if (!IsDomainAllowed(host, _cfg))
        {
            _logger.LogWarning("SkillSdk HTTP blocked: domain '{Host}' is not in allow-list", host);
            return Fail($"Domain '{host}' is not in the agent allow-list");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return Fail($"Scheme '{uri.Scheme}' is not allowed");
        }

        try
        {
            using var client = CreateClient();
            using var httpReq = new HttpRequestMessage(new HttpMethod(request.Method.ToUpperInvariant()), request.Url);

            if (request.Headers is not null)
            {
                foreach (var (key, value) in request.Headers)
                {
                    httpReq.Headers.TryAddWithoutValidation(key, value);
                }
            }

            if (!string.IsNullOrEmpty(request.Body))
            {
                httpReq.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
            }

            using var resp = await client.SendAsync(httpReq, ct);
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            var maxBytes = _cfg.MaxResponseSizeKb * 1024;
            var truncated = bytes.Length > maxBytes;
            var text = Encoding.UTF8.GetString(truncated ? bytes.AsSpan(0, maxBytes) : bytes);

            var headers = resp.Headers.ToDictionary(
                h => h.Key,
                h => string.Join(", ", h.Value),
                StringComparer.OrdinalIgnoreCase);

            _logger.LogInformation("SkillSdk HTTP {Method} {Host} → {StatusCode} ({Bytes} bytes)",
                request.Method, host, (int)resp.StatusCode, bytes.Length);

            return new SkillHttpResponse
            {
                StatusCode = (int)resp.StatusCode,
                Body = text,
                Headers = headers,
                IsSuccess = resp.IsSuccessStatusCode
            };
        }
        catch (TaskCanceledException)
        {
            return Fail($"Request timed out after {_cfg.TimeoutSeconds}s");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SkillSdk HTTP error for {Url}", request.Url);
            return Fail($"HTTP error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static SkillHttpResponse Fail(string error)
    {
        return new SkillHttpResponse
        {
            StatusCode = 0,
            Body = "",
            IsSuccess = false,
            Error = error
        };
    }

    internal static bool IsDomainAllowed(string host, HttpConfig cfg)
    {
        foreach (var pattern in cfg.AllowedDomains)
        {
            if (pattern == "*")
            {
                return true;
            }

            if (string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (pattern.StartsWith("*.") && host.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private HttpClient CreateClient()
    {
        if (_httpFactory is not null)
        {
            return _httpFactory.CreateClient(HttpTool.HttpClientName);
        }

        return new HttpClient { Timeout = TimeSpan.FromSeconds(_cfg.TimeoutSeconds) };
    }
}
