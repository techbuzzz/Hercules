using System.Text;
using System.Text.Json;
using Hercules.Config;

namespace Hercules.Tools;

/// <summary>
///     A2A (Agent-to-Agent) клиент (Stage 3).
///     Минимальный JSON-RPC 2.0 клиент для делегирования задач другим агентам.
///     Spec: https://a2a-protocol.org/latest/ (LF AI draft, breaking changes возможны).
///
///     Pin версии через интерфейс — при изменении spec достаточно заменить реализацию.
/// </summary>
public sealed class A2AClient : ITool
{
    public string Name => "a2a";

    public string Description =>
        "Делегировать задачу другому агенту через A2A-протокол (JSON-RPC 2.0). " +
        "Endpoints задаются в appsettings.json:A2A.Endpoints (имя → URL).";

    public string? ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "agent": { "type": "string", "description": "Имя агента из Endpoints" },
            "task": { "type": "string", "description": "Текст задачи" },
            "context": { "type": "string", "description": "Контекст (опц.)" }
          },
          "required": ["agent", "task"]
        }
        """;

    private readonly A2AConfig _cfg;
    private readonly HttpClient _http;

    public A2AClient(A2AConfig cfg)
    {
        _cfg = cfg;
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(cfg.TimeoutSeconds),
        };
    }

    public async Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        A2ARequest? req;
        try
        {
            req = JsonSerializer.Deserialize<A2ARequest>(argumentsJson, JsonOpts);
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Invalid arguments JSON: {ex.Message}");
        }

        if (req is null || string.IsNullOrWhiteSpace(req.Agent) || string.IsNullOrWhiteSpace(req.Task))
        {
            return ToolResult.Fail("Both 'agent' and 'task' are required");
        }

        if (!_cfg.Endpoints.TryGetValue(req.Agent, out var endpoint))
        {
            return ToolResult.Fail(
                $"Unknown agent '{req.Agent}'. Configured: {string.Join(", ", _cfg.Endpoints.Keys)}",
                new Dictionary<string, object> { ["unknown_agent"] = req.Agent });
        }

        try
        {
            var rpc = new
            {
                jsonrpc = "2.0",
                id = Guid.NewGuid().ToString("N")[..8],
                method = "tasks/submit",
                @params = new
                {
                    task = req.Task,
                    context = req.Context ?? "",
                },
            };

            var json = JsonSerializer.Serialize(rpc, JsonOpts);
            using var httpReq = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

            using var resp = await _http.SendAsync(httpReq, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                return ToolResult.Fail(
                    $"A2A server returned HTTP {(int)resp.StatusCode}: {body}",
                    new Dictionary<string, object> { ["http_status"] = (int)resp.StatusCode });
            }

            // Парсим JSON-RPC ответ
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var err))
                {
                    return ToolResult.Fail(
                        $"A2A error: {err.GetRawText()}",
                        new Dictionary<string, object> { ["a2a_error"] = true });
                }
                var result = root.TryGetProperty("result", out var r) ? r.GetRawText() : body;
                return ToolResult.Ok(
                    $"A2A agent '{req.Agent}' response:\n{result}",
                    new Dictionary<string, object> { ["agent"] = req.Agent, ["endpoint"] = endpoint });
            }
            catch
            {
                // Non-JSON response — вернуть как есть
                return ToolResult.Ok(
                    $"A2A agent '{req.Agent}' response:\n{body}",
                    new Dictionary<string, object> { ["agent"] = req.Agent });
            }
        }
        catch (TaskCanceledException)
        {
            return ToolResult.Fail($"A2A request timed out after {_cfg.TimeoutSeconds}s",
                new Dictionary<string, object> { ["timeout"] = true });
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"A2A error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private sealed class A2ARequest
    {
        public string? Agent { get; set; }
        public string? Task { get; set; }
        public string? Context { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}
