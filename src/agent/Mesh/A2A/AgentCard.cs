using System.Text.Json.Serialization;

namespace Hercules.Mesh.A2A;

/// <summary>
///     A2A (Agent-to-Agent) Agent Card — стандартный discovery-формат агента.
///     Spec: https://a2a-protocol.org/ (Google/Adobe LF AI Agent Protocol draft).
///     Публикуется по GET /agent-card.json и используется peer-агентами для discovery.
/// </summary>
public sealed class AgentCard
{
    /// <summary>
    ///     Уникальное имя агента (URL-safe, e.g. "hercules-code-assistant").
    ///     Должно совпадать с AgentManifest.AgentId.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    ///     Human-readable описание агента и его специализации.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>
    ///     URL базового endpoint агента (для JSON-RPC вызовов).
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    /// <summary>
    ///     Версия Agent Card (semver, e.g. "1.0.0").
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    ///     Информация о провайдере агента (организация, контакты).
    /// </summary>
    [JsonPropertyName("provider")]
    public A2AProvider? Provider { get; set; }

    /// <summary>
    ///     Supported capabilities этого агента.
    /// </summary>
    [JsonPropertyName("capabilities")]
    public AgentCardCapabilities Capabilities { get; set; } = new();

    /// <summary>
    ///     Authentication-схемы, поддерживаемые агентом.
    /// </summary>
    [JsonPropertyName("authentication")]
    public A2AAuthentication? Authentication { get; set; }

    /// <summary>
    ///     Список навыков (skills), которые агент предоставляет.
    /// </summary>
    [JsonPropertyName("skills")]
    public List<AgentCardSkill> Skills { get; set; } = new();

    /// <summary>
    ///     Default input modes, поддерживаемые агентом.
    /// </summary>
    [JsonPropertyName("defaultInputModes")]
    public List<string> DefaultInputModes { get; set; } = new() { "text", "json" };

    /// <summary>
    ///     Default output modes, поддерживаемые агентом.
    /// </summary>
    [JsonPropertyName("defaultOutputModes")]
    public List<string> DefaultOutputModes { get; set; } = new() { "text", "json" };

    /// <summary>
    ///     Дополнительные теги для фильтрации/категоризации агента.
    /// </summary>
    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    /// <summary>
    ///     Metadata: кто предоставил эту Agent Card.
    /// </summary>
    [JsonPropertyName("documentationUrl")]
    public string? DocumentationUrl { get; set; }

    /// <summary>
    ///     Дата генерации карты (ISO 8601 UTC).
    /// </summary>
    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = DateTime.UtcNow.ToString("o");
}

/// <summary>
///     Capabilities агента: что агент умеет (streaming, push notifications, etc.).
/// </summary>
public sealed class AgentCardCapabilities
{
    /// <summary>
    ///     Агент поддерживает streaming responses.
    /// </summary>
    [JsonPropertyName("streaming")]
    public bool Streaming { get; set; } = false;

    /// <summary>
    ///     Агент поддерживает push notifications (webhook/callback).
    /// </summary>
    [JsonPropertyName("pushNotifications")]
    public bool PushNotifications { get; set; } = false;

    /// <summary>
    ///     Агент отправляет state transition reports.
    /// </summary>
    [JsonPropertyName("stateTransitionReports")]
    public bool StateTransitionReports { get; set; } = false;

    /// <summary>
    ///     Агент поддерживает multipart responses.
    /// </summary>
    [JsonPropertyName("multipartResponses")]
    public bool MultipartResponses { get; set; } = false;
}

/// <summary>
///     Информация о провайдере агента.
/// </summary>
public sealed class A2AProvider
{
    /// <summary>
    ///     Организация или проект (e.g. "hercules-project", "acme-corp").
    /// </summary>
    [JsonPropertyName("organization")]
    public string Organization { get; set; } = "";

    /// <summary>
    ///     Homepage или документация провайдера.
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

/// <summary>
///     Authentication-схемы агента.
/// </summary>
public sealed class A2AAuthentication
{
    /// <summary>
    ///     Список поддерживаемых auth schemes (e.g. "bearer", "api-key", "basic").
    /// </summary>
    [JsonPropertyName("schemes")]
    public List<string> Schemes { get; set; } = new() { "bearer" };

    /// <summary>
    ///     Credentials-информация (например, где получить ключ).
    /// </summary>
    [JsonPropertyName("credentials")]
    public string? Credentials { get; set; }
}

/// <summary>
///     Skill, объявленный в Agent Card.
/// </summary>
public sealed class AgentCardSkill
{
    /// <summary>
    ///     Уникальный ID skill (должен совпадать с skill ID в Hercules).
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>
    ///     Human-readable имя skill.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    ///     Описание skill.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>
    ///     Теги для категоризации.
    /// </summary>
    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    /// <summary>
    ///     Input modes, поддерживаемые этим skill.
    /// </summary>
    [JsonPropertyName("inputModes")]
    public List<string>? InputModes { get; set; }

    /// <summary>
    ///     Output modes, поддерживаемые этим skill.
    /// </summary>
    [JsonPropertyName("outputModes")]
    public List<string>? OutputModes { get; set; }

    /// <summary>
    ///     Версия skill (semver).
    /// </summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }
}
