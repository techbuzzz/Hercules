using Hercules.WebApi.Config;

namespace Hercules.WebApi.Auth;

/// <summary>
///     Ограничение размера тела запроса для защиты от OOM.
///     Применяется ко всем маршрутам /api/.
/// </summary>
public sealed class RequestBodyLimitMiddleware(RequestDelegate next, WebApiConfig cfg)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;

        if (cfg.MaxRequestBodyBytes > 0 && path.StartsWithSegments("/api"))
        {
            if (context.Request.ContentLength > cfg.MaxRequestBodyBytes)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                await context.Response.WriteAsJsonAsync(new { error = "Request body too large." });
                return;
            }
        }

        await next(context);
    }
}
