namespace Hercules.WebApi.Config;

/// <summary>Настройки Web API (секция "WebApi" в appsettings.json).</summary>
public sealed class WebApiConfig
{
    /// <summary>Ключ, ожидаемый в заголовке X-Api-Key. Если пуст — авторизация отключена (dev).</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Разрешённые CORS-источники (origin'ы фронтенда).</summary>
    public List<string> AllowedCorsOrigins { get; set; } = new();

    /// <summary>Максимальное количество запросов к /api/chat в минуту с одного IP. 0 = без лимита.</summary>
    public int ChatRateLimitPerMinute { get; set; } = 30;

    /// <summary>Максимальный размер тела запроса в байтах. 0 = без лимита.</summary>
    public int MaxRequestBodyBytes { get; set; } = 1_048_576;
}
