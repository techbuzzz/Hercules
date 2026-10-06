using System.Collections.Concurrent;
using System.Security.Cryptography;
using Hercules.WebApi.Config;
using Microsoft.AspNetCore.Http;

namespace Hercules.WebApi.Auth;

/// <summary>
///     Session issued to a browser client after it presents its API key once.
/// </summary>
/// <param name="Token">Opaque bearer value. Never logged, never persisted.</param>
/// <param name="Role">Role granted by the API key that was exchanged.</param>
/// <param name="ExpiresAt">Absolute expiry; the client is expected to re-exchange.</param>
public sealed record StudioSession(
    string Token,
    ApiKeyRole Role,
    string AgentId,
    string DisplayName,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string> Capabilities);

/// <summary>
///     In-memory session registry backing <c>X-Session-Token</c>.
/// <para>
///     Purpose: Hercules Studio runs in a browser, where an API key cannot be kept
///     safely. The client exchanges its key once for a short-lived opaque token
///     which it holds only in tab memory. This keeps the long-lived secret on the
///     agent and out of localStorage.
/// </para>
/// <para>
///     Deliberately in-memory: a restart invalidates every session, which is the
///     correct failure mode — clients re-exchange. Persisting tokens would widen
///     the blast radius of a filesystem compromise for no operational gain.
/// </para>
/// </summary>
public sealed class StudioSessionStore
{
    public const string HeaderName = "X-Session-Token";

    private static readonly IReadOnlyList<string> ContributeCapabilities = new[]
    {
        "chat", "skills:read", "skills:write", "config:read", "config:patch",
        "mesh:read", "tools:read", "stats:read", "system:checkin",
    };

    private static readonly IReadOnlyList<string> SystemCapabilities = new[]
    {
        "chat", "skills:read", "skills:write", "config:read", "config:patch", "config:write",
        "mesh:read", "mesh:write", "tools:read", "tools:write", "stats:read",
        "system:checkin", "system:restart", "mcp:write", "quota:write",
    };

    private readonly ConcurrentDictionary<string, StudioSession> _sessions = new(StringComparer.Ordinal);
    private readonly ILogger<StudioSessionStore> _logger;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _ttl;

    public StudioSessionStore(WebApiConfig cfg, ILogger<StudioSessionStore> logger, TimeProvider? clock = null)
    {
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
        var minutes = cfg.StudioSessionTtlMinutes;
        _ttl = minutes <= 0 ? TimeSpan.FromMinutes(30) : TimeSpan.FromMinutes(minutes);
    }

    /// <summary>Lifetime of a freshly issued session.</summary>
    public TimeSpan Ttl => _ttl;

    public StudioSession Create(ApiKeyRole role, string agentId, string displayName)
    {
        Sweep();

        // 32 bytes of entropy, base64url — no padding, safe in a header value.
        var raw = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(raw).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var session = new StudioSession(
            token,
            role,
            agentId,
            displayName,
            _clock.GetUtcNow().Add(_ttl),
            role == ApiKeyRole.System ? SystemCapabilities : ContributeCapabilities);

        _sessions[token] = session;
        _logger.LogInformation(
            "[StudioSession] Issued {Role} session for agent {AgentId}, expires {ExpiresAt:O}",
            role, agentId, session.ExpiresAt);

        return session;
    }

    public bool TryValidate(string? token, out StudioSession? session)
    {
        session = null;
        if (string.IsNullOrEmpty(token)) return false;

        if (!_sessions.TryGetValue(token, out var found)) return false;

        if (found.ExpiresAt <= _clock.GetUtcNow())
        {
            // Lazy expiry: remove on access so a stale token cannot be replayed
            // after its TTL, even if no sweep has run since.
            _sessions.TryRemove(token, out _);
            return false;
        }

        session = found;
        return true;
    }

    /// <summary>Reads a validated session out of <see cref="HttpContext.Items"/>.</summary>
    public static StudioSession? From(HttpContext http) =>
        http.Items.TryGetValue(ItemKey, out var value) ? value as StudioSession : null;

    internal const string ItemKey = "StudioSession";

    /// <summary>Drops every session — used by the drain/restart path.</summary>
    public int RevokeAll()
    {
        var count = _sessions.Count;
        if (count > 0) _logger.LogInformation("[StudioSession] Revoked {Count} session(s)", count);
        _sessions.Clear();
        return count;
    }

    private void Sweep()
    {
        var now = _clock.GetUtcNow();
        var stale = _sessions.Where(kv => kv.Value.ExpiresAt <= now).Select(kv => kv.Key).ToArray();
        foreach (var key in stale) _sessions.TryRemove(key, out _);
        if (stale.Length > 0)
            _logger.LogDebug("[StudioSession] Swept {Count} expired session(s)", stale.Length);
    }
}