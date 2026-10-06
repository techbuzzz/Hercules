using Hercules.Mesh;
using Hercules.WebApi.Auth;
using Hercules.WebApi.Contracts;
using Hercules.WebApi.Config;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Studio session endpoints (ADR-0009).
///     <para>
///     Hercules Studio is a browser SPA. An API key cannot be stored safely in a
///     browser, so the client presents its key <em>once</em> here and receives a
///     short-lived opaque token it keeps only in tab memory. Every later request
///     carries <c>X-Session-Token</c> instead of <c>X-Api-Key</c>.
///     </para>
/// </summary>
public static class StudioController
{
    public static void MapStudio(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/studio").WithTags("Studio");

        // POST /api/studio/session — exchange an API key for a session token.
        // The key arrives via X-Api-Key and is authenticated by ApiKeyMiddleware.
        group.MapPost("/session", (
            HttpContext http,
            StudioSessionStore sessions,
            AgentManifestService manifestService,
            ILoggerFactory loggerFactory) =>
        {
            var role = ResolveRole(http);
            var manifest = manifestService.Current;

            var session = sessions.Create(
                role,
                string.IsNullOrWhiteSpace(manifest.AgentId) ? "hercules-agent" : manifest.AgentId,
                string.IsNullOrWhiteSpace(manifest.DisplayName) ? "Hercules Agent" : manifest.DisplayName,
                // Stage 6.3: bind the session to the key that minted it so deleting or
                // demoting that key can revoke this session instead of letting it keep
                // the old role until TTL.
                http.Items.TryGetValue(ApiKeyMiddleware.EntryItemKey, out var entry) && entry is ApiKeyEntry matched
                    ? ApiKeyStore.Fingerprint(matched.Key)
                    : null);

            // Deliberately does not echo the API key or log it.
            return Results.Ok(new StudioSessionResponseDto
            {
                Token = session.Token,
                Role = session.Role.ToString().ToLowerInvariant(),
                AgentId = session.AgentId,
                DisplayName = session.DisplayName,
                ExpiresAt = session.ExpiresAt,
                Capabilities = session.Capabilities,
                TtlSeconds = (int)sessions.Ttl.TotalSeconds,
            });
        })
        .WithName("CreateStudioSession")
        .WithSummary("Exchange an API key for a short-lived Studio session token")
        .Produces<StudioSessionResponseDto>(200);

        // GET /api/studio/session — whoami for the current token.
        group.MapGet("/session", (HttpContext http) =>
        {
            var session = StudioSessionStore.From(http);
            if (session is null) return Results.Problem("No active session", statusCode: 401);

            return Results.Ok(new StudioSessionResponseDto
            {
                Token = "",
                Role = session.Role.ToString().ToLowerInvariant(),
                AgentId = session.AgentId,
                DisplayName = session.DisplayName,
                ExpiresAt = session.ExpiresAt,
                Capabilities = session.Capabilities,
                TtlSeconds = 0,
            });
        })
        .WithName("GetStudioSession")
        .WithSummary("Describe the current Studio session")
        .Produces<StudioSessionResponseDto>(200);

        // DELETE /api/studio/session — drop every session (used on sign-out/drain).
        group.MapDelete("/session", (StudioSessionStore sessions) =>
        {
            var revoked = sessions.RevokeAll();
            return Results.Ok(new { revoked });
        })
        .WithName("RevokeStudioSessions")
        .WithSummary("Revoke all issued Studio sessions");
    }

    private static ApiKeyRole ResolveRole(HttpContext http)
    {
        if (http.Items.TryGetValue(ApiKeyMiddleware.RoleItemKey, out var roleObj) && roleObj is ApiKeyRole role)
        {
            return role;
        }
        // Authenticated but no role recorded — treat as the least privilege.
        return ApiKeyRole.Contribute;
    }
}