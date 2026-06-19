namespace Hercules.CodeExecution;

/// <summary>
///     Входные параметры выполнения кода в sandbox.
/// </summary>
/// <param name="Code">Исходный код (C# file-based app).</param>
/// <param name="Language">Язык: "csharp" | "fsharp" (пока только csharp реализован).</param>
/// <param name="Args">CLI-аргументы, передаваемые в запускаемый процесс.</param>
/// <param name="TimeoutMs">Таймаут выполнения (overrides SandboxOptions.CpuTimeoutSeconds если задан).</param>
/// <param name="MemoryLimitMb">Лимит памяти (overrides SandboxOptions.MaxVirtualMemoryMb если задан).</param>
/// <param name="WorkingDir">Опциональный подкаталог внутри sandbox (для доп. файлов).</param>
public sealed record ExecutionRequest(
    string Code,
    string Language = "csharp",
    string[]? Args = null,
    int? TimeoutMs = null,
    int? MemoryLimitMb = null,
    string? WorkingDir = null);

/// <summary>
///     Результат выполнения кода в sandbox.
/// </summary>
/// <param name="ExitCode">Код выхода процесса (-1 если не успел стартовать).</param>
/// <param name="Stdout">Захваченный stdout.</param>
/// <param name="Stderr">Захваченный stderr.</param>
/// <param name="DurationMs">Длительность выполнения в миллисекундах.</param>
/// <param name="Status">Статус: ok | rejected | failed | timeout | killed.</param>
/// <param name="BlockedPatterns">Список regex-паттернов, заблокировавших выполнение (если rejected).</param>
/// <param name="SessionDir">Путь к временной директории выполнения (для диагностики).</param>
public sealed record ExecutionResult(
    int ExitCode,
    string Stdout,
    string Stderr,
    long DurationMs,
    string Status,
    IReadOnlyList<string> BlockedPatterns,
    string? SessionDir = null)
{
    public bool IsSuccess => Status == "ok" && ExitCode == 0;

    public static ExecutionResult Rejected(IReadOnlyList<string> patterns) =>
        new(0, "", "Blocked by DangerousCodeScanner", 0, "rejected", patterns);

    public static ExecutionResult Failed(string stderr, int exitCode = -1) =>
        new(exitCode, "", stderr, 0, "failed", Array.Empty<string>());

    public static ExecutionResult TimedOut(long durationMs) =>
        new(-1, "", $"Killed after {durationMs}ms timeout", durationMs, "timeout", Array.Empty<string>());

    public static ExecutionResult Killed(string reason) =>
        new(-1, "", reason, 0, "killed", Array.Empty<string>());
}

/// <summary>
///     Контракт исполнителя кода в sandbox.
/// </summary>
public interface ICodeExecutor
{
    /// <summary>Имя исполнителя (для логов и админ-вывода).</summary>
    string Name { get; }

    /// <summary>Поддерживаемые языки.</summary>
    IReadOnlySet<string> SupportedLanguages { get; }

    /// <summary>Выполнить код в sandbox. Никогда не бросает — все ошибки в ExecutionResult.</summary>
    Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, CancellationToken ct = default);
}
