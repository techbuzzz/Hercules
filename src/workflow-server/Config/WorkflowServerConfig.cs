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
}

/// <summary>Аутентификация по clientId/clientSecret (task_104, ADR-0008). Без JWT — упрощённая схема для MVP.</summary>
public sealed class AuthSection
{
    /// <summary>Если задан — используется как есть. Иначе — автогенерация на старте в <c>workflow-credentials.json</c>.</summary>
    public string? ClientId { get; set; }

    /// <summary>Если задан — используется как есть. Иначе — автогенерация.</summary>
    public string? ClientSecret { get; set; }
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
