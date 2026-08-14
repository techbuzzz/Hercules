using Hercules.Tools.Policy;

namespace Hercules.Tools.Registry;

/// <summary>
///     Категория инструмента по типу выполняемой работы.
/// </summary>
public enum ToolCategory
{
    /// <summary>HTTP/网络 запросы.</summary>
    Http = 0,

    /// <summary>Файловая система.</summary>
    FileSystem = 1,

    /// <summary>Shell/командная строка.</summary>
    Shell = 2,

    /// <summary>База данных.</summary>
    Database = 3,

    /// <summary>GPIO/аппаратные порты.</summary>
    Gpio = 4,

    /// <summary>MQTT/IoT messaging.</summary>
    Mqtt = 5,

    /// <summary>MCP (Model Context Protocol) внешние инструменты.</summary>
    Mcp = 6,

    /// <summary>Code execution (WASM, sandbox).</summary>
    CodeExecution = 7,

    /// <summary>Внутренние .NET инструменты.</summary>
    Internal = 8,

    /// <summary>Неизвестная категория.</summary>
    Unknown = 9,
}

/// <summary>
///     Health status одного tool.
/// </summary>
public enum ToolHealthStatus
{
    /// <summary>Health check ещё не выполнялся.</summary>
    Unknown = 0,

    /// <summary>Tool работает корректно.</summary>
    Healthy = 1,

    /// <summary>Tool не прошёл health check.</summary>
    Unhealthy = 2,

    /// <summary>Tool явно отключён.</summary>
    Disabled = 3,
}

/// <summary>
///     Текущее состояние health для одного tool.
/// </summary>
public sealed record ToolHealthState(
    ToolHealthStatus Status,
    DateTime LastCheckedAt,
    string? LastError,
    int ConsecutiveFailures)
{
    /// <summary>Создать начальное состояние (Unknown).</summary>
    public static ToolHealthState Initial => new(
        ToolHealthStatus.Unknown,
        DateTime.MinValue,
        null,
        0);
}

/// <summary>
///     Per-tool лимиты (runtime overrides).
/// </summary>
public sealed class ToolLimits
{
    public int? MaxCallsPerMinute { get; set; }
    public int? TimeoutSeconds { get; set; }
    public int? MaxRetries { get; set; }
    public int? MaxConcurrent { get; set; }
}

/// <summary>
///     Один entry в реестре инструментов.
/// </summary>
public sealed class ToolRegistryEntry
{
    /// <summary>Имя tool (совпадает с ITool.Name).</summary>
    public required string Name { get; init; }

    /// <summary>Категория инструмента.</summary>
    public ToolCategory Category { get; set; } = ToolCategory.Unknown;

    /// <summary>Краткое описание для UI/API.</summary>
    public string Description { get; init; } = "";

    /// <summary>Policy descriptor (side-effect, permissions, etc.).</summary>
    public ToolDescriptor? Descriptor { get; set; }

    /// <summary>Текущий health state.</summary>
    public ToolHealthState HealthState { get; set; } = ToolHealthState.Initial;

    /// <summary>Разрешён ли tool к выполнению (включая allow/deny и явно).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Runtime overrides для лимитов.</summary>
    public ToolLimits? Limits { get; set; }

    /// <summary>Время регистрации в реестре.</summary>
    public DateTime RegisteredAt { get; init; } = DateTime.UtcNow;

    /// <summary>Источник регистрации: Internal | File | Dynamic.</summary>
    public string Source { get; init; } = "Internal";

    /// <summary>Может ли tool проходить health check.</summary>
    public bool SupportsHealthCheck { get; init; } = false;
}
