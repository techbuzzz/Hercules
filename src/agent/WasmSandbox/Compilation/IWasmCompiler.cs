namespace Hercules.WasmSandbox.Compilation;

/// <summary>
///     Компилирует исходный код (.cs / .py / .rs) в .wasm-модуль.
///     В v3.1 реализованы CSharpCompiler (dotnet-wasm-tools) и PythonCompiler (RustPython via external binary).
/// </summary>
public interface IWasmCompiler
{
    /// <summary>Язык: "csharp" | "python" | "rust".</summary>
    string Language { get; }

    /// <summary>Человекочитаемое имя для админ-вывода.</summary>
    string DisplayName { get; }

    /// <summary>
    ///     Скомпилировать исходник в .wasm.
    ///     Возвращает байты wasm-модуля или throws CompilationException.
    ///     Кэширование результата — ответственность вызывающего.
    /// </summary>
    Task<byte[]> CompileAsync(string sourceCode, CancellationToken ct = default);
}

/// <summary>
///     Ошибка компиляции (исходник невалиден, зависимости отсутствуют, и т.п.).
/// </summary>
public sealed class CompilationException : Exception
{
    public string Language { get; }

    public CompilationException(string language, string message, Exception? inner = null)
        : base($"[{language}] {message}", inner)
    {
        Language = language;
    }
}
