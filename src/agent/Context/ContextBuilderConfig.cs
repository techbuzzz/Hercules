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

    /// <summary>
    ///     Конфигурация иерархической дистилляции контекста (task_102).
    ///     При <c>Distillation.Mode == Off</c> используется legacy assembly из фактов + эпизодов.
    /// </summary>
    public DistillationConfig Distillation { get; set; } = new();
}

/// <summary>
///     Режим дистилляции (task_102).
///     <list type="bullet">
///         <item><description><c>Off</c> — без дистилляции, только recent messages + факты.</description></item>
///         <item><description><c>Auto</c> — дистилляция запускается при достижении <see cref="DistillationConfig.SummaryInterval"/> сообщений.</description></item>
///         <item><description><c>Manual</c> — только по явному <c>POST /api/context/distill</c>.</description></item>
///     </list>
/// </summary>
public enum DistillationMode
{
    Off,
    Auto,
    Manual
}

/// <summary>
///     Пресет для дистилляции (task_102). Применяется ко всем полям конфига при первом старте,
///     если не переопределено в appsettings.
/// </summary>
public enum DistillationPreset
{
    /// <summary>Минимальный контекст: low maxTokens, частые саммари.</summary>
    Economy,
    /// <summary>Сбалансированный дефолт: medium maxTokens, умеренные интервалы.</summary>
    Balanced,
    /// <summary>Максимальный контекст: high maxTokens, редкие саммари.</summary>
    Full
}

/// <summary>
///     Параметры иерархической дистилляции (task_102).
/// </summary>
public sealed class DistillationConfig
{
    /// <summary>Текущий режим. <c>Off</c> отключает дистилляцию.</summary>
    public DistillationMode Mode { get; set; } = DistillationMode.Off;

    /// <summary>Стратегия компрессии (пока только hierarchical).</summary>
    public string Strategy { get; set; } = "hierarchical";

    /// <summary>
    ///     Сколько последних сообщений оставлять в raw-виде (recent tier).
    ///     Сообщения старше этого порога попадают в summary tier.
    /// </summary>
    public int RecentRawCount { get; set; } = 10;

    /// <summary>
    ///     Интервал (в сообщениях) для генерации summary из older tier.
    ///     При <c>Mode == Auto</c> дистилляция запускается каждые N сообщений.
    /// </summary>
    public int SummaryInterval { get; set; } = 20;

    /// <summary>Извлекать key-facts из ancient messages (старше <c>RecentRawCount + SummaryInterval</c>).</summary>
    public bool KeyFactsExtraction { get; set; } = true;

    /// <summary>Максимум key-facts на сессию в контексте.</summary>
    public int MaxAncientFacts { get; set; } = 50;

    /// <summary>Token budget для summary секции в ассемблированном контексте.</summary>
    public int SummaryTokenBudget { get; set; } = 800;

    /// <summary>Token budget для key-facts секции в ассемблированном контексте.</summary>
    public int KeyFactsTokenBudget { get; set; } = 400;

    /// <summary>Текущий пресет (informational, не влияет на runtime напрямую).</summary>
    public DistillationPreset Preset { get; set; } = DistillationPreset.Balanced;

    /// <summary>
    ///     Применить пресет — перезаписать дефолты полей. Не вызывается если поля уже заданы.
    /// </summary>
    public void ApplyPreset(DistillationPreset preset)
    {
        Preset = preset;
        switch (preset)
        {
            case DistillationPreset.Economy:
                RecentRawCount = 5;
                SummaryInterval = 10;
                MaxAncientFacts = 25;
                SummaryTokenBudget = 400;
                KeyFactsTokenBudget = 200;
                break;
            case DistillationPreset.Balanced:
                RecentRawCount = 10;
                SummaryInterval = 20;
                MaxAncientFacts = 50;
                SummaryTokenBudget = 800;
                KeyFactsTokenBudget = 400;
                break;
            case DistillationPreset.Full:
                RecentRawCount = 25;
                SummaryInterval = 40;
                MaxAncientFacts = 100;
                SummaryTokenBudget = 1600;
                KeyFactsTokenBudget = 800;
                break;
        }
    }
}
