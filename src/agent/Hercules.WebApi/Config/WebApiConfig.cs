namespace Hercules.WebApi.Config;

/// <summary>Настройки Web API (секция "WebApi" в appsettings.json).</summary>
public sealed class WebApiConfig
{
    /// <summary>Ключ, ожидаемый в заголовке X-Api-Key. Если пуст — авторизация отключена (dev).</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Разрешённые CORS-источники (origin'ы фронтенда).</summary>
    public List<string> AllowedCorsOrigins { get; set; } = new();
}
