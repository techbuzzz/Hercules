using Hercules.CheckIn;
using Hercules.Restart;
using Hercules.WebApi.Auth;
using Hercules.WebApi.Config;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     System-level endpoints для Studio-подключений (task_098, ADR-0005) и supervisor-протокола
///     рестарта агента (task_099). CheckIn: одновременно только один contribute-Studio может
///     занимать агента. System-Studio подключается параллельно (read-only monitor). TTL 60s
///     без heartbeat → автоматический checkout. Force-checkout — только system role.
///     Restart: оператор/system ставит флаг через <c>POST /api/system/restart</c>, supervisor
///     опрашивает <c>GET /api/system/restart-pending</c> и сам выполняет kill. Agent не
///     убивает себя сам.
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

        // --- task_099: System restart protocol (supervisor-managed) ---

        // POST /api/system/restart — запросить рестарт (system-only).
        // Возвращает 202 Accepted + snapshot состояния. Agent сам себя не убивает —
        // supervisor (Studio / systemd / watcher) опрашивает /restart-pending и сам делает kill.
        group.MapPost("/restart", (RestartRequestDto? req, RestartService svc, HttpContext http) =>
        {
            var actor = ResolveActor(http);
            var state = svc.RequestRestart(req?.Reason, actor);
            return Results.Accepted(value: new
            {
                pending = state.Pending,
                requestedAt = state.RequestedAt,
                reason = state.Reason,
                requestedBy = state.RequestedBy,
                message = "restart flag set; supervisor should poll /api/system/restart-pending and perform kill"
            });
        }).WithName("SystemRestart").RequireSystemRole();

        // GET /api/system/restart-pending — текущий статус (system-only).
        // Supervisor polling'ит этот endpoint пока pending=true, затем kill процесс.
        group.MapGet("/restart-pending", (RestartService svc) =>
        {
            var state = svc.GetStatus();
            return Results.Ok(new
            {
                pending = state.Pending,
                requestedAt = state.RequestedAt,
                reason = state.Reason,
                requestedBy = state.RequestedBy
            });
        }).WithName("SystemRestartPending").RequireSystemRole();

        // POST /api/system/restart/clear — очистить флаг (system-only).
        // Используется supervisor'ом/оператором чтобы отменить запланированный рестарт.
        group.MapPost("/restart/clear", (RestartClearRequestDto? req, RestartService svc, HttpContext http) =>
        {
            var actor = ResolveActor(http);
            var cleared = svc.ClearRestartRequest(actor);
            return cleared
                ? Results.NoContent()
                : Results.NotFound(new { error = "no restart flag to clear" });
        }).WithName("SystemRestartClear").RequireSystemRole();
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

    /// <summary>Резолвит actor для audit-лога из middleware или HttpContext.User.</summary>
    private static string? ResolveActor(HttpContext http)
    {
        if (http.Items.TryGetValue(ApiKeyMiddleware.EntryItemKey, out var entryObj)
            && entryObj is ApiKeyEntry entry
            && !string.IsNullOrWhiteSpace(entry.Description))
        {
            return entry.Description;
        }
        if (http.Items.TryGetValue(ApiKeyMiddleware.RoleItemKey, out var roleObj) && roleObj is ApiKeyRole role)
        {
            return role == ApiKeyRole.System ? "system" : "contribute";
        }
        return null;
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

/// <summary>DTO для <c>POST /api/system/restart</c> (task_099).</summary>
public sealed class RestartRequestDto
{
    /// <summary>Опциональная причина рестарта (operator-supplied), попадает в audit и status response.</summary>
    public string? Reason { get; set; }
}

/// <summary>DTO для <c>POST /api/system/restart/clear</c> (task_099, опциональный body).</summary>
public sealed class RestartClearRequestDto
{
    /// <summary>Опциональный actor для audit-лога (по умолчанию берётся из ApiKeyMiddleware).</summary>
    public string? By { get; set; }
}
