namespace Hercules.Config;

/// <summary>
///     Корневая конфигурация приложения (маппится из appsettings.json).
/// </summary>
public sealed class AppConfig
{
    public LlmConfig Llm { get; set; } = new();
    public StorageConfig Storage { get; set; } = new();
    public AgentConfig Agent { get; set; } = new();
    public TelegramConfig Telegram { get; set; } = new();

    /// <summary>
    ///     Параметры sandbox для исполнения LLM-сгенерированного кода (Stage 2, v2).
    /// </summary>
    public CodeExecutionConfig CodeExecution { get; set; } = new();

    /// <summary>
    ///     Параметры HTTP-инструмента (Stage 3). Allow-list доменов, rate limits, timeouts.
    /// </summary>
    public HttpConfig Http { get; set; } = new();

    /// <summary>
    ///     Параметры MCP-клиента (Stage 3). Список MCP-серверов для подключения.
    /// </summary>
    public McpConfig Mcp { get; set; } = new();

    /// <summary>
    ///     Параметры A2A-клиента (Stage 3). Endpoints других агентов.
    /// </summary>
    public A2AConfig A2A { get; set; } = new();

    /// <summary>
    ///     Конфигурация именованных ролей LLM (multi-role routing, v2).
    ///     Ключ — имя роли ("main", "code_writer", "reflector", ...).
    ///     Значение — провайдер + модель + temperature.
    ///     Если секция пуста — все роли используют Llm.Provider (обратная совместимость).
    /// </summary>
    public Dictionary<string, RoleConfig> Roles { get; set; } = new();
}

/// <summary>
///     Параметры одной LLM-роли. Если Provider пуст — наследуется из Llm.Provider.
/// </summary>
public sealed class RoleConfig
{
    /// <summary>Имя провайдера: yandexgpt | ollama-cloud | ollama-local. Пусто → наследовать.</summary>
    public string Provider { get; set; } = "";

    /// <summary>Имя модели (если пусто — дефолт провайдера).</summary>
    public string Model { get; set; } = "";

    public float Temperature { get; set; } = 0.6f;

    public int MaxTokens { get; set; } = 2000;
}

/// <summary>
///     Параметры sandbox для исполнения LLM-кода (Stage 2).
///     Маппится из appsettings.json:CodeExecution.
/// </summary>
public sealed class CodeExecutionConfig
{
    public int CpuTimeoutSeconds { get; set; } = 30;
    public int MaxFileSizeMb { get; set; } = 10;
    public int MaxProcesses { get; set; } = 0;
    public int MaxOpenFiles { get; set; } = 1024;
    public long MaxVirtualMemoryMb { get; set; } = 0;
    public bool AllowNetwork { get; set; } = false;
    public int MaxCodeSizeKb { get; set; } = 100;
    public int SessionTtlSeconds { get; set; } = 3600;

    /// <summary>Override temp root. Пусто → использовать дефолт платформы.</summary>
    public string TempRoot { get; set; } = "";
}

/// <summary>HTTP-инструмент: безопасные исходящие запросы с allow-list.</summary>
public sealed class HttpConfig
{
    /// <summary>Allow-list доменов. ["*"] = все домены. ["api.github.com"] = только этот домен.</summary>
    public List<string> AllowedDomains { get; set; } = ["*"];

    /// <summary>Глобальный rate limit (запросов в минуту). 0 = без лимита.</summary>
    public int RateLimitPerMinute { get; set; } = 60;

    /// <summary>Timeout на запрос (секунды).</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Максимальный размер ответа (КБ). Превышение → truncated.</summary>
    public int MaxResponseSizeKb { get; set; } = 256;
}

/// <summary>MCP-клиент: подключение к Model Context Protocol серверам.</summary>
public sealed class McpConfig
{
    /// <summary>Список MCP-серверов для автоподключения при старте.</summary>
    public List<McpServerConfig> Servers { get; set; } = new();
}

/// <summary>Конфигурация одного MCP-сервера.</summary>
public sealed class McpServerConfig
{
    /// <summary>Имя сервера (для логов и namespace в tool registry).</summary>
    public string Name { get; set; } = "";

    /// <summary>"stdio" | "http".</summary>
    public string Transport { get; set; } = "stdio";

    /// <summary>Команда для stdio транспорта (например, "mcp-server-filesystem").</summary>
    public string? Command { get; set; }

    /// <summary>Аргументы команды.</summary>
    public List<string> Args { get; set; } = new();

    /// <summary>Endpoint URL для http транспорта.</summary>
    public string? Endpoint { get; set; }
}

/// <summary>A2A-клиент: Agent-to-Agent протокол (JSON-RPC 2.0).</summary>
public sealed class A2AConfig
{
    /// <summary>Список endpoints других агентов (имя → URL).</summary>
    public Dictionary<string, string> Endpoints { get; set; } = new();

    /// <summary>Таймаут на delegate-задачу (секунды).</summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
///     Конфигурация LLM-провайдеров. Поддерживает основной провайдер и список fallback.
/// </summary>
public sealed class LlmConfig
{
    /// <summary>Имя активного (основного) провайдера: yandexgpt | ollama-cloud | ollama-local.</summary>
    public string Provider { get; set; } = "yandexgpt";

    /// <summary>Порядок fallback-провайдеров, если основной недоступен.</summary>
    public List<string> Fallback { get; set; } = ["ollama-cloud", "ollama-local"];

    public YandexGptConfig YandexGpt { get; set; } = new();
    public OllamaConfig OllamaCloud { get; set; } = new();
    public OllamaConfig OllamaLocal { get; set; } = new();
}

/// <summary>Параметры YandexGPT (OpenAI-совместимый endpoint).</summary>
public sealed class YandexGptConfig
{
    /// <summary>OpenAI-совместимый endpoint YandexGPT.</summary>
    public string Endpoint { get; set; } = "https://llm.api.cloud.yandex.net/v1";

    /// <summary>IAM-токен или API-ключ сервисного аккаунта.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Идентификатор каталога Yandex Cloud (folder id).</summary>
    public string FolderId { get; set; } = "";

    /// <summary>Имя модели. Для Yandex используется URI gpt://{folderId}/{model}/latest.</summary>
    public string Model { get; set; } = "yandexgpt";

    public float Temperature { get; set; } = 0.6f;
    public int MaxTokens { get; set; } = 2000;
}

/// <summary>Параметры Ollama (Cloud или Local), OpenAI-совместимый интерфейс.</summary>
public sealed class OllamaConfig
{
    /// <summary>OpenAI-совместимый endpoint Ollama (например, http://localhost:11434/v1).</summary>
    public string Endpoint { get; set; } = "http://localhost:11434/v1";

    /// <summary>API-ключ (нужен для Ollama Cloud; для локального можно оставить пустым).</summary>
    public string ApiKey { get; set; } = "";

    public string Model { get; set; } = "llama3.1";
    public float Temperature { get; set; } = 0.6f;
    public int MaxTokens { get; set; } = 2000;
}

/// <summary>Пути к хранилищам данных.</summary>
public sealed class StorageConfig
{
    /// <summary>Корневая папка данных агента.</summary>
    public string DataRoot { get; set; } = "data";

    public string SkillsDir { get; set; } = "Skills";
    public string MemoryDir { get; set; } = "Memory";
    public string SqliteFile { get; set; } = "sessions.db";
}

/// <summary>Пороговые значения поведения агента.</summary>
public sealed class AgentConfig
{
    /// <summary>Системный промпт по умолчанию.</summary>
    public string SystemPrompt { get; set; } =
        "Ты — Hercules, самообучающийся ассистент. Отвечай кратко, по делу и на русском языке.";

    /// <summary>Сколько повторов однотипного запроса до предложения создать навык.</summary>
    public int SkillCreationThreshold { get; set; } = 3;

    /// <summary>Порог success_rate, ниже которого предлагается улучшение навыка.</summary>
    public double SkillImprovementThreshold { get; set; } = 0.6;

    /// <summary>Сколько последних использований учитывается при оценке навыка.</summary>
    public int SkillEvaluationWindow { get; set; } = 5;

    /// <summary>Запуск рефлексии каждые N команд (помимо завершения сессии).</summary>
    public int ReflectionEveryNCommands { get; set; } = 10;
}

/// <summary>Параметры Telegram-бота.</summary>
public sealed class TelegramConfig
{
    public bool Enabled { get; set; } = false;
    public string BotToken { get; set; } = "";
}
