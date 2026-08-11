using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hercules.Mesh;

/// <summary>
///     Манифест агента — публичное описание возможностей агента для других участников mesh.
///     Публикуется по well-known URL (GET /agent.manifest.json) или через capability registry.
///     Содержит: ID, версию, capabilities (навыки + триггеры), endpoint, auth, модели.
///     Спецификация: docs/AGENT-MESH-RU.md §3.
/// </summary>
public sealed class AgentManifest
{
    /// <summary>Стабильный идентификатор агента (e.g. "hercules-code-assistant").</summary>
    public string AgentId { get; set; } = "";

    /// <summary>Версия манифеста (semver, e.g. "1.2.0").</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>Human-readable имя для UI.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>Краткое описание — что делает агент, для чего специализирован.</summary>
    public string Description { get; set; } = "";

    /// <summary>Endpoint для inter-agent вызовов (intent transport).</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>Транспорт: "http" | "grpc" | "bus".</summary>
    public string Transport { get; set; } = "http";

    /// <summary>Способ аутентификации для inter-agent вызовов.</summary>
    public ManifestAuth Auth { get; set; } = new();

    /// <summary>Список capabilities (навыков), которые агент предоставляет mesh.</summary>
    public List<ManifestCapability> Capabilities { get; set; } = new();

    /// <summary>Модели LLM, используемые агентом (для routing решений).</summary>
    public ManifestModels Models { get; set; } = new();

    /// <summary>URL health-check endpoint.</summary>
    public string Health { get; set; } = "";

    /// <summary>Дополнительные метаданные (теги, location, owner — для фильтрации в registry).</summary>
    public Dictionary<string, string>? Tags { get; set; }

    /// <summary>Время генерации манифеста (ISO 8601 UTC).</summary>
    public string GeneratedAt { get; set; } = DateTime.UtcNow.ToString("o");
}

/// <summary>Способ аутентификации inter-agent вызовов.</summary>
public sealed class ManifestAuth
{
    /// <summary>Тип: "apikey" | "mtls" | "none".</summary>
    public string Type { get; set; } = "apikey";

    /// <summary>Имя header для API-key (e.g. "X-Api-Key").</summary>
    public string? Header { get; set; } = "X-Api-Key";
}

/// <summary>Одна capability агента: имя + триггеры + инструменты.</summary>
public sealed class ManifestCapability
{
    /// <summary>Уникальное имя capability (e.g. "csharp-refactor").</summary>
    public string Name { get; set; } = "";

    /// <summary>Описание — что делает capability.</summary>
    public string Description { get; set; } = "";

    /// <summary>Триггеры (phrase receivers) — по каким фразам/намерениям capability активируется.</summary>
    [JsonPropertyName("phrase_receivers")]
    public List<string> PhraseReceivers { get; set; } = new();

    /// <summary>Инструменты, необходимые для capability (e.g. ["execute_code", "http"]).</summary>
    public List<string>? Tools { get; set; }
}

/// <summary>Модели LLM, используемые агентом.</summary>
public sealed class ManifestModels
{
    /// <summary>Основной провайдер (e.g. "yandexgpt").</summary>
    public string Primary { get; set; } = "";

    /// <summary>Fallback-провайдеры.</summary>
    public List<string> Fallback { get; set; } = new();
}

/// <summary>
///     Сервис управления манифестом агента.
///     Генерирует манифест из текущих навыков и конфигурации,
///     публикует его в файл agent.manifest.json и в capability registry.
/// </summary>
public sealed class AgentManifestService
{
    private readonly AgentManifest _manifest;
    private readonly string _manifestPath;
    private readonly Func<List<ManifestCapability>> _capabilitiesProvider;

    /// <summary>
    ///     Создать сервис манифеста.
    /// </summary>
    /// <param name="agentId">Стабильный ID агента.</param>
    /// <param name="displayName">Human-readable имя.</param>
    /// <param name="description">Описание агента.</param>
    /// <param name="endpoint">Endpoint для inter-agent вызовов.</param>
    /// <param name="manifestDir">Папка для сохранения agent.manifest.json.</param>
    /// <param name="capabilitiesProvider">Функция, возвращающая текущие capabilities (навыки агента).</param>
    /// <param name="primaryModel">Основной LLM-провайдер.</param>
    /// <param name="fallbackModels">Fallback LLM-провайдеры.</param>
    /// <param name="healthEndpoint">URL health-check.</param>
    public AgentManifestService(
        string agentId,
        string displayName,
        string description,
        string endpoint,
        string manifestDir,
        Func<List<ManifestCapability>> capabilitiesProvider,
        string primaryModel = "",
        List<string>? fallbackModels = null,
        string healthEndpoint = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        _manifest = new AgentManifest
        {
            AgentId = agentId,
            DisplayName = displayName,
            Description = description,
            Endpoint = endpoint,
            Transport = "http",
            Auth = new ManifestAuth { Type = "apikey", Header = "X-Api-Key" },
            Models = new ManifestModels
            {
                Primary = primaryModel,
                Fallback = fallbackModels ?? new(),
            },
            Health = healthEndpoint,
        };
        _manifestPath = Path.Combine(manifestDir, "agent.manifest.json");
        Directory.CreateDirectory(manifestDir);
        _capabilitiesProvider = capabilitiesProvider ?? throw new ArgumentNullException(nameof(capabilitiesProvider));
    }

    /// <summary>Текущий манифест (с актуальными capabilities).</summary>
    public AgentManifest Current
    {
        get
        {
            _manifest.Capabilities = _capabilitiesProvider();
            _manifest.GeneratedAt = DateTime.UtcNow.ToString("o");
            return _manifest;
        }
    }

    /// <summary>Сгенерировать и сохранить манифест в файл agent.manifest.json.</summary>
    public AgentManifest Save()
    {
        var manifest = Current;
        var json = JsonSerializer.Serialize(manifest, JsonOpts);
        var temp = _manifestPath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, _manifestPath, overwrite: true);
        return manifest;
    }

    /// <summary>Путь к файлу манифеста.</summary>
    public string ManifestPath => _manifestPath;

    /// <summary>Обновить endpoint агента (например, при смене URL).</summary>
    public void UpdateEndpoint(string endpoint)
    {
        _manifest.Endpoint = endpoint;
        _manifest.Health = string.IsNullOrEmpty(endpoint) ? "" : endpoint.TrimEnd('/') + "/api/health";
    }

    /// <summary>Валидировать манифест. Возвращает список ошибок (пустой = OK).</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(_manifest.AgentId))
            errors.Add("agentId пуст.");
        if (string.IsNullOrWhiteSpace(_manifest.DisplayName))
            errors.Add("displayName пуст.");
        if (string.IsNullOrWhiteSpace(_manifest.Endpoint))
            errors.Add("endpoint пуст — другие агенты не смогут вызывать этот.");
        if (_manifest.Capabilities.Count == 0)
            errors.Add("Capabilities пуст — агент не декларирует ни одной capability.");
        foreach (var cap in _manifest.Capabilities)
        {
            if (string.IsNullOrWhiteSpace(cap.Name))
                errors.Add($"Capability без имени: {cap.Description}");
            if (cap.PhraseReceivers.Count == 0)
                errors.Add($"Capability '{cap.Name}' не имеет phrase_receivers — не сможет маршрутизироваться.");
        }
        return errors;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}