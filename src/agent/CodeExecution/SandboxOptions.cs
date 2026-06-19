using System.ComponentModel.DataAnnotations;

namespace Hercules.CodeExecution;

/// <summary>
///     Параметры sandbox для исполнения LLM-сгенерированного кода.
///     См. <c>references/code-execution-sandbox.md</c> для threat model и обоснования дефолтов.
/// </summary>
public sealed class SandboxOptions
{
    /// <summary>
    ///     Корневая директория для изолированных сессий.
    ///     Каждое выполнение получает {TempRoot}/{session_id}/.
    ///     Linux/macOS: /tmp/hercules/sessions по умолчанию.
    ///     Windows: %LOCALAPPDATA%/Hercules/sessions.
    /// </summary>
    public string TempRoot { get; set; } = DefaultTempRoot();

    /// <summary>Wall-clock таймаут для всего выполнения (секунды).</summary>
    public int CpuTimeoutSeconds { get; set; } = 30;

    /// <summary>Максимальный размер файла, который может создать процесс (МБ).</summary>
    public int MaxFileSizeMb { get; set; } = 10;

    /// <summary>Защита от fork-bomb: максимум процессов. 0 = не применять (dotnet CLI форкает). 20 = жёсткий лимит.</summary>
    public int MaxProcesses { get; set; } = 0;

    /// <summary>Максимум открытых файловых дескрипторов (минимум 1024 для dotnet CLI/MSBuild).</summary>
    public int MaxOpenFiles { get; set; } = 1024;

    /// <summary>Лимит виртуальной памяти (МБ). 0 = не применять (CLR требует много VM для heap init).</summary>
    public long MaxVirtualMemoryMb { get; set; } = 0;

    /// <summary>
    ///     По умолчанию сеть запрещена. Включать только для скиллов, явно требующих сеть
    ///     (например, "fetch URL and parse"). Документировать в skill meta.json.
    /// </summary>
    public bool AllowNetwork { get; set; } = false;

    /// <summary>Лимит на размер исходного кода (КБ) — pre-execution.</summary>
    public int MaxCodeSizeKb { get; set; } = 100;

    /// <summary>
    ///     Escape hatch: явно разрешённые namespace (например, ["System.Net.Http.HttpClient"]
    ///     для fetch-скилла). Использовать редко — каждый entry расширяет attack surface.
    /// </summary>
    public string[]? CustomAllowedNamespaces { get; set; }

    /// <summary>
    ///     Дополнительные regex-паттерны для блокировки сверх defaults.
    ///     Передавать ТОЛЬКО Regex.Escape-литералы (защита от backtracking DoS).
    /// </summary>
    public string[]? CustomBlockedPatterns { get; set; }

    /// <summary>Сколько времени хранить сессию на диске после выполнения (секунды, для отладки). 0 = чистить сразу.</summary>
    public int SessionTtlSeconds { get; set; } = 3600; // 1 час

    private static string DefaultTempRoot() =>
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hercules", "sessions")
            : "/tmp/hercules/sessions";

    /// <summary>Валидация — вызывать при создании executor'а.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TempRoot))
            throw new InvalidOperationException("SandboxOptions.TempRoot is required");
        if (CpuTimeoutSeconds <= 0 || CpuTimeoutSeconds > 300)
            throw new InvalidOperationException("CpuTimeoutSeconds must be 1..300");
        if (MaxFileSizeMb <= 0 || MaxFileSizeMb > 100)
            throw new InvalidOperationException("MaxFileSizeMb must be 1..100");
        if (MaxCodeSizeKb <= 0 || MaxCodeSizeKb > 1024)
            throw new InvalidOperationException("MaxCodeSizeKb must be 1..1024");
        if (MaxVirtualMemoryMb < 0 || MaxVirtualMemoryMb > 16384)
            throw new InvalidOperationException("MaxVirtualMemoryMb must be 0..16384 (0 = no limit)");
    }
}
