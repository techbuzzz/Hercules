namespace Hercules.WasmSandbox.Compilation;

/// <summary>
///     Реестр доступных компиляторов для разных языков.
///     Используется WasmTool для выбора IWasmCompiler по языку.
/// </summary>
public sealed class CompilerRegistry
{
    private readonly Dictionary<string, IWasmCompiler> _compilers = new(StringComparer.OrdinalIgnoreCase);

    public CompilerRegistry Register(IWasmCompiler compiler)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        _compilers[compiler.Language] = compiler;
        return this;
    }

    public IWasmCompiler? Resolve(string language)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;
        return _compilers.TryGetValue(language, out var c) ? c : null;
    }

    public IEnumerable<string> Languages => _compilers.Keys;

    public IEnumerable<IWasmCompiler> Compilers => _compilers.Values;
}
