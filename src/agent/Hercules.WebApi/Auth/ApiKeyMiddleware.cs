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
public sealed class ApiKeyMiddleware
{
    private const string HeaderName = "X-Api-Key";
    public const string RoleItemKey = "ApiKeyRole";
    public const string EntryItemKey = "ApiKeyEntry";

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    // R5/R6: the resolved entries and their parallel byte table are both assigned in every
    // constructor. Previously the 3-argument constructor left _expectedKeyBytes null
    // (CS8618) and InvokeAsync then read that null as "no keys configured" — i.e. one
    // refactor away from silently disabling authentication on every /api route.
    private readonly ApiKeyEntry[] _entries;
    private readonly byte[]?[] _expectedKeyBytes;
    private readonly bool _unauthenticated;
    private readonly StudioSessionStore? _sessions;

    public ApiKeyMiddleware(RequestDelegate next, WebApiConfig cfg, ILogger<ApiKeyMiddleware> logger)
        : this(next, cfg, logger, keys: null)
    {
    }

    public ApiKeyMiddleware(RequestDelegate next, WebApiConfig cfg, ILogger<ApiKeyMiddleware> logger, IReadOnlyList<ApiKeyEntry>? keys)
        : this(next, cfg, logger, keys, sessions: null)
    {
    }

    /// <summary>DI path: keys come from <see cref="ApiKeyStore"/>, sessions from the registry.</summary>
    public ApiKeyMiddleware(
        RequestDelegate next,
        WebApiConfig cfg,
        ILogger<ApiKeyMiddleware> logger,
        ApiKeyStore store,
        StudioSessionStore sessions)
        : this(next, cfg, logger, store.LoadOrGenerate(cfg.ApiKeys), sessions)
    {
    }

    public ApiKeyMiddleware(
        RequestDelegate next,
        WebApiConfig cfg,
        ILogger<ApiKeyMiddleware> logger,
        IReadOnlyList<ApiKeyEntry>? keys,
        StudioSessionStore? sessions)
    {
        _next = next;
        _logger = logger;
        _sessions = sessions;

        var resolved = ResolveKeys(cfg, keys);
        _entries = resolved.Entries;
        _expectedKeyBytes = resolved.Bytes;
        _unauthenticated = resolved.Entries.Length == 0;

        if (_unauthenticated)
        {
            // Fail loudly. Open access is a deliberate local-dev mode, not something an
            // operator should discover from a 200 response in production.
            logger.LogError(
                "[ApiKey] No API keys configured (WebApi:ApiKeys empty and WebApi:ApiKey empty). " +
                "The API is UNAUTHENTICATED. Set WebApi:ApiKeys or WebApi:ApiKey before exposing this port.");
        }
        else
        {
            logger.LogInformation("[ApiKey] {Count} API key(s) loaded from configuration", _entries.Length);
        }
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
            await _next(context);
            return;
        }

        // Ни одного ключа не настроено — открытый доступ (только для локали; см. ctor-лог).
        if (_unauthenticated)
        {
            await _next(context);
            return;
        }

        // ADR-0009: браузерный клиент обменял API-ключ на короткоживущую сессию.
        // Сессия проверяется раньше ключа, но не вместо него — при отсутствии
        // заголовка сессии flow продолжает обычным путём проверки X-Api-Key.
        if (_sessions is not null &&
            context.Request.Headers.TryGetValue(StudioSessionStore.HeaderName, out var sessionToken) &&
            _sessions.TryValidate(sessionToken.ToString(), out var session) &&
            session is not null)
        {
            context.Items[RoleItemKey] = session.Role;
            context.Items[StudioSessionStore.ItemKey] = session;
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var provided))
        {
            _logger.LogWarning("Отклонён запрос {Path}: отсутствует {Header}", path, HeaderName);
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
            // R5: index into the RESOLVED entries, not cfg.ApiKeys. The table can be built
            // from store.LoadOrGenerate(...), whose length is not guaranteed to match
            // cfg.ApiKeys — the previous code would have thrown IndexOutOfRange there.
            context.Items[RoleItemKey] = _entries[i].Role;
            context.Items[EntryItemKey] = _entries[i];
            await _next(context);
            return;
        }

        _logger.LogWarning("Отклонён запрос {Path}: неверный {Header}", path, HeaderName);
        await Reject(context, "Требуется корректный заголовок X-Api-Key.");
    }

    private static async Task Reject(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = message });
    }

    /// <summary>
    ///     R5: resolves the effective key set AND builds the parallel byte table.
    ///     <para>
    ///     The previous implementation documented a backward-compat fallback to the legacy
    ///     <c>cfg.ApiKey</c> but returned <c>Array.Empty</c> instead — so any deployment
    ///     relying on the single legacy key had it silently rejected. That fallback is now
    ///     implemented here, for every construction path (including tests that pass an
    ///     explicit key list), not only for the DI path.
    ///     </para>
    /// </summary>
    private static (ApiKeyEntry[] Entries, byte[]?[] Bytes) ResolveKeys(WebApiConfig cfg, IReadOnlyList<ApiKeyEntry>? keys)
    {
        var source = keys ?? cfg.ApiKeys;

        if (source is null || source.Count == 0)
        {
            // Legacy single-key fallback (task_097): treat WebApi:ApiKey as a contribute key.
            if (!string.IsNullOrEmpty(cfg.ApiKey))
            {
                source =
                [
                    new ApiKeyEntry
                    {
                        Key = cfg.ApiKey,
                        Role = ApiKeyRole.Contribute,
                        Description = "legacy single key (WebApi:ApiKey)",
                    }
                ];
            }
        }

        if (source is null || source.Count == 0)
        {
            return ([], []);
        }

        var entries = source.ToArray();
        var table = new byte[]?[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            var key = entries[i]?.Key;
            table[i] = string.IsNullOrEmpty(key) ? null : Encoding.UTF8.GetBytes(key);
        }

        return (entries, table);
    }
}
