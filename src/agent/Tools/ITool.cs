namespace Hercules.Tools;

/// <summary>
///     Базовый контракт tool'а, доступного агенту.
///     Реализации регистрируются в <see cref="ToolRegistry" /> по имени
///     и могут быть вызваны LLM-агентом через специальный action-протокол (Stage 4).
/// </summary>
public interface ITool
{
    /// <summary>Имя tool'а (lowercase, unique). Используется LLM для вызова.</summary>
    string Name { get; }

    /// <summary>Краткое описание (1-2 строки) для LLM prompt injection.</summary>
    string Description { get; }

    /// <summary>
    ///     JSON Schema параметров (минимальный, для LLM prompt).
    ///     Если null — параметров нет.
    /// </summary>
    string? ParametersSchema { get; }

    /// <summary>Выполнить вызов. Никогда не бросает — все ошибки в ToolResult.</summary>
    Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default);
}

/// <summary>
///     Результат вызова tool'а.
/// </summary>
/// <param name="Success">true если tool успешно отработал.</param>
/// <param name="Output">Текстовый output (stdout-style). Может быть многострочным.</param>
/// <param name="Error">Текст ошибки (если Success=false).</param>
/// <param name="Metadata">Доп. данные для рефлексии (HTTP status, duration, blocked_reason, etc.).</param>
public sealed record ToolResult(
    bool Success,
    string Output,
    string? Error = null,
    IReadOnlyDictionary<string, object>? Metadata = null)
{
    public static ToolResult Ok(string output, IReadOnlyDictionary<string, object>? meta = null)
        => new(true, output, null, meta);

    public static ToolResult Fail(string error, IReadOnlyDictionary<string, object>? meta = null)
        => new(false, "", error, meta);
}
