using Hercules.Tools.Grants;
using Microsoft.AspNetCore.Mvc;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     WebAPI endpoints для управления skill grants (least-privilege).
/// </summary>
[ApiController]
[Route("api/grants")]
public sealed class GrantsController : ControllerBase
{
    private readonly ISkillGrantService _grants;

    public GrantsController(ISkillGrantService grants)
    {
        _grants = grants ?? throw new ArgumentNullException(nameof(grants));
    }

    /// <summary>Получить все grants для навыка.</summary>
    [HttpGet("{skillId}")]
    public async Task<IActionResult> GetGrants(string skillId, [FromQuery] string? sessionId = null)
    {
        var grants = await _grants.GetGrantsAsync(skillId, sessionId);
        return Ok(grants.Select(g => new GrantDto
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
    }

    /// <summary>Выдать grant навыку.</summary>
    [HttpPost]
    public async Task<IActionResult> Grant([FromBody] GrantRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.SkillId) || string.IsNullOrWhiteSpace(dto.Permission))
        {
            return BadRequest("SkillId and Permission are required.");
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

        var grant = await _grants.GrantAsync(request);
        return Created($"/api/grants/{grant.SkillId}/{grant.Id}", new GrantDto
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
    }

    /// <summary>Удалить все grants навыка (или конкретный permission).</summary>
    [HttpDelete("{skillId}")]
    public async Task<IActionResult> Revoke(string skillId, [FromQuery] string? permission = null)
    {
        await _grants.RevokeAsync(skillId, permission);
        return NoContent();
    }

    /// <summary>Удалить конкретный grant по ID.</summary>
    [HttpDelete("by-id/{grantId}")]
    public async Task<IActionResult> RevokeById(string grantId)
    {
        await _grants.RevokeByIdAsync(grantId);
        return NoContent();
    }

    /// <summary>Проверить, есть ли у навыка конкретный permission.</summary>
    [HttpGet("{skillId}/check")]
    public async Task<IActionResult> Check(string skillId, [FromQuery] string permission, [FromQuery] string? sessionId = null)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            return BadRequest("Permission query parameter is required.");
        }

        var perm = Hercules.Tools.Policy.ToolPermissionExtensions.ParseFromString(permission);
        var result = await _grants.CheckPermissionAsync(skillId, perm, sessionId);
        return Ok(new GrantCheckResultDto
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
