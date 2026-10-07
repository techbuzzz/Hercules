using Hercules.WebApi.Auth;
using Hercules.WebApi.Config;
using Hercules.WebApi.Contracts;

namespace Hercules.WebApi.Controllers;

/// <summary>
/// API key and role administration (Stage 6.3, ADR-0004).
/// <para>
/// Keys live in <c>WebApi:ApiKeys</c> / <c>{DataRoot}/security/keys.json</c>, not in
/// <c>AppConfig</c>, so unlike quotas or LLM settings they cannot be edited through
/// <c>PATCH /api/config</c>. This controller is that write surface.
/// </para>
/// <para>
/// <b>Secrets.</b> The raw key is never returned by the list endpoint and never appears
/// in any response except the single <c>generatedKey</c> field of a create call. Studio
/// addresses keys by <see cref="ApiKeyStore.Fingerprint"/>, so an operator can manage
/// keys from a browser that has never seen one.
/// </para>
/// <para>
/// <b>Lockout guards.</b> <see cref="ApiKeyMiddleware"/> treats an empty key set as
/// "no authentication configured" and lets every request through. Both guard methods
/// below therefore reject any mutation that would leave zero keys or zero system keys.
/// </para>
/// </summary>
public static class AuthController
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        // GET /api/auth/keys — metadata only, never the key value.
        group.MapGet("/keys", (ApiKeyStore store, HttpContext http) =>
        {
            var entries = store.Active;
            return Results.Ok(new ApiKeysListResponseDto
            {
                Count = entries.Count,
                ContributeCount = entries.Count(e => e.Role == ApiKeyRole.Contribute),
                SystemCount = entries.Count(e => e.Role == ApiKeyRole.System),
                Keys = entries.Select(Summarize).ToList(),
                CurrentFingerprint = CurrentFingerprint(http)
            });
        }).WithName("ListApiKeys").RequireSystemRole().WithTags("Auth").Produces<ApiKeysListResponseDto>(200);

        // POST /api/auth/keys — add a key, generating one when none is supplied.
        group.MapPost("/keys", (CreateApiKeyRequestDto body, ApiKeyStore store, HttpContext http) =>
        {
            if (!TryParseRole(body.Role, out var role, out var roleError))
            {
                return Results.BadRequest(new { error = roleError });
            }

            var existing = store.Active.ToList();

            string? generated = null;
            string key;
            if (string.IsNullOrWhiteSpace(body.Key))
            {
                generated = GenerateKey(role);
                key = generated;
            }
            else
            {
                key = body.Key.Trim();
                if (existing.Any(k => string.Equals(k.Key, key, StringComparison.Ordinal)))
                {
                    return Results.Conflict(new { error = "A key with that value already exists" });
                }
            }

            var entry = new ApiKeyEntry
            {
                Key = key,
                Role = role,
                Description = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim()
            };

            var next = new List<ApiKeyEntry>(existing) { entry };
            if (!CanApply(next, out var guard))
            {
                return Results.BadRequest(new { error = guard });
            }

            try
            {
                store.SaveAndActivate(next);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Could not persist keys.json: {ex.Message}");
            }

            return Results.Ok(new CreatedApiKeyResponseDto
            {
                Key = Summarize(entry),
                GeneratedKey = generated
            });
        }).WithName("CreateApiKey").RequireSystemRole().WithTags("Auth").Produces<CreatedApiKeyResponseDto>(200);

        // PATCH /api/auth/keys/{fingerprint} — change role and/or description.
        group.MapPatch("/keys/{fingerprint}", (
            string fingerprint,
            UpdateApiKeyRequestDto body,
            ApiKeyStore store,
            StudioSessionStore sessions,
            HttpContext http) =>
        {
            var next = store.Active.ToList();
            var index = next.FindIndex(k =>
                string.Equals(ApiKeyStore.Fingerprint(k.Key), fingerprint, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return Results.NotFound(new { error = $"No key with fingerprint '{fingerprint}'" });

            var current = next[index];

            ApiKeyRole role = current.Role;
            if (body.Role is not null && !TryParseRole(body.Role, out role, out var roleError))
            {
                return Results.BadRequest(new { error = roleError });
            }

            var updated = new ApiKeyEntry
            {
                Key = current.Key,
                Role = role,
                Description = body.Description is null
                    ? current.Description
                    : (string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim())
            };
            next[index] = updated;

            if (!CanApply(next, out var guard))
            {
                return Results.BadRequest(new { error = guard });
            }

            try
            {
                store.SaveAndActivate(next);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Could not persist keys.json: {ex.Message}");
            }

            // A session carries the role captured at exchange time, so a demotion only
            // takes effect for existing clients once their sessions are dropped.
            var revoked = role == current.Role ? 0 : sessions.RevokeByFingerprint(fingerprint);

            return Results.Ok(new ApiKeyMutationResponseDto
            {
                Key = Summarize(updated),
                SystemCount = next.Count(k => k.Role == ApiKeyRole.System),
                RevokedSessions = revoked
            });
        }).WithName("UpdateApiKey").RequireSystemRole().WithTags("Auth").Produces<ApiKeyMutationResponseDto>(200);

        // DELETE /api/auth/keys/{fingerprint}
        group.MapDelete("/keys/{fingerprint}", (
            string fingerprint,
            ApiKeyStore store,
            StudioSessionStore sessions) =>
        {
            var next = store.Active
                .Where(k => !string.Equals(ApiKeyStore.Fingerprint(k.Key), fingerprint, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (next.Count == store.Active.Count)
            {
                return Results.NotFound(new { error = $"No key with fingerprint '{fingerprint}'" });
            }

            if (!CanApply(next, out var guard))
            {
                return Results.BadRequest(new { error = guard });
            }

            try
            {
                store.SaveAndActivate(next);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Could not persist keys.json: {ex.Message}");
            }

            return Results.Ok(new ApiKeyDeleteResponseDto
            {
                Fingerprint = fingerprint,
                SystemCount = next.Count(k => k.Role == ApiKeyRole.System),
                RevokedSessions = sessions.RevokeByFingerprint(fingerprint)
            });
        }).WithName("DeleteApiKey").RequireSystemRole().WithTags("Auth").Produces<ApiKeyDeleteResponseDto>(200);
    }

    /// <summary>
    /// Refuses any key set that would lock every operator out. The middleware treats an
    /// empty set as "authentication disabled", so allowing the last key to be deleted
    /// would silently expose the whole API.
    /// </summary>
    private static bool CanApply(List<ApiKeyEntry> next, out string error)
    {
        if (next.Count == 0)
        {
            error = "Refusing to remove the last API key: the middleware would fall back to unauthenticated access.";
            return false;
        }

        if (next.All(k => k.Role != ApiKeyRole.System))
        {
            error = "Refusing to leave zero system keys: no client would be able to administer the agent.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryParseRole(string? value, out ApiKeyRole role, out string error)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "contribute":
                role = ApiKeyRole.Contribute;
                error = string.Empty;
                return true;
            case "system":
                role = ApiKeyRole.System;
                error = string.Empty;
                return true;
            default:
                role = ApiKeyRole.Contribute;
                error = $"Unknown role '{value}'. Expected 'contribute' or 'system'.";
                return false;
        }
    }

    private static ApiKeySummaryDto Summarize(ApiKeyEntry entry) => new()
    {
        Fingerprint = ApiKeyStore.Fingerprint(entry.Key),
        Role = entry.Role == ApiKeyRole.System ? "system" : "contribute",
        Description = entry.Description,
        Label = Label(entry.Key)
    };

    /// <summary>
    /// Display hint from the key's known family prefix only. No part of the random token
    /// is exposed, so the label cannot leak key material.
    /// </summary>
    private static string Label(string key)
    {
        if (key.StartsWith("hc_sys_", StringComparison.Ordinal)) return "hc_sys_";
        if (key.StartsWith("hc_contrib_", StringComparison.Ordinal)) return "hc_contrib_";
        return "custom";
    }

    private static string GenerateKey(ApiKeyRole role) => ApiKeyStore.GenerateKey(role);

    /// <summary>
    /// Fingerprint of the key backing the current caller.
    /// <para>
    /// Read from the session when there is one, because Studio authenticates with
    /// <c>X-Session-Token</c> after the single key exchange — <see cref="ApiKeyMiddleware.EntryItemKey"/>
    /// is only populated on requests that still carry <c>X-Api-Key</c>.
    /// </para>
    /// </summary>
    private static string? CurrentFingerprint(HttpContext http)
    {
        var sessionFingerprint = StudioSessionStore.From(http)?.KeyFingerprint;
        if (!string.IsNullOrEmpty(sessionFingerprint)) return sessionFingerprint;

        return http.Items.TryGetValue(ApiKeyMiddleware.EntryItemKey, out var value) && value is ApiKeyEntry entry
            ? ApiKeyStore.Fingerprint(entry.Key)
            : null;
    }
}