namespace Hercules.Context;

/// <summary>
///     Тип контекстного элемента.
/// </summary>
public enum ContextItemType
{
    DurableFact,
    WorkingMemory,
    Episode,
    ToolSchema,
    SkillPrompt,
    RequestContext
}

/// <summary>
///     Контекстный элемент с приоритетом и оценкой токенов.
/// </summary>
/// <param name="Type">Категория элемента.</param>
/// <param name="Content">Текстовое содержание для LLM.</param>
/// <param name="TokenEstimate">Оценка токенов (через EstimateTokens).</param>
/// <param name="Importance">Важность для сохранения (High/Medium/Low).</param>
/// <param name="Source">Источник: "working_memory", "durable_fact", "episodic", "tool_schema", "skill", "request".</param>
/// <param name="Tags">Дополнительные теги для фильтрации.</param>
public sealed record ContextItem(
    ContextItemType Type,
    string Content,
    int TokenEstimate,
    ImportanceLevel Importance,
    string Source,
    List<string> Tags = default!);

/// <summary>
///     Importance level для приоритизации контекста.
/// </summary>
public enum ImportanceLevel
{
    High,
    Medium,
    Low
}

/// <summary>
///     Token budget для context assembly.
/// </summary>
/// <param name="MaxTokens">Максимальный бюджет.</param>
/// <param name="UsedTokens">Использовано токенов.</param>
/// <param name="RemainingTokens">Осталось.</param>
public sealed record ContextBudget(
    int MaxTokens,
    int UsedTokens,
    int RemainingTokens);

/// <summary>
///     Один tool call в trace.
/// </summary>
/// <param name="ToolName">Имя tool.</param>
/// <param name="Input">Входные аргументы (JSON string).</param>
/// <param name="Output">Результат выполнения.</param>
/// <param name="DurationMs">Длительность в миллисекундах.</param>
/// <param name="Timestamp">UTC timestamp.</param>
/// <param name="Success">Успешно ли выполнен.</param>
public sealed record ToolTraceEntry(
    string ToolName,
    string Input,
    string Output,
    long DurationMs,
    DateTime Timestamp,
    bool Success);

/// <summary>
///     Результат assembly контекста.
/// </summary>
/// <param name="ContextBlock">Итоговый text block для system prompt.</param>
/// <param name="Budget">Использованный budget.</param>
/// <param name="ItemCount">Число включённых элементов.</param>
/// <param name="Truncated">Были ли элементы отброшены по budget.</param>
public sealed record ContextAssembly(
    string ContextBlock,
    ContextBudget Budget,
    int ItemCount,
    bool Truncated);
