using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Hercules.Supervisor;

/// <summary>
/// Client for the agent's supervisor-facing endpoints.
/// <para>
/// Authenticates the same way the browser client does (ADR-0009): the system API
/// key is exchanged once for a short-lived session token, which is then attached
/// to each poll. On 401/403 the token is re-exchanged once before giving up, so
/// a token that expires mid-run does not require a supervisor restart.
/// </para>
/// </summary>
public sealed class SupervisorApiClient(HttpClient http, ILogger<SupervisorApiClient> logger)
{
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    /// <summary>When false, requests are sent unauthenticated (useful for dry runs).</summary>
    public bool HasCredentials { get; set; }

    private async Task<string?> GetTokenAsync(CancellationToken ct)
    {
        if (!HasCredentials) return null;
        if (_token is not null && _expiresAt - DateTimeOffset.UtcNow > TimeSpan.FromMinutes(1)) return _token;

        await _tokenGate.WaitAsync(ct);
        try
        {
            if (_token is not null && _expiresAt - DateTimeOffset.UtcNow > TimeSpan.FromMinutes(1)) return _token;

            using var res = await http.PostAsJsonAsync(
                "/api/studio/session",
                new { },
                ct);
            // The exchange endpoint authenticates via X-Api-Key, which the
            // delegating handler below attaches. Body is intentionally empty.
            var session = await res.Content.ReadFromJsonAsync<StudioSessionResponse>(ct);
            if (session is null || string.IsNullOrEmpty(session.Token))
                throw new InvalidOperationException($"Session exchange returned no token ({(int)res.StatusCode})");

            _token = session.Token;
            _expiresAt = session.ExpiresAt;
            logger.LogInformation("Obtained agent session token, expires {ExpiresAt:O}", _expiresAt);
            return _token;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    /// <summary>Gets the current restart-pending state, or null when unavailable.</summary>
    public async Task<RestartPendingResponse?> GetRestartPendingAsync(CancellationToken ct)
    {
        var res = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, "/api/system/restart-pending"), ct);
        if (res is null) return null;
        if (!res.IsSuccessStatusCode)
        {
            logger.LogWarning("restart-pending poll failed: HTTP {Code}", (int)res.StatusCode);
            res.Dispose();
            return null;
        }
        return await res.Content.ReadFromJsonAsync<RestartPendingResponse>(ct);
    }

    /// <summary>Clears the restart flag after performing the restart.</summary>
    public async Task ClearRestartAsync(CancellationToken ct)
    {
        var res = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "/api/system/restart/clear") { Content = new StringContent("{}") },
            ct);
        res?.Dispose();
    }

    private async Task<HttpResponseMessage?> SendAsync(Func<HttpRequestMessage> factory, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);

        async Task<HttpResponseMessage> Send(string? t)
        {
            var req = factory();
            if (t is not null) req.Headers.TryAddWithoutValidation("X-Session-Token", t);
            return await http.SendAsync(req, ct);
        }

        var res = await Send(token);
        if (res.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
            && token is not null)
        {
            // Exactly one retry after re-exchanging; a second failure is a real
            // credentials problem and must not become a retry storm.
            res.Dispose();
            _token = null;
            var fresh = await GetTokenAsync(ct);
            res = await Send(fresh);
        }
        return res;
    }

    /// <summary>
    /// Delegating handler attaching the raw API key to the exchange call only.
    /// Every subsequent request uses the session token instead.
    /// </summary>
    public sealed class ApiKeyHandler : DelegatingHandler
    {
        private readonly string _apiKey;

        public ApiKeyHandler(string apiKey)
            : this(apiKey, new HttpClientHandler())
        {
        }

        public ApiKeyHandler(string apiKey, HttpMessageHandler inner)
            : base(inner)
        {
            _apiKey = apiKey;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(_apiKey))
            {
                request.Headers.TryAddWithoutValidation("X-Api-Key", _apiKey);
            }
            return base.SendAsync(request, cancellationToken);
        }
    }
}