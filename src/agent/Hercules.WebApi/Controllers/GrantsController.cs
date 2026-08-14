using Hercules.Tools.Grants;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     WebAPI endpoints для управления skill grants (least-privilege).
/// </summary>
public static class GrantsController
{
    public static void MapGrants(this IEndpointRouteBuilder app)
    {
        // GET /api/grants/{skillId}?sessionId=xxx — все grants для навыка.
        app.MapGet("/api/grants/{skillId}", async (string skillId, string? sessionId, ISkillGrantService grants) =>
        {
            var results = await grants.GetGrantsAsync(skillId, sessionId);
            return Results.Ok(results.Select(g => new GrantDto
            {
                Id = g.Id,
                SkillId = g.SkillId,
                Permission = g.Permission.ToString(),
                Scope = g.Scope.ToString(),
                SessionId = g.SessionId,
                Grantor = g.Grantor,
                GrantedAt = g.GrantedAt,
                ExpiresAt = g.ExpiresAt,
                IsActive = g.IsActive
            }));
        }).WithName("GetGrants");

        // POST /api/grants — выдать grant навыку.
        app.MapPost("/api/grants", async (GrantRequestDto dto, ISkillGrantService grants) =>
        {
            if (string.IsNullOrWhiteSpace(dto.SkillId) || string.IsNullOrWhiteSpace(dto.Permission))
            {
                return Results.BadRequest("SkillId and Permission are required.");
            }

            var request = new GrantRequest
            {
                SkillId = dto.SkillId,
                Permission = dto.Permission,
                Scope = Enum.TryParse<GrantScope>(dto.Scope, true, out var s) ? s : GrantScope.Skill,
                SessionId = dto.SessionId,
                DurationMinutes = dto.DurationMinutes,
                Grantor = dto.Grantor ?? "user"
            };

            var grant = await grants.GrantAsync(request);
            return Results.Created($"/api/grants/{grant.SkillId}/{grant.Id}", new GrantDto
            {
                Id = grant.Id,
                SkillId = grant.SkillId,
                Permission = grant.Permission.ToString(),
                Scope = grant.Scope.ToString(),
                SessionId = grant.SessionId,
                Grantor = grant.Grantor,
                GrantedAt = grant.GrantedAt,
                ExpiresAt = grant.ExpiresAt,
                IsActive = grant.IsActive
            });
        }).WithName("GrantSkill");

        // DELETE /api/grants/{skillId}?permission=xxx — удалить все grants навыка (или конкретный permission).
        app.MapDelete("/api/grants/{skillId}", async (string skillId, string? permission, ISkillGrantService grants) =>
        {
            await grants.RevokeAsync(skillId, permission);
            return Results.NoContent();
        }).WithName("RevokeGrants");

        // DELETE /api/grants/by-id/{grantId} — удалить конкретный grant по ID.
        app.MapDelete("/api/grants/by-id/{grantId}", async (string grantId, ISkillGrantService grants) =>
        {
            await grants.RevokeByIdAsync(grantId);
            return Results.NoContent();
        }).WithName("RevokeGrantById");

        // GET /api/grants/{skillId}/check?permission=xxx&sessionId=xxx — проверить наличие permission.
        app.MapGet("/api/grants/{skillId}/check", async (string skillId, string permission, string? sessionId, ISkillGrantService grants) =>
        {
            if (string.IsNullOrWhiteSpace(permission))
            {
                return Results.BadRequest("Permission query parameter is required.");
            }

            var perm = Hercules.Tools.Policy.ToolPermissionExtensions.ParseFromString(permission);
            var result = await grants.CheckPermissionAsync(skillId, perm, sessionId);
            return Results.Ok(new GrantCheckResultDto
            {
                SkillId = skillId,
                Permission = permission,
                IsValid = result.IsValid,
                GrantedPermissions = result.GrantedPermissions.ToString(),
                MissingPermissions = result.MissingPermissions.ToString(),
                IsExpired = result.IsExpired,
                NotFound = result.NotFound,
                Scope = result.Scope.ToString(),
                Message = result.Message
            });
        }).WithName("CheckGrant");
    }
}

#pragma warning disable SA1202 // "Elements should appear in the correct order"
#pragma warning disable IDE0051 // "Remove unused private members"
public sealed class GrantRequestDto
{
    public string SkillId { get; set; } = "";
    public string Permission { get; set; } = "";
    public string Scope { get; set; } = "Skill";
    public string? SessionId { get; set; }
    public int DurationMinutes { get; set; } = 0;
    public string? Grantor { get; set; }
}

public sealed class GrantDto
{
    public string Id { get; set; } = "";
    public string SkillId { get; set; } = "";
    public string Permission { get; set; } = "";
    public string Scope { get; set; } = "";
    public string? SessionId { get; set; }
    public string Grantor { get; set; } = "";
    public DateTime GrantedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; }
}

public sealed class GrantCheckResultDto
{
    public string SkillId { get; set; } = "";
    public string Permission { get; set; } = "";
    public bool IsValid { get; set; }
    public string GrantedPermissions { get; set; } = "";
    public string MissingPermissions { get; set; } = "";
    public bool IsExpired { get; set; }
    public bool NotFound { get; set; }
    public string Scope { get; set; } = "";
    public string? Message { get; set; }
}
#pragma warning restore IDE0051
#pragma warning restore SA1202
