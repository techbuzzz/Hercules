namespace Hercules.LLM;

/// <summary>
///     Результат health check одного LLM-провайдера.
/// </summary>
public sealed record ProviderHealthResult
{
    public required string Provider { get; init; }
    public required bool Healthy { get; init; }
    public required string Status { get; init; }
    public string? Error { get; init; }
    public int LatencyMs { get; init; }
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
///     Способности одного LLM-провайдера.
/// </summary>
public sealed record ProviderCapabilities
{
    /// <summary>Имя провайдера.</summary>
    public required string Provider { get; init; }

    /// <summary>Поддержка vision (image inputs).</summary>
    public bool Vision { get; init; }

    /// <summary>Поддержка function calling / tool use.</summary>
    public bool FunctionCalling { get; init; }

    /// <summary>Поддержка streaming.</summary>
    public bool Streaming { get; init; } = true;

    /// <summary>Max context tokens (приблизительно; -1 = неизвестно).</summary>
    public int MaxContextTokens { get; init; } = -1;

    /// <summary>Список доступных моделей (если удалось получить).</summary>
    public List<string> Models { get; init; } = [];

    /// <summary>Конкретная текущая модель из конфига.</summary>
    public string Model { get; init; } = "";

    /// <summary>Когда определено.</summary>
    public DateTime DetectedAt { get; init; } = DateTime.UtcNow;
}
