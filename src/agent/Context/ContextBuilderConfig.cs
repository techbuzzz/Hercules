namespace Hercules.Config;

/// <summary>
///     Конфигурация context assembly (task_027).
///     Контролирует token budget, limits и compression threshold.
/// </summary>
public sealed class ContextConfig
{
    /// <summary>Включить context assembly. Если false — используется legacy BuildContextBlock.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Максимум токенов для context block (input для LLM + system prompt не считаются).</summary>
    public int MaxContextTokens { get; set; } = 6000;

    /// <summary>Максимум фактов в context (без учёта token budget).</summary>
    public int MaxFactsInContext { get; set; } = 20;

    /// <summary>Максимум episodes в context.</summary>
    public int MaxEpisodesInContext { get; set; } = 5;

    /// <summary>Максимум tool schemas в context.</summary>
    public int MaxToolSchemasInContext { get; set; } = 10;

    /// <summary>
    ///     Порог числа tool calls в trace для запуска compression.
    ///     Если trace.length >= CompressionThreshold — сжимается в episodic memory.
    /// </summary>
    public int CompressionThreshold { get; set; } = 3;

    /// <summary>
    ///     Оценка overhead системного prompt (system prompt + skill + confidence marker).
    ///     Используется для расчёта оставшегося budget под context block.
    ///     Default: 2000 токенов.
    /// </summary>
    public int SystemPromptOverheadTokens { get; set; } = 2000;
}
