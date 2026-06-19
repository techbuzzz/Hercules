using Hercules.WebApi.Config;

namespace Hercules.WebApi.Auth;

/// <summary>
/// Простая аутентификация по ключу в заголовке <c>X-Api-Key</c>.
/// Применяется только к маршрутам /api/*. Если ключ в конфиге не задан —
/// проверка пропускается (удобно для локального запуска).
/// </summary>
public sealed class ApiKeyMiddleware(RequestDelegate next, WebApiConfig cfg, ILogger<ApiKeyMiddleware> logger)
{
    public const string HeaderName = "X-Api-Key";

    private readonly string _expectedKey = cfg.ApiKey ?? "";

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;

        // CORS preflight, не-API маршруты и публичный health-check пропускаем без проверки.
        if (HttpMethods.IsOptions(context.Request.Method)
            || !path.StartsWithSegments("/api")
            || path.StartsWithSegments("/api/health"))
        {
            await next(context);
            return;
        }

        // Если ключ не настроен — режим открытого доступа (только для локали).
        if (string.IsNullOrEmpty(_expectedKey))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var provided) ||
            !string.Equals(provided.ToString(), _expectedKey, StringComparison.Ordinal))
        {
            logger.LogWarning("Отклонён запрос {Path}: неверный или отсутствующий {Header}", path, HeaderName);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Требуется корректный заголовок X-Api-Key." });
            return;
        }

        await next(context);
    }
}
