using System.Diagnostics;

namespace Hercules.WasmSandbox.Compilation;

/// <summary>
///     Компилирует C# код в WASM через `dotnet workload install wasm-tools` + `dotnet publish`.
///     Требует:
///       • .NET 10 SDK
///       • `dotnet workload install wasm-tools` (должно быть установлено заранее)
///       • `dotnet workload install wasi-experimental` (для wasi-wasm RID)
///     В v3.1 — STUB. Реальная компиляция требует отдельного temp .csproj с RID=wasi-wasm,
///     publish, и извлечения .wasm. Помечено как stub для последующей реализации в V3.2.
/// </summary>
public sealed class CSharpCompiler : IWasmCompiler
{
    public string Language => "csharp";

    public string DisplayName => "C# → WASM (via dotnet wasm-tools)";

    public Task<byte[]> CompileAsync(string sourceCode, CancellationToken ct = default)
    {
        // V3.1: stub. V3.2: реализовать через dotnet publish -c Release -r wasi-wasm.
        throw new CompilationException("csharp",
            "C# → WASM compilation requires dotnet wasm-tools workload and a separate publish step. " +
            "Planned for V3.2. For now, provide a pre-compiled .wasm via 'wasm' language or use the existing " +
            "DotnetFileBasedExecutor (file-based apps) which runs C# directly without WASM.");
    }
}

/// <summary>
///     Компилирует Python в WASM через RustPython.wasm.
///     Требует:
///       • Скачать rustpython.wasm (~10 MB) в ~/.hercules/wasm/rustpython.wasm
///       • Python скрипт упаковывается как init-секция custom section в wasm
///     V3.1: STUB. V3.2: реализовать через:
///       1) `wasi_snapshot_preview1.args_get` для получения скрипта из argv
///       2) либо через embedding скрипта как custom section (name="python_src")
///     Для прототипирования — используйте готовый RustPython.wasm + его WASI CLI интерфейс.
/// </summary>
public sealed class PythonCompiler : IWasmCompiler
{
    public string Language => "python";

    public string DisplayName => "Python → WASM (via RustPython)";

    public Task<byte[]> CompileAsync(string sourceCode, CancellationToken ct = default)
    {
        throw new CompilationException("python",
            "Python → WASM compilation requires RustPython.wasm pre-installed at " +
            "~/.hercules/wasm/rustpython.wasm. Planned for V3.2. " +
            "For now, use 'wasm' language directly with a pre-compiled RustPython + script, " +
            "or wait for V3.2 implementation.");
    }
}
