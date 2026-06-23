using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Hercules.Config;

namespace Hercules.Tools;

/// <summary>
///     HTTP-инструмент: GET/POST/PUT/DELETE с allow-list доменов, rate limit, timeout.
///     Только HTTPS (по умолчанию). HTTP allow через AllowedDomains (схема не проверяется).
///     Audit log: каждый вызов логируется в stderr (для парсинга в рефлексии).
/// </summary>
public sealed class HttpTool : ITool
{
    public string Name => "http";

    public string Description =>
        "Безопасные HTTP-запросы. GET/POST/PUT/DELETE. Allow-list доменов, rate limit, timeout. " +
        "Возвращает body и HTTP status. Для больших ответов — truncated.";

    public string? ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "method": { "type": "string", "enum": ["GET", "POST", "PUT", "DELETE"] },
            "url": { "type": "string", "description": "Полный URL (только из allow-list)" },
            "headers": { "type": "object", "description": "Доп. HTTP headers (опц.)" },
            "body": { "type": "string", "description": "Body для POST/PUT (опц.)" }
          },
          "required": ["method", "url"]
        }
        """;

    private readonly HttpConfig _cfg;
    private readonly HttpClient _http;
    private readonly RateLimiter _rateLimiter;

    public HttpTool(HttpConfig cfg)
    {
        _cfg = cfg;
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(cfg.TimeoutSeconds),
        };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Hercules", "2.0"));
        _rateLimiter = new RateLimiter(cfg.RateLimitPerMinute);
    }

    public async Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        HttpRequest? req;
        try
        {
            req = JsonSerializer.Deserialize<HttpRequest>(argumentsJson, JsonOpts);
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Invalid arguments JSON: {ex.Message}");
        }

        if (req is null || string.IsNullOrWhiteSpace(req.Method) || string.IsNullOrWhiteSpace(req.Url))
        {
            return ToolResult.Fail("Both 'method' and 'url' are required");
        }

        // Allow-list check
        if (!IsDomainAllowed(req.Url, out var host))
        {
            return ToolResult.Fail($"Domain '{host}' is not in the allow-list (allowed: {string.Join(", ", _cfg.AllowedDomains)})",
                new Dictionary<string, object> { ["blocked"] = host });
        }

        // Rate limit
        if (!_rateLimiter.TryAcquire())
        {
            return ToolResult.Fail($"Rate limit exceeded ({_cfg.RateLimitPerMinute}/min)",
                new Dictionary<string, object> { ["rate_limited"] = true });
        }

        try
        {
            using var httpReq = new HttpRequestMessage(new HttpMethod(req.Method.ToUpperInvariant()), req.Url);
            if (req.Headers is not null)
            {
                foreach (var kv in req.Headers)
                {
                    httpReq.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }
            }
            if (!string.IsNullOrEmpty(req.Body))
            {
                httpReq.Content = new StringContent(req.Body, Encoding.UTF8, "application/json");
            }

            using var resp = await _http.SendAsync(httpReq, ct);
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            var truncated = bytes.Length > _cfg.MaxResponseSizeKb * 1024;
            var text = Encoding.UTF8.GetString(truncated ? bytes.AsSpan(0, _cfg.MaxResponseSizeKb * 1024) : bytes);

            var meta = new Dictionary<string, object>
            {
                ["status"] = (int)resp.StatusCode,
                ["host"] = host,
                ["bytes"] = bytes.Length,
                ["truncated"] = truncated,
            };

            var status = resp.IsSuccessStatusCode ? "ok" : "http_error";
            var output = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}\n{text}";

            await Console.Error.WriteLineAsync(
                $"[HTTP] {req.Method} {host} → {(int)resp.StatusCode} ({bytes.Length} bytes)");

            return resp.IsSuccessStatusCode
                ? ToolResult.Ok(output, meta)
                : ToolResult.Fail(output, meta);
        }
        catch (TaskCanceledException)
        {
            return ToolResult.Fail($"Request timed out after {_cfg.TimeoutSeconds}s",
                new Dictionary<string, object> { ["timeout"] = true });
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"HTTP error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private bool IsDomainAllowed(string url, out string host)
    {
        host = "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }
        host = uri.Host;

        // HTTPS-only по умолчанию
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }

        foreach (var pattern in _cfg.AllowedDomains)
        {
            if (pattern == "*") return true;
            if (string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase)) return true;
            // Wildcard subdomain match: "*.example.com" matches "api.example.com"
            if (pattern.StartsWith("*.") && host.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private sealed class HttpRequest
    {
        public string? Method { get; set; }
        public string? Url { get; set; }
        public Dictionary<string, string>? Headers { get; set; }
        public string? Body { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}

/// <summary>
///     Простой sliding-window rate limiter (1 minute window).
///     Thread-safe (lock-based; для нашего use case достаточно).
/// </summary>
internal sealed class RateLimiter
{
    private readonly int _maxPerMinute;
    private readonly Queue<DateTime> _hits = new();
    private readonly object _lock = new();

    public RateLimiter(int maxPerMinute)
    {
        _maxPerMinute = maxPerMinute;
    }

    public bool TryAcquire()
    {
        if (_maxPerMinute <= 0) return true; // unlimited
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var windowStart = now.AddMinutes(-1);
            while (_hits.Count > 0 && _hits.Peek() < windowStart)
            {
                _hits.Dequeue();
            }
            if (_hits.Count >= _maxPerMinute)
            {
                return false;
            }
            _hits.Enqueue(now);
            return true;
        }
    }
}
