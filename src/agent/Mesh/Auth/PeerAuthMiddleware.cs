using System.Text.Json;
using Hercules.Mesh.Schema;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Auth;

/// <summary>
///     Middleware that authenticates inter-agent mesh requests and attaches
///     the verified <see cref="IdentityResult"/> to <c>HttpContext.Items["MeshIdentity"]</c>.
///     Only applies to /api/mesh/* routes; other endpoints (agent chat, etc.) are unaffected.
///     Specification: docs/ROADMAP-RU.md Phase 3 task_039.
/// </summary>
public sealed class PeerAuthMiddleware
{
    /// <summary>Key in HttpContext.Items where the verified identity is stored.</summary>
    public const string IdentityItemKey = "Mesh.Identity";

    private readonly RequestDelegate _next;
    private readonly IIdentityProvider[] _providers;
    private readonly PeerAuthConfig _config;
    private readonly ILogger<PeerAuthMiddleware> _logger;

    public PeerAuthMiddleware(
        RequestDelegate next,
        IEnumerable<IIdentityProvider> providers,
        PeerAuthConfig config,
        ILogger<PeerAuthMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _providers = (providers ?? throw new ArgumentNullException(nameof(providers))).ToArray();
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only apply to mesh endpoints
        if (!IsMeshRequest(context))
        {
            await _next(context);
            return;
        }

        if (!_config.Enabled)
        {
            // Auth disabled — pass through with anonymous identity
            context.Items[IdentityItemKey] = IdentityResult.Anonymous();
            await _next(context);
            return;
        }

        var headers = ExtractHeaders(context.Request.Headers);

        IdentityResult? identity = null;
        Exception? lastError = null;

        foreach (var provider in _providers)
        {
            try
            {
                identity = await provider.AuthenticateAsync(headers, context.RequestAborted);
                if (identity is not null)
                {
                    break;
                }
            }
            catch (AuthenticationException ex)
            {
                lastError = ex;
                break; // First provider that had a credential validated (or rejected) wins
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Identity provider {Method} threw unexpectedly", provider.AuthMethod);
            }
        }

        if (identity is null)
        {
            var message = lastError?.Message
                ?? "Inter-agent auth required: provide bearer token, API key, or mTLS cert.";

            _logger.LogWarning("Auth rejected for {Path}: {Reason}", context.Request.Path, message);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = message,
                supportedMethods = _providers.Select(p => p.AuthMethod).ToArray()
            }));
            return;
        }

        // Enforce delegation depth limit
        if (identity.DelegationDepth > _config.MaxDelegationDepth)
        {
            _logger.LogWarning(
                "Auth rejected: delegation depth {Depth} exceeds max {Max}",
                identity.DelegationDepth, _config.MaxDelegationDepth);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = $"Delegation depth {identity.DelegationDepth} exceeds max {_config.MaxDelegationDepth}."
            }));
            return;
        }

        context.Items[IdentityItemKey] = identity;
        await _next(context);
    }

    private static bool IsMeshRequest(HttpContext context)
    {
        var path = context.Request.Path;
        // /api/mesh/* and /agent.manifest.json (well-known peer discovery)
        return path.StartsWithSegments("/api/mesh") ||
               path.Equals("/agent.manifest.json");
    }

    private static IReadOnlyDictionary<string, string> ExtractHeaders(IHeaderDictionary headers)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in headers)
        {
            // Join multi-value headers with comma (per HTTP spec)
            result[kvp.Key] = string.Join(",", kvp.Value);
        }

        return result;
    }
}

/// <summary>
///     Extension helpers for retrieving the verified identity from HttpContext.
/// </summary>
public static class HttpContextMeshIdentityExtensions
{
    /// <summary>Get the verified mesh identity from HttpContext.Items. Returns null if not authenticated.</summary>
    public static IdentityResult? GetMeshIdentity(this HttpContext context)
    {
        if (context.Items.TryGetValue(PeerAuthMiddleware.IdentityItemKey, out var value) &&
            value is IdentityResult identity)
        {
            return identity;
        }

        return null;
    }

    /// <summary>Get the mesh identity or return anonymous if not authenticated.</summary>
    public static IdentityResult RequireMeshIdentity(this HttpContext context)
    {
        return context.GetMeshIdentity() ?? IdentityResult.Anonymous();
    }
}
