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
///     Включает bounded-execution параметры (task_008).
/// </summary>
/// <param name="CurrentStep">Текущий выполняемый шаг.</param>
/// <param name="ToolIteration">Номер итерации tool-call loop (0 = без tools).</param>
/// <param name="LastTool">Имя последнего выполненного tool или null.</param>
/// <param name="Cancelled">Был ли запрос отменён извне.</param>
/// <param name="MaxIterations">Effective max tool iterations (from config or per-request override).</param>
/// <param name="WallClockTimeout">Wall-clock timeout для этого запроса (null = из конфига).</param>
/// <param name="StartTime">Время старта запроса (для wall-clock tracking).</param>
/// <param name="RecursionDepth">Текущая глубина вложенности (0 = top-level).</param>
/// <param name="MaxRecursionDepth">Максимальная разрешённая глубина (0 = unlimited).</param>
/// <param name="CancellationRequested">Запрос на отмену от runtime policy.</param>
public readonly record struct LoopContext(
    LoopStep CurrentStep,
    int ToolIteration,
    string? LastTool,
    bool Cancelled,
    int MaxIterations,
    TimeSpan? WallClockTimeout,
    DateTimeOffset StartTime,
    int RecursionDepth,
    int MaxRecursionDepth,
    bool CancellationRequested)
{
    /// <summary>Начальное состояние: маршрутизация, итерация 0, без tool.</summary>
    /// <param name="maxIterations">Effective max iterations (from config).</param>
    /// <param name="wallClockTimeout">Wall-clock timeout (from config, null = unlimited).</param>
    /// <param name="maxRecursionDepth">Max recursion depth (from config, 0 = unlimited).</param>
    public static LoopContext Initial(
        int maxIterations = 3,
        TimeSpan? wallClockTimeout = null,
        int maxRecursionDepth = 2) =>
        new(LoopStep.SkillRoute, 0, null, false,
            maxIterations, wallClockTimeout, DateTimeOffset.UtcNow,
            0, maxRecursionDepth, false);

    /// <summary>Следующий контекст с обновлённым шагом.</summary>
    public LoopContext WithStep(LoopStep step) => this with { CurrentStep = step };

    /// <summary>Следующий контекст после исполнения tool.</summary>
    public LoopContext AfterTool(string toolName) =>
        this with
        {
            CurrentStep = LoopStep.ToolExecute,
            ToolIteration = ToolIteration + 1,
            LastTool = toolName
        };

    /// <summary>Пометить контекст как отменённый.</summary>
    public LoopContext CancelledContext() => this with { Cancelled = true };

    /// <summary>Увеличить глубину рекурсии на 1.</summary>
    public LoopContext TickRecursion() =>
        this with { RecursionDepth = RecursionDepth + 1 };

    /// <summary>Уменьшить глубину рекурсии на 1 (при возврате из вложенного вызова).</summary>
    public LoopContext UnwindRecursion() =>
        this with { RecursionDepth = Math.Max(0, RecursionDepth - 1) };

    /// <summary>
    ///     Пометить, что отмена запрошена runtime policy
    ///     (e.g. budget exceeded, approval denied, policy violation).
    /// </summary>
    public LoopContext WithCancellationRequested() =>
        this with { CancellationRequested = true };

    /// <summary>
    ///     Установить override для max iterations (per-request limit).
    /// </summary>
    public LoopContext WithMaxIterations(int max) =>
        this with { MaxIterations = max };

    /// <summary>
    ///     Проверить, не превышен ли wall-clock timeout.
    /// </summary>
    public bool IsWallClockExpired => WallClockTimeout.HasValue &&
        DateTimeOffset.UtcNow - StartTime > WallClockTimeout.Value;

    /// <summary>
    ///     Проверить, не превышена ли глубина рекурсии.
    /// </summary>
    public bool IsRecursionExceeded => MaxRecursionDepth > 0 && RecursionDepth >= MaxRecursionDepth;
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
