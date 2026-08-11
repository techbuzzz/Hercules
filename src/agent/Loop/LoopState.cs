namespace Hercules.Agent.Loop;

/// <summary>
///     Фаза (шаг) внутри одного вызова <see cref="AgentCore.HandleAsync" />.
///     Каждый вход/выход из шага логируется как <c>[Loop] {Step} started/finished</c>.
// </summary>
public enum LoopStep
{
    /// <summary>Маршрутизация навыка через SkillRouter.</summary>
    SkillRoute,

    /// <summary>Вызов LLM (прямой или с tool iteration).</summary>
    LlmCall,

    /// <summary>Исполнение одного tool (HttpTool, CodeExecutionTool и т.д.).</summary>
    ToolExecute,

    /// <summary>Обновление памяти (PersistSessionAsync при EndSession).</summary>
    MemoryUpdate,

    /// <summary>Запись InteractionLog в SqliteSessionStore.</summary>
    LogInteraction
}

/// <summary>
///     Состояние одного вызова <see cref="AgentCore.HandleAsync" />.
///     Передаётся через <c>RunWithToolsAsync</c> для явного трекинга шагов.
/// </summary>
/// <param name="CurrentStep">Текущий выполняемый шаг.</param>
/// <param name="ToolIteration">Номер итерации tool-call loop (0 = без tools).</param>
/// <param name="LastTool">Имя последнего выполненного tool или null.</param>
/// <param name="Cancelled">Был ли запрос отменён извне.</param>
public readonly record struct LoopContext(
    LoopStep CurrentStep,
    int ToolIteration,
    string? LastTool,
    bool Cancelled)
{
    /// <summary>Начальное состояние: маршрутизация, итерация 0, без tool.</summary>
    public static LoopContext Initial => new(LoopStep.SkillRoute, 0, null, false);

    /// <summary>Следующий контекст с обновлённым шагом.</summary>
    public LoopContext WithStep(LoopStep step) => this with { CurrentStep = step };

    /// <summary>Следующий контекст после исполнения tool.</summary>
    public LoopContext AfterTool(string toolName) =>
        this with { CurrentStep = LoopStep.ToolExecute, ToolIteration = ToolIteration + 1, LastTool = toolName };

    /// <summary>Пометить контекст как отменённый.</summary>
    public LoopContext CancelledContext() => this with { Cancelled = true };
}

/// <summary>
///     Результат одного шага цикла.
/// </summary>
public readonly record struct StepResult(
    LoopStep Step,
    bool Success,
    string? Error,
    TimeSpan Elapsed)
{
    public static StepResult Ok(LoopStep step, TimeSpan elapsed) =>
        new(step, true, null, elapsed);

    public static StepResult Fail(LoopStep step, string error, TimeSpan elapsed) =>
        new(step, false, error, elapsed);
}
