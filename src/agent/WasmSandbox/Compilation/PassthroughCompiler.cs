namespace Hercules.WasmSandbox.Compilation;

/// <summary>
///     "Компилятор", который принимает уже готовый .wasm-модуль (hex-encoded или base64).
///     Используется для тестирования и для случаев, когда компиляция делается вручную (rustc → wasm32-wasi).
/// </summary>
public sealed class PassthroughCompiler : IWasmCompiler
{
    public string Language => "wasm";

    public string DisplayName => "Raw WASM module (already compiled)";

    public Task<byte[]> CompileAsync(string sourceCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
            throw new CompilationException("wasm", "Empty wasm source");

        var trimmed = sourceCode.Trim();

        // Поддерживаем hex (preferred) или base64.
        if (IsHex(trimmed))
        {
            try { return Task.FromResult(Convert.FromHexString(trimmed)); }
            catch (FormatException ex)
            {
                throw new CompilationException("wasm", $"Invalid hex: {ex.Message}", ex);
            }
        }

        try
        {
            return Task.FromResult(Convert.FromBase64String(trimmed));
        }
        catch (FormatException ex)
        {
            throw new CompilationException("wasm",
                "Source must be hex-encoded (preferred) or base64-encoded .wasm bytes.", ex);
        }
    }

    private static bool IsHex(string s)
    {
        // Эвристика: wasm всегда начинается с \0asm (00 61 73 6D) → hex "0061736d..."
        return s.StartsWith("0061736d", StringComparison.OrdinalIgnoreCase)
            || (s.Length % 2 == 0 && s.All(c => "0123456789abcdefABCDEF".Contains(c)) && s.Length > 8);
    }
}
