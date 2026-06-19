using System.Text.Json;
using Hercules.CodeExecution;

namespace Hercules.Tools;

/// <summary>
///     Adapter: ICodeExecutor → ITool. Позволяет LLM вызывать code execution через
///     стандартный tool-протокол (Stage 4).
/// </summary>
public sealed class CodeExecutionTool : ITool
{
    public string Name => "execute_code";

    public string Description =>
        "Запустить C# код в sandbox (dotnet run --file). " +
        "Возвращает stdout/stderr/status. Ограничения: 30s timeout, сеть по умолчанию запрещена, " +
        "Process.Start/File.Delete блокируются. Для data processing / вычислений.";

    public string? ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "code": { "type": "string", "description": "Полный C# код (top-level statements)" },
            "args": { "type": "array", "items": { "type": "string" }, "description": "CLI args (опц.)" },
            "timeoutMs": { "type": "integer", "description": "Timeout в мс (опц., default 30000)" }
          },
          "required": ["code"]
        }
        """;

    private readonly ICodeExecutor _executor;

    public CodeExecutionTool(ICodeExecutor executor)
    {
        _executor = executor;
    }

    public async Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        ExecRequest? req;
        try
        {
            req = JsonSerializer.Deserialize<ExecRequest>(argumentsJson, JsonOpts);
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Invalid arguments JSON: {ex.Message}");
        }

        if (req is null || string.IsNullOrWhiteSpace(req.Code))
        {
            return ToolResult.Fail("'code' is required");
        }

        try
        {
            var execReq = new ExecutionRequest(
                Code: req.Code,
                Language: "csharp",
                Args: req.Args?.ToArray(),
                TimeoutMs: req.TimeoutMs);
            var result = await _executor.ExecuteAsync(execReq, ct);

            var meta = new Dictionary<string, object>
            {
                ["status"] = result.Status,
                ["exit_code"] = result.ExitCode,
                ["duration_ms"] = result.DurationMs,
            };
            if (result.BlockedPatterns.Count > 0)
            {
                meta["blocked"] = result.BlockedPatterns;
            }

            if (result.IsSuccess)
            {
                var output = $"✅ {result.Status} ({result.DurationMs}ms)\nstdout:\n{result.Stdout}";
                if (!string.IsNullOrEmpty(result.Stderr))
                {
                    output += $"\nstderr:\n{result.Stderr}";
                }
                return ToolResult.Ok(output, meta);
            }
            else
            {
                return ToolResult.Fail(
                    $"❌ {result.Status} (exit={result.ExitCode}, {result.DurationMs}ms)\n" +
                    (string.IsNullOrEmpty(result.Stderr) ? result.Stdout : result.Stderr),
                    meta);
            }
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Executor exception: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private sealed class ExecRequest
    {
        public string? Code { get; set; }
        public List<string>? Args { get; set; }
        public int? TimeoutMs { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
