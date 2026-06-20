namespace Hercules.WasmSandbox;

/// <summary>
///     Входные параметры выполнения WebAssembly-модуля в sandbox.
/// </summary>
/// <param name="Module">Скомпилированный .wasm-модуль (bytes).</param>
/// <param name="EntryPoint">Имя функции, которую нужно вызвать (по умолчанию "_start" для WASI).</param>
/// <param name="Args">Аргументы CLI, передаваемые в модуль через wasi:cli/environment.</param>
/// <param name="Stdin">Опциональный stdin, передаётся через wasi:io/streams.</param>
/// <param name="Limits">Resource limits (fuel, memory, wall-clock).</param>
/// <param name="Environment">Опциональные env-переменные (в sandbox только allow-list).</param>
public sealed record WasmExecutionRequest(
    byte[] Module,
    string EntryPoint = "_start",
    string[]? Args = null,
    string? Stdin = null,
    WasmResourceLimits? Limits = null,
    IReadOnlyDictionary<string, string>? Environment = null);

/// <summary>
///     Лимиты ресурсов для WASM sandbox.
///     Все лимиты fail-closed: при исчерпании — Trap + ExecutionResult.Status = "timeout"/"killed".
/// </summary>
/// <param name="MaxFuel">
///     Максимальное количество WASM-инструкций. 0 = без лимита (не рекомендуется).
///     1_000_000_000 ≈ 10 сек CPU на типичной нагрузке.
/// </param>
/// <param name="MaxMemoryBytes">
///     Максимум linear memory в байтах. 0 = без лимита (не рекомендуется).
///     100 МБ = 100 * 1024 * 1024.
/// </param>
/// <param name="MaxWallClockMs">
///     Wall-clock таймаут. При срабатывании — epoch interruption + Status = "timeout".
///     0 = без лимита (не рекомендуется в production).
/// </param>
/// <param name="MaxOutputBytes">
///     Лимит на общий объём stdout+stderr (защита от OOM на агенте).
///     0 = без лимита.
/// </param>
public sealed record WasmResourceLimits(
    long MaxFuel = 1_000_000_000,
    long MaxMemoryBytes = 100L * 1024 * 1024,
    int MaxWallClockMs = 30_000,
    int MaxOutputBytes = 1_000_000)
{
    public static WasmResourceLimits Default { get; } = new();

    public static WasmResourceLimits Strict { get; } = new(
        MaxFuel: 100_000_000,
        MaxMemoryBytes: 32L * 1024 * 1024,
        MaxWallClockMs: 5_000,
        MaxOutputBytes: 100_000);
}

/// <summary>
///     Результат выполнения WASM-модуля в sandbox.
/// </summary>
/// <param name="ExitCode">Код выхода. 0 = успех. Ненулевое = trap / error.</param>
/// <param name="Stdout">Захваченный stdout.</param>
/// <param name="Stderr">Захваченный stderr.</param>
/// <param name="DurationMs">Длительность выполнения в миллисекундах.</param>
/// <param name="Status">Статус: ok | failed | timeout | killed | rejected.</param>
/// <param name="FuelConsumed">Фактически потраченное топливо (для метрик и оптимизации лимитов).</param>
/// <param name="PeakMemoryBytes">Пиковое потребление linear memory (best-effort).</param>
public sealed record WasmExecutionResult(
    int ExitCode,
    string Stdout,
    string Stderr,
    long DurationMs,
    string Status,
    long FuelConsumed,
    long PeakMemoryBytes)
{
    public bool IsSuccess => Status == "ok" && ExitCode == 0;

    public static WasmExecutionResult Failed(string stderr, int exitCode = -1) =>
        new(exitCode, "", stderr, 0, "failed", 0, 0);

    public static WasmExecutionResult TimedOut(long durationMs, long fuel, long mem) =>
        new(-1, "", $"Killed after {durationMs}ms (fuel={fuel}, mem={mem})", durationMs, "timeout", fuel, mem);

    public static WasmExecutionResult Killed(string reason) =>
        new(-1, "", reason, 0, "killed", 0, 0);
}

/// <summary>
///     Контракт WASM-sandbox исполнителя.
/// </summary>
public interface IWasmSandbox
{
    /// <summary>Имя исполнителя (например, "wasmtime-wasi").</summary>
    string Name { get; }

    /// <summary>Версия runtime (например, "wasmtime 14.0.0").</summary>
    string RuntimeVersion { get; }

    /// <summary>Выполнить .wasm-модуль в sandbox. Никогда не бросает — все ошибки в WasmExecutionResult.</summary>
    Task<WasmExecutionResult> ExecuteAsync(WasmExecutionRequest request, CancellationToken ct = default);
}
