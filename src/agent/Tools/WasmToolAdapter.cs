using System.Text.Json;
using Hercules.WasmSandbox;
using Hercules.WasmSandbox.Compilation;

namespace Hercules.Tools;

/// <summary>
///     Адаптер: <see cref="WasmTool" /> → <see cref="ITool" />.
///     Позволяет LLM вызывать WASM-sandbox исполнение кода через стандартный
///     tool-протокол (Stage 4) наравне с HttpTool, CodeExecutionTool и A2AClient.
///     Парсит JSON-аргументы {source, language, args?} и делегирует в WasmTool.
/// </summary>
public sealed class WasmToolAdapter : ITool
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CompilerRegistry _compilers;
    private readonly IWasmSandbox _sandbox;

    private readonly WasmTool _wasmTool;

    public WasmToolAdapter(WasmTool wasmTool, IWasmSandbox sandbox, CompilerRegistry compilers)
    {
        _wasmTool = wasmTool ?? throw new ArgumentNullException(nameof(wasmTool));
        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
        _compilers = compilers ?? throw new ArgumentNullException(nameof(compilers));
    }

    public string Name => "execute_wasm";

    public string Description =>
        "Выполнить код в WebAssembly sandbox (Wasmtime, capability-based isolation). " +
        "Поддерживает языки csharp/python/rust (требуется компилятор) или готовый .wasm (hex/base64). " +
        "Возвращает stdout/stderr/status/fuel. Изоляция: fuel limit, memory cap, wall-clock timeout, " +
        "файловая система и сеть запрещены по умолчанию. Безопаснее чем execute_code.";

    public string? ParametersSchema => """
                                       {
                                         "type": "object",
                                         "properties": {
                                           "source": { "type": "string", "description": "Исходный код (csharp/python/rust) или готовый .wasm в hex/base64" },
                                           "language": { "type": "string", "description": "Язык: csharp | python | rust | wasm", "default": "wasm" },
                                           "args": { "type": "array", "items": { "type": "string" }, "description": "CLI args (опц.)" }
                                         },
                                         "required": ["source", "language"]
                                       }
                                       """;

    public async Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        WasmToolArgs? req;
        try
        {
            req = JsonSerializer.Deserialize<WasmToolArgs>(argumentsJson, JsonOpts);
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Invalid arguments JSON: {ex.Message}");
        }

        if (req is null || string.IsNullOrWhiteSpace(req.Source))
        {
            return ToolResult.Fail("'source' is required");
        }

        if (string.IsNullOrWhiteSpace(req.Language))
        {
            return ToolResult.Fail("'language' is required (csharp | python | rust | wasm)");
        }

        try
        {
            var toolRequest = new WasmToolRequest(
                req.Source,
                req.Language,
                req.Args?.ToArray());
            WasmToolResult result = await _wasmTool.ExecuteAsync(toolRequest, ct);

            var meta = new Dictionary<string, object>
            {
                ["status"] = result.Execution.Status,
                ["exit_code"] = result.Execution.ExitCode,
                ["duration_ms"] = result.Execution.DurationMs,
                ["fuel_consumed"] = result.Execution.FuelConsumed,
                ["compiled"] = result.Compiled,
                ["compile_duration_ms"] = result.CompileDuration.TotalMilliseconds,
                ["total_duration_ms"] = result.TotalDuration.TotalMilliseconds
            };

            if (!string.IsNullOrEmpty(result.CompilationError))
            {
                return ToolResult.Fail($"Compilation error: {result.CompilationError}", meta);
            }

            if (result.Execution.IsSuccess)
            {
                var output = $"✅ {result.Execution.Status} ({result.Execution.DurationMs}ms, fuel={result.Execution.FuelConsumed})\nstdout:\n{result.Execution.Stdout}";
                if (!string.IsNullOrEmpty(result.Execution.Stderr))
                {
                    output += $"\nstderr:\n{result.Execution.Stderr}";
                }

                return ToolResult.Ok(output, meta);
            }

            return ToolResult.Fail(
                $"❌ {result.Execution.Status} (exit={result.Execution.ExitCode}, {result.Execution.DurationMs}ms)\n" +
                (string.IsNullOrEmpty(result.Execution.Stderr)
                    ? result.Execution.Stdout
                    : result.Execution.Stderr),
                meta);
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"WasmTool exception: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private sealed class WasmToolArgs
    {
        public string? Source { get; set; }
        public string? Language { get; set; }
        public List<string>? Args { get; set; }
    }
}
