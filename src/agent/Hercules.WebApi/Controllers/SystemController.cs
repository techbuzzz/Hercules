using Hercules.CheckIn;
using Hercules.WebApi.Auth;
using Hercules.WebApi.Config;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     System-level endpoints для Studio-подключений (task_098, ADR-0005).
///     CheckIn-протокол: одновременно только один contribute-Studio может занимать
///     агента. System-Studio подключается параллельно (read-only monitor). TTL 60s
///     без heartbeat → автоматический checkout. Force-checkout — только system role.
/// </summary>
public static class SystemController
{
    public static void MapSystem(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/system").WithTags("System");

        // POST /api/system/checkin — зарегистрировать Studio (contribute, эксклюзивно).
        // System role — no-op (parallel monitor, не модифицирует state).
        group.MapPost("/checkin", (CheckInRequestDto req, CheckInService svc, HttpContext http) =>
        {
            if (req is null || string.IsNullOrWhiteSpace(req.StudioId) || string.IsNullOrWhiteSpace(req.StudioName))
            {
                return Results.BadRequest(new { error = "studioId и studioName обязательны" });
            }
            var role = ResolveRole(http);
            var result = svc.CheckIn(req.StudioId, req.StudioName, role);
            if (!result.Success)
            {
                return Results.Conflict(new { error = result.Error, status = result.Status });
            }
            return Results.Ok(result.Status);
        }).WithName("SystemCheckIn");

        // POST /api/system/checkout — освободить агента.
        group.MapPost("/checkout", (CheckInRequestDto req, CheckInService svc, HttpContext http) =>
        {
            if (req is null || string.IsNullOrWhiteSpace(req.StudioId))
            {
                return Results.BadRequest(new { error = "studioId обязателен" });
            }
            var role = ResolveRole(http);
            var ok = svc.CheckOut(req.StudioId, role);
            return ok
                ? Results.NoContent()
                : Results.NotFound(new { error = "no active checkin for this studioId" });
        }).WithName("SystemCheckOut");

        // POST /api/system/checkin/heartbeat — обновить TTL.
        group.MapPost("/checkin/heartbeat", (CheckInRequestDto req, CheckInService svc, HttpContext http) =>
        {
            if (req is null || string.IsNullOrWhiteSpace(req.StudioId))
            {
                return Results.BadRequest(new { error = "studioId обязателен" });
            }
            var role = ResolveRole(http);
            var ok = svc.Heartbeat(req.StudioId, role);
            if (!ok)
            {
                return Results.NotFound(new { error = "no active checkin for this studioId" });
            }
            return Results.Ok(svc.GetStatus());
        }).WithName("SystemCheckInHeartbeat");

        // GET /api/system/checkin/status — текущий статус (any auth).
        group.MapGet("/checkin/status", (CheckInService svc) => Results.Ok(svc.GetStatus()))
            .WithName("SystemCheckInStatus");

        // POST /api/system/checkin/force — принудительное освобождение (system-only).
        group.MapPost("/checkin/force", (ForceCheckOutRequestDto? req, CheckInService svc) =>
        {
            var result = svc.ForceCheckOut(req?.By);
            return Results.Ok(result.Status);
        }).WithName("SystemCheckInForce").RequireSystemRole();
    }

    private static CheckInRole ResolveRole(HttpContext http)
    {
        if (http.Items.TryGetValue(ApiKeyMiddleware.RoleItemKey, out var roleObj) && roleObj is ApiKeyRole apiRole)
        {
            return apiRole == ApiKeyRole.System ? CheckInRole.System : CheckInRole.Contribute;
        }
        // Без middleware (например, в unit-тестах) — assume contribute как безопасный дефолт.
        return CheckInRole.Contribute;
    }
}

/// <summary>DTO для checkin / checkout / heartbeat.</summary>
public sealed class CheckInRequestDto
{
    public string? StudioId { get; set; }
    public string? StudioName { get; set; }
}

/// <summary>DTO для force-checkout (опциональный <c>by</c> — actor для audit-лога).</summary>
public sealed class ForceCheckOutRequestDto
{
    public string? By { get; set; }
}
