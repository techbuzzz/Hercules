using System.Security.Cryptography;
using System.Text;
using Hercules.WebApi.Config;
using Microsoft.AspNetCore.Http;

namespace Hercules.WebApi.Auth;

/// <summary>
///     Аутентификация по ключу в заголовке <c>X-Api-Key</c>.
///     Применяется только к маршрутам /api/*. Если ключ в конфиге не задан —
///     проверка пропускается (удобно для локального запуска).
///     После успешной проверки ставит <c>HttpContext.Items["ApiKeyRole"]</c> и
///     <c>HttpContext.Items["ApiKeyEntry"]</c> для downstream-фильтров
///     (см. <see cref="RequireSystemRoleFilter"/>, task_097, ADR-0004).
/// </summary>
public sealed class ApiKeyMiddleware(RequestDelegate next, WebApiConfig cfg, ILogger<ApiKeyMiddleware> logger)
{
    private const string HeaderName = "X-Api-Key";
    public const string RoleItemKey = "ApiKeyRole";
    public const string EntryItemKey = "ApiKeyEntry";

    private readonly byte[]?[] _expectedKeyBytes;

    public ApiKeyMiddleware(RequestDelegate next, WebApiConfig cfg, ILogger<ApiKeyMiddleware> logger, IReadOnlyList<ApiKeyEntry>? keys)
        : this(next, cfg, logger)
    {
        _expectedKeyBytes = BuildKeyTable(keys);
    }

    public ApiKeyMiddleware(RequestDelegate next, WebApiConfig cfg, ILogger<ApiKeyMiddleware> logger, ApiKeyStore store)
        : this(next, cfg, logger, store.LoadOrGenerate(cfg.ApiKeys))
    {
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;

        // CORS preflight, не-API маршруты и публичный health-check пропускаем без проверки.
        if (HttpMethods.IsOptions(context.Request.Method) || !path.StartsWithSegments("/api") || path.StartsWithSegments("/api/health"))
        {
            await next(context);
            return;
        }

        // Если ни один ключ не настроен — режим открытого доступа (только для локали).
        if (_expectedKeyBytes is null || _expectedKeyBytes.Length == 0 || _expectedKeyBytes.All(b => b is null || b.Length == 0))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var provided))
        {
            logger.LogWarning("Отклонён запрос {Path}: отсутствует {Header}", path, HeaderName);
            await Reject(context, "Требуется корректный заголовок X-Api-Key.");
            return;
        }

        var providedBytes = Encoding.UTF8.GetBytes(provided.ToString());
        for (var i = 0; i < _expectedKeyBytes.Length; i++)
        {
            var expected = _expectedKeyBytes[i];
            if (expected is null || expected.Length == 0) continue;
            if (providedBytes.Length != expected.Length) continue;
            if (!CryptographicOperations.FixedTimeEquals(providedBytes, expected)) continue;

            // Match — ставим role + entry в HttpContext.Items.
            var role = cfg.ApiKeys[i].Role;
            context.Items[RoleItemKey] = role;
            context.Items[EntryItemKey] = cfg.ApiKeys[i];
            await next(context);
            return;
        }

        logger.LogWarning("Отклонён запрос {Path}: неверный {Header}", path, HeaderName);
        await Reject(context, "Требуется корректный заголовок X-Api-Key.");
    }

    private static async Task Reject(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = message });
    }

    /// <summary>
    ///     Строит параллельную таблицу байт-ключей для constant-time сравнения.
    ///     Если <paramref name="keys"/> пуст, выполняет backward-compat fallback на legacy
    ///     <c>cfg.ApiKey</c> (role=contribute).
    /// </summary>
    private static byte[]?[] BuildKeyTable(IReadOnlyList<ApiKeyEntry>? keys)
    {
        if (keys is not null && keys.Count > 0)
        {
            var table = new byte[]?[keys.Count];
            for (var i = 0; i < keys.Count; i++)
            {
                table[i] = string.IsNullOrEmpty(keys[i].Key)
                    ? null
                    : Encoding.UTF8.GetBytes(keys[i].Key);
            }
            return table;
        }
        return Array.Empty<byte[]?>();
    }
}
