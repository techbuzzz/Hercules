using Hercules.WebApi.Config;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Hercules.WebApi.Auth;

/// <summary>
///     Endpoint-level фильтр (task_097, ADR-0004): требует роль <see cref="ApiKeyRole.System"/>
///     в <c>HttpContext.Items["ApiKeyRole"]</c>. Используется на system-only endpoints
///     (например <c>PUT /api/config</c>, <c>POST /api/system/restart</c>, force-checkout).
///     401 если ключ невалиден (отсутствует) — это случай, когда middleware
///     <see cref="ApiKeyMiddleware"/> не сработал (например, в unit-тестах без middleware).
///     В проде middleware всегда стоит перед endpoint, и этот случай не возникает.
/// </summary>
public sealed class RequireSystemRoleFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var role = ctx.HttpContext.Items.TryGetValue(ApiKeyMiddleware.RoleItemKey, out var roleObj)
            ? roleObj as ApiKeyRole?
            : null;

        if (role is null)
        {
            // Нет аутентификации (например, endpoint вызван в обход middleware в тестах).
            return Results.Unauthorized();
        }
        if (role != ApiKeyRole.System)
        {
            return Results.Json(
                new { error = "Этот endpoint требует system-роль API-ключа." },
                statusCode: StatusCodes.Status403Forbidden);
        }
        return await next(ctx);
    }
}

/// <summary>
///     Sugar-extension для минимальных API: применить system-role фильтр одной строкой.
///     Пример: <c>app.MapPut("/api/config", ...).RequireSystemRole()</c>.
/// </summary>
public static class SystemRoleEndpointExtensions
{
    public static RouteHandlerBuilder RequireSystemRole(this RouteHandlerBuilder builder)
    {
        return builder.AddEndpointFilter<RequireSystemRoleFilter>();
    }
}
