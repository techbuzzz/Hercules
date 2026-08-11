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

    /// <summary>
    ///     Конфигурация Phase 2: семантическая маршрутизация и компонуемые навыки.
    /// </summary>
    public Phase2Config Phase2 { get; set; } = new();

    /// <summary>
    ///     Конфигурация Phase 3: inter-agent протокол, capability registry, discovery.
    /// </summary>
    public MeshConfig Mesh { get; set; } = new();
}

/// <summary>
///     Настройки Phase 2: семантическая маршрутизация, маркетплейс навыков, реестр инструментов.
/// </summary>
public sealed class Phase2Config
{
    /// <summary>Включить семантическую маршрутизацию (embedding-based). Если false — используется только keyword-matching.</summary>
    public bool SemanticRoutingEnabled { get; set; } = false;

    /// <summary>Embedding-провайдер: "stub-hash" | "yandexgpt" | "ollama". По умолчанию — stub (offline).</summary>
    public string EmbeddingProvider { get; set; } = "stub-hash";

    /// <summary>Минимальный порог cosine-similarity для семантического матча (0..1).</summary>
    public double SimilarityThreshold { get; set; } = 0.35;

    /// <summary>Использовать keyword-matching как fallback, если embedding < порога.</summary>
    public bool KeywordFallback { get; set; } = true;

    /// <summary>Папка для импортированных пакетов навыков (маркетплейс). По умолчанию — data/Skills/marketplace/.</summary>
    public string MarketplaceDir { get; set; } = "marketplace";

    /// <summary>Папка для деклараций инструментов (data/Tools/). По умолчанию — Tools.</summary>
    public string ToolsDir { get; set; } = "Tools";

    /// <summary>Папка для шаблонов агентов (data/Templates/). По умолчанию — Templates.</summary>
    public string TemplatesDir { get; set; } = "Templates";
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

    /// <summary>Phase 2 настройки (маркетплейс, инструменты, шаблоны). Если null — используются дефолты.</summary>
    public Phase2Config? Phase2 { get; set; }
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

/// <summary>
///     Конфигурация Phase 3: inter-agent mesh.
///     Discovery, capability registry, intent routing, transport.
/// </summary>
public sealed class MeshConfig
{
    /// <summary>Включить mesh-функциональность (manifest, registry, intent routing).</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>AgentId этого агента в mesh (e.g. "hercules-main").</summary>
    public string AgentId { get; set; } = "hercules-main";

    /// <summary>Human-readable имя для UI и registry.</summary>
    public string DisplayName { get; set; } = "Hercules";

    /// <summary>Описание агента для манифеста.</summary>
    public string Description { get; set; } = "Self-improving micro-agent";

    /// <summary>Endpoint этого агента для inter-agent вызовов (e.g. "http://localhost:5000").</summary>
    public string Endpoint { get; set; } = "http://localhost:5000";

    /// <summary>Путь к SQLite-файлу capability registry (относительно DataRoot или абсолютный).</summary>
    public string RegistryDb { get; set; } = "mesh_registry.db";

    /// <summary>Таймаут inter-agent вызовов (мс).</summary>
    public int IntentTimeoutMs { get; set; } = 30_000;

    /// <summary>
    ///     Минимальная уверенность локального навыка, при которой intent обрабатывается локально.
    ///     Если ниже — intent пересылается peer'у (если есть).
    /// </summary>
    public double LocalConfidenceThreshold { get; set; } = 0.5;

    /// <summary>
    ///     Статический список известных peer-агентов для discovery.
    ///     Формат: [{ "agentId": "...", "endpoint": "http://..." }, ...].
    ///     При запуске агенты из этого списка автоматически регистрируются в CapabilityRegistry.
    /// </summary>
    public List<MeshPeerConfig> Peers { get; set; } = new();
}

/// <summary>Конфигурация одного peer-агента для статического discovery.</summary>
public sealed class MeshPeerConfig
{
    public string AgentId { get; set; } = "";
    public string Endpoint { get; set; } = "";

    /// <summary>Опционально: сразу задать capabilities (если peer-агент не публикует манифест).</summary>
    public List<ManifestCapabilityConfig>? Capabilities { get; set; }
}

/// <summary>Capability в конфигурации peer-агента (упрощённая форма).</summary>
public sealed class ManifestCapabilityConfig
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("phrase_receivers")]
    public List<string> PhraseReceivers { get; set; } = new();
}
