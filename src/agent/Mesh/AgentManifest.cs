using System.Text.Encodings.Web;
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

    /// <summary>Версии протокола inter-agent communication, поддерживаемые агентом (e.g. ["1.0", "2.0"]).</summary>
    [JsonPropertyName("supported_protocol_versions")]
    public List<string> SupportedProtocolVersions { get; set; } = new() { "1.0" };

    /// <summary>Лимиты ресурсов агента (token budget, max concurrent requests, etc.).</summary>
    public ManifestResourceLimits? ResourceLimits { get; set; }

    /// <summary>Trust metadata: identity claims, trust level, verified_by.</summary>
    public ManifestTrustMetadata? TrustMetadata { get; set; }

    /// <summary>Полный список навыков агента (расширенная информация, не только capabilities).</summary>
    public List<ManifestSkillEntry> Skills { get; set; } = new();

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
    /// <summary>Основной прровайдер (e.g. "yandexgpt").</summary>
    public string Primary { get; set; } = "";

    /// <summary>Fallback-провайдеры.</summary>
    public List<string> Fallback { get; set; } = new();
}

/// <summary>
/// Лимиты ресурсов агента.
/// </summary>
public sealed class ManifestResourceLimits
{
    /// <summary>Максимум токенов на один запрос.</summary>
    [JsonPropertyName("max_tokens_per_request")]
    public int MaxTokensPerRequest { get; set; }

    /// <summary>Максимум одновременных запросов.</summary>
    [JsonPropertyName("max_concurrent_requests")]
    public int MaxConcurrentRequests { get; set; }

    /// <summary>Максимум вызовов инструментов за один запрос.</summary>
    [JsonPropertyName("max_tool_calls_per_request")]
    public int MaxToolCallsPerRequest { get; set; }

    /// <summary>Максимум wall-clock времени на запрос (секунды).</summary>
    [JsonPropertyName("max_wall_clock_seconds_per_request")]
    public int MaxWallClockSecondsPerRequest { get; set; }

    /// <summary>Максимум стоимости в USD за день.</summary>
    [JsonPropertyName("max_cost_per_day_usd")]
    public decimal MaxCostPerDayUsd { get; set; }

    /// <summary>Максимум токенов за день.</summary>
    [JsonPropertyName("max_tokens_per_day")]
    public int MaxTokensPerDay { get; set; }
}

/// <summary>
/// Trust metadata агента.
/// </summary>
public sealed class ManifestTrustMetadata
{
    /// <summary>Уровень доверия: "trusted" | "verified" | "unverified".</summary>
    public string Level { get; set; } = "unverified";

    /// <summary>Провайдер identity (e.g. "self-signed", "custom-ca").</summary>
    public string IdentityProvider { get; set; } = "self-signed";

    /// <summary>Список identity claims (subject, issuer, audience).</summary>
    public Dictionary<string, string> IdentityClaims { get; set; } = new();

    /// <summary>Кем верифицирован агент.</summary>
    public string? VerifiedBy { get; set; }

    /// <summary>Дата верификации (ISO 8601 UTC).</summary>
    public string? VerifiedAt { get; set; }
}

/// <summary>
/// Расширенная запись о навыке в манифесте.
/// </summary>
public sealed class ManifestSkillEntry
{
    /// <summary>Уникальный ID навыка.</summary>
    public string Id { get; set; } = "";

    /// <summary>Имя навыка.</summary>
    public string Name { get; set; } = "";

    /// <summary>Описание.</summary>
    public string Description { get; set; } = "";

    /// <summary>Версия навыка (semver).</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>Уровень риска навыка.</summary>
    [JsonPropertyName("risk_level")]
    public string RiskLevel { get; set; } = "low";

    /// <summary>Инструменты, требуемые навыком.</summary>
    public List<string> Tools { get; set; } = new();

    /// <summary>Триггеры (phrase receivers).</summary>
    [JsonPropertyName("phrase_receivers")]
    public List<string> PhraseReceivers { get; set; } = new();

    /// <summary>Дата создания навыка (ISO 8601).</summary>
    public string? CreatedAt { get; set; }

    /// <summary>Дата последнего обновления (ISO 8601).</summary>
    public string? UpdatedAt { get; set; }
}

/// <summary>
///     Сервис управления манифестом агента.
///     Генерирует манифест из текущих навыков и конфигурации,
///     публикует его в файл agent.manifest.json и в capability registry.
/// </summary>
public sealed class AgentManifestService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly Func<List<ManifestCapability>> _capabilitiesProvider;
    private readonly Func<List<ManifestSkillEntry>>? _skillsProvider;
    private readonly List<string> _supportedProtocolVersions;
    private readonly ManifestResourceLimits? _resourceLimits;
    private readonly ManifestTrustMetadata? _trustMetadata;
    private readonly AgentManifest _manifest;

    /// <summary>
    ///     Создать сервис манифеста (backward-compatible constructor).
    /// </summary>
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
        : this(agentId, displayName, description, endpoint, manifestDir,
            capabilitiesProvider, null, primaryModel, fallbackModels, healthEndpoint,
            null, null, null)
    {
    }

    /// <summary>
    ///     Создать сервис манифеста с расширенными полями (protocol versions, resource limits, trust).
    /// </summary>
    public AgentManifestService(
        string agentId,
        string displayName,
        string description,
        string endpoint,
        string manifestDir,
        Func<List<ManifestCapability>> capabilitiesProvider,
        Func<List<ManifestSkillEntry>>? skillsProvider,
        string primaryModel,
        List<string>? fallbackModels,
        string healthEndpoint,
        List<string>? supportedProtocolVersions,
        ManifestResourceLimits? resourceLimits,
        ManifestTrustMetadata? trustMetadata)
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
                Primary = primaryModel ?? "",
                Fallback = fallbackModels ?? new List<string>()
            },
            Health = healthEndpoint ?? "",
            SupportedProtocolVersions = supportedProtocolVersions ?? new List<string> { "1.0" },
            ResourceLimits = resourceLimits,
            TrustMetadata = trustMetadata
        };

        ManifestPath = Path.Combine(manifestDir, Hercules.BuiltIn.AgentManifestFileName);
        Directory.CreateDirectory(manifestDir);
        _capabilitiesProvider = capabilitiesProvider ?? throw new ArgumentNullException(nameof(capabilitiesProvider));
        _skillsProvider = skillsProvider;
        _supportedProtocolVersions = supportedProtocolVersions ?? new List<string> { "1.0" };
        _resourceLimits = resourceLimits;
        _trustMetadata = trustMetadata;
    }

    /// <summary>Текущий манифест (с актуальными capabilities, skills и полями из конфига).</summary>
    public AgentManifest Current
    {
        get
        {
            _manifest.Capabilities = _capabilitiesProvider();
            _manifest.Skills = _skillsProvider?.Invoke() ?? new List<ManifestSkillEntry>();

            // Only override static fields if they were explicitly set via the extended constructor.
            // The basic constructor chain sets _Xxx fields to null (from null parameters in this(...)).
            // We preserve the values already set on _manifest by the constructor chain.
            if (_supportedProtocolVersions is not null)
            {
                _manifest.SupportedProtocolVersions = _supportedProtocolVersions;
            }
            else
            {
                // Basic constructor: preserve what the extended constructor chain set on _manifest
            }

            if (_resourceLimits is not null)
            {
                _manifest.ResourceLimits = _resourceLimits;
            }
            // else: preserve _manifest.ResourceLimits (already set by constructor chain)

            if (_trustMetadata is not null)
            {
                _manifest.TrustMetadata = _trustMetadata;
            }
            // else: preserve _manifest.TrustMetadata (already set by constructor chain)

            _manifest.GeneratedAt = DateTime.UtcNow.ToString("o");
            return _manifest;
        }
    }

    /// <summary>Путь к файлу манифеста.</summary>
    public string ManifestPath { get; }

    /// <summary>Сгенерировать и сохранить манифест в файл agent.manifest.json.</summary>
    public AgentManifest Save()
    {
        AgentManifest manifest = Current;
        var json = JsonSerializer.Serialize(manifest, JsonOpts);
        var temp = ManifestPath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, ManifestPath, true);
        return manifest;
    }

    /// <summary>Асинхронно сохранить манифест в файл.</summary>
    public async Task<AgentManifest> SaveAsync(CancellationToken ct = default)
    {
        AgentManifest manifest = Current;
        var json = JsonSerializer.Serialize(manifest, JsonOpts);
        var temp = ManifestPath + ".tmp";
        await File.WriteAllTextAsync(temp, json, ct);
        File.Move(temp, ManifestPath, true);
        return manifest;
    }

    /// <summary>Обновить endpoint агента (например, при смене URL).</summary>
    public void UpdateEndpoint(string endpoint)
    {
        _manifest.Endpoint = endpoint;
        _manifest.Health = string.IsNullOrEmpty(endpoint)
            ? ""
            : endpoint.TrimEnd('/') + "/api/health";
    }

    /// <summary>Валидировать манифест. Возвращает список ошибок (пустой = OK).</summary>
    public List<string> Validate()
    {
        // Обновляем capabilities и skills из провайдеров перед валидацией
        _manifest.Capabilities = _capabilitiesProvider();
        _manifest.Skills = _skillsProvider?.Invoke() ?? new List<ManifestSkillEntry>();

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(_manifest.AgentId))
        {
            errors.Add("agentId пуст.");
        }

        if (string.IsNullOrWhiteSpace(_manifest.DisplayName))
        {
            errors.Add("displayName пуст.");
        }

        if (string.IsNullOrWhiteSpace(_manifest.Endpoint))
        {
            errors.Add("endpoint пуст — другие агенты не смогут вызывать этот.");
        }

        if (_manifest.SupportedProtocolVersions.Count == 0)
        {
            errors.Add("supportedProtocolVersions пуст — peer'ы не узнают поддерживаемые версии протокола.");
        }

        if (_manifest.Capabilities.Count == 0)
        {
            errors.Add("Capabilities пуст — агент не декларирует ни одной capability.");
        }

        foreach (ManifestCapability cap in _manifest.Capabilities)
        {
            if (string.IsNullOrWhiteSpace(cap.Name))
            {
                errors.Add($"Capability без имени: {cap.Description}");
            }

            if (cap.PhraseReceivers.Count == 0)
            {
                errors.Add($"Capability '{cap.Name}' не имеет phrase_receivers — не сможет маршрутизироваться.");
            }
        }

        return errors;
    }
}
