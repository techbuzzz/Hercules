namespace Hercules.WebApi.Config;

/// <summary>Настройки Web API (секция "WebApi" в appsettings.json).</summary>
public sealed class WebApiConfig
{
    /// <summary>Legacy: одиночный ключ, ожидаемый в заголовке X-Api-Key. Если пуст — авторизация отключена (dev).
    /// Используется как fallback (role=contribute) если <see cref="ApiKeys"/> пуст (task_097).</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Список API-ключей с ролями (task_097, ADR-0004).
    /// При наличии хотя бы одного элемента legacy <see cref="ApiKey"/> игнорируется.
    /// Backward compat: пустой список + legacy <see cref="ApiKey"/> → contribute role.</summary>
    public List<ApiKeyEntry> ApiKeys { get; set; } = new();

    /// <summary>Разрешённые CORS-источники (origin'ы фронтенда). Если пуст — fallback к localhost.</summary>
    public List<string> AllowedCorsOrigins { get; set; } = new();

    /// <summary>Допускать ли <c>AllowAnyOrigin()</c> по умолчанию (только dev!). По умолчанию false → используется localhost whitelist.</summary>
    public bool AllowAnyOrigin { get; set; } = false;

    /// <summary>Максимальное количество запросов к /api/chat в минуту с одного IP. 0 = без лимита (legacy).</summary>
    public int ChatRateLimitPerMinute { get; set; } = 30;

    /// <summary>Максимальный размер тела запроса в байтах. 0 = без лимита.</summary>
    public long MaxRequestBodyBytes { get; set; } = 1_048_576;

    /// <summary>Kestrel server tuning (task_081).</summary>
    public KestrelConfig Kestrel { get; set; } = new();

    /// <summary>Framework rate limiter (task_081) — fixed window и concurrency policies.</summary>
    public RateLimitingConfig RateLimiting { get; set; } = new();
}

/// <summary>Kestrel server limits (task_081).</summary>
public sealed class KestrelConfig
{
    /// <summary>Max concurrent open connections. Default: 1000 (sane for production).</summary>
    public int MaxConcurrentConnections { get; set; } = 1000;

    /// <summary>Max concurrent upgraded (WebSocket) connections. Default: 100.</summary>
    public int MaxConcurrentUpgradedConnections { get; set; } = 100;

    /// <summary>Max request body size in bytes. Default: 4 MiB.</summary>
    public long MaxRequestBodySize { get; set; } = 4_194_304;

    /// <summary>Keep-alive timeout. Default: 2 minutes.</summary>
    public int KeepAliveTimeoutSeconds { get; set; } = 120;

    /// <summary>Request headers timeout. Default: 30 seconds.</summary>
    public int RequestHeadersTimeoutSeconds { get; set; } = 30;
}

/// <summary>Rate limiting policies (task_081). Replaces the legacy <c>RateLimitMiddleware</c>.</summary>
public sealed class RateLimitingConfig
{
    /// <summary>Per-IP fixed-window quota for <c>/api/chat</c>. 0 = disable.</summary>
    public int ChatPerMinute { get; set; } = 30;

    /// <summary>Per-process concurrency cap for expensive endpoints (reflection, eval, SLO).</summary>
    public int ExpensiveConcurrency { get; set; } = 3;

    /// <summary>Length of the fixed window for chat (seconds). Default: 60.</summary>
    public int ChatWindowSeconds { get; set; } = 60;

    /// <summary>Length of the queue when the limiter is full (chat only). Default: 0 (reject).</summary>
    public int ChatQueueLimit { get; set; } = 0;
}

/// <summary>Роль API-ключа (task_097, ADR-0004). Определяет уровень привилегий.</summary>
public enum ApiKeyRole
{
    /// <summary>Оператор: ежедневная работа — чат, навыки, конфиг чтение/патч, mesh read, tools enable/disable, marketplace install.</summary>
    Contribute = 0,

    /// <summary>Админ: всё что у contribute + restart, lifecycle destructive, config PUT (full replace), MCP add/remove, quota changes, force checkout.</summary>
    System = 1
}

/// <summary>Один API-ключ с ролью (task_097, ADR-0004).</summary>
public sealed class ApiKeyEntry
{
    /// <summary>Значение ключа (открытый текст — как у legacy <c>WebApi:ApiKey</c>).</summary>
    public string Key { get; set; } = "";

    /// <summary>Роль: <c>contribute</c> или <c>system</c>.</summary>
    public ApiKeyRole Role { get; set; } = ApiKeyRole.Contribute;

    /// <summary>Опциональное описание (для отладки, в <c>keys.json</c>).</summary>
    public string? Description { get; set; }
}
