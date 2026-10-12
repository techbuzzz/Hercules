using System.Security.Cryptography;
using System.Text;
using Hercules.WorkflowServer.Config;

namespace Hercules.WorkflowServer.Auth;

/// <summary>
///     Аутентификация по <c>X-Client-Id</c> + <c>X-Client-Secret</c> (task_104, ADR-0008).
///     Применяется ко всем <c>/api/*</c> маршрутам, кроме <c>/api/workflows/health</c>
///     (как в агенте: health-check всегда публичен).
///     При успешной проверке ставит <c>HttpContext.Items["ClientId"]</c>.
///     Если credentials не настроены (пустой ClientId) — режим открытого доступа (dev).
/// </summary>
public sealed class ClientAuthMiddleware(RequestDelegate next, ILogger<ClientAuthMiddleware> logger)
{
    public const string HeaderId = "X-Client-Id";
    public const string HeaderSecret = "X-Client-Secret";
    public const string ClientIdItemKey = "ClientId";

    private byte[]? _expectedIdBytes;
    private byte[]? _expectedSecretBytes;
    private string? _expectedId;

    /// <summary>
    ///     DI-конструктор: получает credentials из <see cref="ClientCredentialStore"/> (auto-generated или configured).
    /// </summary>
    public ClientAuthMiddleware(
        RequestDelegate next,
        ClientCredentialStore store,
        WorkflowServerConfig cfg,
        ILogger<ClientAuthMiddleware> logger)
        : this(next, logger)
    {
        var creds = store.LoadOrGenerate(cfg.Auth);
        if (!string.IsNullOrEmpty(creds.ClientId))
        {
            _expectedId = creds.ClientId;
            _expectedIdBytes = Encoding.UTF8.GetBytes(creds.ClientId);
            _expectedSecretBytes = Encoding.UTF8.GetBytes(creds.ClientSecret);
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;

        // CORS preflight, health, webhook trigger и не-API маршруты пропускаем без проверки.
        // Webhook авторизуется по токену в URL (task_104, ADR-0008) — см. token-валидацию в самом endpoint'е.
        if (HttpMethods.IsOptions(context.Request.Method)
            || !path.StartsWithSegments("/api")
            || path.StartsWithSegments("/api/workflows/health")
            || path.StartsWithSegments("/api/workflows/triggers/webhook"))
        {
            await next(context);
            return;
        }

        // Если credentials не настроены — режим открытого доступа (только для локали).
        if (_expectedIdBytes is null || _expectedSecretBytes is null)
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderId, out var providedId)
            || !context.Request.Headers.TryGetValue(HeaderSecret, out var providedSecret))
        {
            logger.LogWarning("Отклонён запрос {Path}: отсутствует {H1}/{H2}", path, HeaderId, HeaderSecret);
            await Reject(context, $"Требуются заголовки {HeaderId} и {HeaderSecret}.");
            return;
        }

        var idBytes = Encoding.UTF8.GetBytes(providedId.ToString());
        var secretBytes = Encoding.UTF8.GetBytes(providedSecret.ToString());

        if (!ConstantTimeEquals(idBytes, _expectedIdBytes) || !ConstantTimeEquals(secretBytes, _expectedSecretBytes))
        {
            logger.LogWarning("Отклонён запрос {Path}: неверные credentials", path);
            await Reject(context, "Неверные clientId/clientSecret.");
            return;
        }

        // Match — ставим ClientId в HttpContext.Items.
        context.Items[ClientIdItemKey] = _expectedId;
        await next(context);
    }

    private static async Task Reject(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = message });
    }

    private static bool ConstantTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
