namespace Hercules.WebApi.Config;

/// <summary>Настройки Web API (секция "WebApi" в appsettings.json).</summary>
public sealed class WebApiConfig
{
    /// <summary>Ключ, ожидаемый в заголовке X-Api-Key. Если пуст — авторизация отключена (dev).</summary>
    public string ApiKey { get; set; } = "";

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
