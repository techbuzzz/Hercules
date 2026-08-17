using System.Text.Json;
using Hercules.CodeExecution;

namespace Hercules.Tools;

/// <summary>
///     Adapter: ICodeExecutor → ITool. Позволяет LLM вызывать code execution через
///     стандартный tool-протокол (Stage 4). Automatically selects the appropriate executor:
///     SkillSdkExecutor for code referencing Hercules.SkillSdk, otherwise DotnetFileBasedExecutor.
/// </summary>
public sealed class CodeExecutionTool : ITool
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IReadOnlyList<ICodeExecutor> _executors;

    public CodeExecutionTool(IEnumerable<ICodeExecutor> executors)
    {
        _executors = executors?.ToList() ?? throw new ArgumentNullException(nameof(executors));
    }

    /// <summary>
    ///     Convenience ctor for tests/backward compat with a single executor.
    /// </summary>
    public CodeExecutionTool(ICodeExecutor executor)
    {
        _executors = executor is null
            ? throw new ArgumentNullException(nameof(executor))
            : new List<ICodeExecutor> { executor };
    }

    private ICodeExecutor SelectExecutor(string code)
    {
        if (code.Contains("Hercules.SkillSdk", StringComparison.Ordinal))
        {
            var sdk = _executors.FirstOrDefault(e => e.Name.Equals("skill-sdk-in-process", StringComparison.OrdinalIgnoreCase));
            if (sdk is not null)
            {
                return sdk;
            }
        }

        var fallback = _executors.FirstOrDefault(e => e.Name.Equals("dotnet-file-based", StringComparison.OrdinalIgnoreCase));
        return fallback ?? _executors[0];
    }

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
            var executor = SelectExecutor(req.Code);
            var execReq = new ExecutionRequest(
                req.Code,
                "csharp",
                req.Args?.ToArray(),
                req.TimeoutMs);
            ExecutionResult result = await executor.ExecuteAsync(execReq, ct);

            var meta = new Dictionary<string, object>
            {
                ["status"] = result.Status,
                ["exit_code"] = result.ExitCode,
                ["duration_ms"] = result.DurationMs
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

            return ToolResult.Fail(
                $"❌ {result.Status} (exit={result.ExitCode}, {result.DurationMs}ms)\n" +
                (string.IsNullOrEmpty(result.Stderr)
                    ? result.Stdout
                    : result.Stderr),
                meta);
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
}
