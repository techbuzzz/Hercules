namespace Hercules.WorkflowServer.Config;

/// <summary>
///     Настройки hercules-workflow-server (task_104, ADR-0008).
///     Секция <c>WorkflowServer</c> в appsettings.json.
/// </summary>
public sealed class WorkflowServerConfig
{
    /// <summary>HTTPS-порт (по умолчанию 8430 — диапазон 8400-8500, см. ADR-0003).</summary>
    public int Port { get; set; } = 8430;

    /// <summary>Корень данных для credentials, SQLite, логов. По умолчанию <c>data</c> (рядом с агентом).</summary>
    public string DataRoot { get; set; } = "data";

    /// <summary>Настройки аутентификации (clientId/clientSecret).</summary>
    public AuthSection Auth { get; set; } = new();

    /// <summary>Список агентов, с которыми workflow-server взаимодействует через <c>/api/mesh/intent</c>.</summary>
    public List<AgentEndpoint> Agents { get; set; } = new();

    /// <summary>Настройки webhook-триггеров (R8/R10).</summary>
    public WebhookSection Webhooks { get; set; } = new();

    /// <summary>Настройки CORS (R16).</summary>
    public CorsSection Cors { get; set; } = new();
}

/// <summary>
///     R8/R10: конфигурация webhook-триггеров.
/// </summary>
/// <remarks>
///     Раньше endpoint принимал ЛЮБОЙ непустой token, был помечен <c>AllowAnonymous</c>
///     и дополнительно выведен из-под <c>ClientAuthMiddleware</c>. В сочетании с
///     неограниченной очередью это давало неаутентифицированный remote DoS через
///     исчерпание памяти. Теперь токены — явный allow-list, и при пустом списке
///     endpoint отвечает 403, а не принимает запрос.
/// </remarks>
public sealed class WebhookSection
{
    /// <summary>
    ///     Разрешённые токены webhook. Пустой список = webhook отключён (fail-closed).
    /// </summary>
    public List<string> AllowedTokens { get; set; } = new();

    /// <summary>Ёмкость кольцевого буфера последних webhook-событий (для отладки).</summary>
    public int EventBufferCapacity { get; set; } = 100;

    /// <summary>Максимальный размер тела webhook-запроса в байтах (защита от DoS).</summary>
    public int MaxRequestBodyBytes { get; set; } = 256 * 1024;
}

/// <summary>Аутентификация по clientId/clientSecret (task_104, ADR-0008). Без JWT — упрощённая схема для MVP.</summary>
public sealed class AuthSection
{
    /// <summary>Если задан — используется как есть. Иначе — автогенерация на старте в <c>workflow-credentials.json</c>.</summary>
    public string? ClientId { get; set; }

    /// <summary>Если задан — используется как есть. Иначе — автогенерация.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    ///     R16: запросов в минуту с одного IP на защищённые <c>/api/*</c> маршруты.
    ///     Раньше ограничителя не было вообще — credential-проверку можно было
    ///     перебирать без ограничений.
    /// </summary>
    public int RateLimitPerMinute { get; set; } = 60;
}

/// <summary>R16: CORS. Пустой <see cref="AllowedOrigins"/> = браузерные клиенты запрещены (fail-closed).</summary>
public sealed class CorsSection
{
    /// <summary>Разрешённые origins. По умолчанию пусто — cross-origin вызовы запрещены.</summary>
    public List<string> AllowedOrigins { get; set; } = new();
}

/// <summary>Описание удалённого агента (mesh peer) для вызова <c>POST /api/mesh/intent</c>.</summary>
public sealed class AgentEndpoint
{
    /// <summary>Стабильный идентификатор (используется workflow-графами для ссылки на агента).</summary>
    public string AgentId { get; set; } = "";

    /// <summary>Базовый URL (например, <c>http://localhost:8421</c>).</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>API-ключ, который workflow-server предъявляет агенту при вызовах.</summary>
    public string ApiKey { get; set; } = "";
}
