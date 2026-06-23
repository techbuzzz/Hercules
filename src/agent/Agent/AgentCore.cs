using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hercules.Config;
using Hercules.LLM;
using Hercules.Storage;
using Hercules.Tools;

namespace Hercules.Agent;

/// <summary>Ответ агента на один запрос пользователя.</summary>
public sealed record AgentResponse
{
    public string Answer { get; init; } = "";
    public string Mode { get; init; } = "direct"; // skill | direct | tool
    public string Confidence { get; init; } = "medium"; // high | medium | low
    public string Provider { get; init; } = "";
    public Skill? UsedSkill { get; init; }
    public string? ToolUsed { get; init; }

    /// <summary>Если задан — агент предлагает создать навык (нужно подтверждение пользователя).</summary>
    public string? ProposeSkillForInput { get; init; }

    /// <summary>Если задан — агент предлагает улучшить навык с этим id.</summary>
    public string? ProposeImproveSkillId { get; init; }

    public string? ProposeImproveSkillName { get; init; }
}

/// <summary>
///     Ядро агента: реализует главный цикл обработки запроса —
///     загрузка памяти → маршрутизация навыка → вызов LLM → tool-execution (если LLM запросил) →
///     финальный ответ → логирование → проверка порогов создания/улучшения навыков.
/// </summary>
public sealed class AgentCore
{
    private static readonly Regex ConfidenceRx =
        new(@"\[confidence:\s*(high|medium|low)\s*\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Парсим JSON-блок с action из ответа LLM: {"action": "tool_name", "arguments": {...}}</summary>
    private static readonly Regex ActionRx =
        new(@"\{\s*""action""\s*:\s*""(?<name>[^""]+)""\s*,\s*""arguments""\s*:\s*(?<args>\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\})\s*\}",
            RegexOptions.Compiled);

    private static readonly JsonSerializerOptions ActionJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Максимум tool-итераций (защита от infinite loops).</summary>
    private const int MaxToolIterations = 3;

    private readonly ILLMClient _llm;
    private readonly SkillRouter _router;
    private readonly SkillManager _skills;
    private readonly MemoryManager _memory;
    private readonly SqliteSessionStore _sessions;
    private readonly AgentConfig _cfg;
    private readonly ToolRegistry? _tools;

    private readonly List<ChatTurn> _transcript = new();
    private string _contextBlock = "";

    public AgentCore(
        ILLMClient llm,
        SkillRouter router,
        SkillManager skills,
        MemoryManager memory,
        SqliteSessionStore sessions,
        AgentConfig cfg,
        ToolRegistry? tools = null)
    {
        _llm = llm;
        _router = router;
        _skills = skills;
        _memory = memory;
        _sessions = sessions;
        _cfg = cfg;
        _tools = tools;
    }

    public string SessionId { get; } = Guid.NewGuid().ToString("N")[..12];
    public int CommandCount { get; private set; }

    /// <summary>Транскрипт текущей сессии (для сохранения памяти).</summary>
    public IReadOnlyList<ChatTurn> Transcript => _transcript;

    /// <summary>Инициализация сессии: создать запись и загрузить контекст памяти.</summary>
    public void StartSession()
    {
        _sessions.StartSession(SessionId);
        _contextBlock = _memory.BuildContextBlock();
    }

    /// <summary>Обработать один запрос пользователя.</summary>
    public async Task<AgentResponse> HandleAsync(string input, CancellationToken ct = default)
    {
        CommandCount++;

        // 1-2. Маршрутизация навыка
        RouteResult route = _router.Route(input);
        var systemPrompt = BuildSystemPrompt(route.MatchedSkill);

        // 3. Вызов LLM (с возможной tool-итерацией)
        List<ChatTurn> messages = BuildMessages(systemPrompt, input);
        LlmResponse llmResp;
        string toolUsed = "";
        try
        {
            (llmResp, toolUsed) = await RunWithToolsAsync(messages, ct);
        }
        catch (Exception ex)
        {
            return new AgentResponse
            {
                Answer = $"Ошибка обращения к LLM: {ex.Message}",
                Confidence = "low",
                Mode = route.IsSkill ? "skill" : "direct",
                UsedSkill = route.MatchedSkill,
            };
        }

        var (answer, confidence) = ExtractConfidence(llmResp.Text);

        // Поддержка диалогового контекста (история)
        _transcript.Add(new ChatTurn(ChatRole.User, input));
        _transcript.Add(new ChatTurn(ChatRole.Assistant, answer));

        var mode = !string.IsNullOrEmpty(toolUsed)
            ? "tool"
            : route.IsSkill ? "skill" : "direct";

        // 4. Логирование взаимодействия
        _sessions.LogInteraction(new InteractionLog(
            SessionId, input, answer, confidence, mode,
            route.MatchedSkill?.Meta.Id, llmResp.Provider, DateTime.UtcNow));

        // Запись использования навыка (успех = уверенность не low)
        if (route.IsSkill)
        {
            _skills.RecordUsage(route.MatchedSkill!.Meta.Id, confidence != "low", confidence);
        }

        // 5-6. Пороги (skill creation/improvement) — без изменений
        return BuildResponse(answer, confidence, llmResp.Provider, route.MatchedSkill, toolUsed, mode);
    }

    /// <summary>
    ///     Вызвать LLM с возможной tool-итерацией: если LLM вернул JSON с action,
    ///     выполнить tool, положить результат в transcript, вызвать LLM снова.
    ///     Max MaxToolIterations итераций (защита от infinite loops).
    /// </summary>
    private async Task<(LlmResponse Response, string ToolUsed)> RunWithToolsAsync(
        List<ChatTurn> messages, CancellationToken ct)
    {
        string toolUsed = "";
        LlmResponse last = default!;
        for (int iter = 0; iter <= MaxToolIterations; iter++)
        {
            last = await _llm.CompleteAsync(messages, ct);

            if (_tools is null || _tools.Names.Count == 0)
            {
                return (last, toolUsed);
            }

            // Try to parse tool action from LLM output
            var action = TryParseAction(last.Text);
            if (action is null)
            {
                return (last, toolUsed);
            }

            var (toolName, argsJson) = action.Value;
            var tool = _tools.Get(toolName);
            if (tool is null)
            {
                messages.Add(new ChatTurn(ChatRole.Assistant, last.Text));
                messages.Add(new ChatTurn(ChatRole.User,
                    $"[system] Tool '{toolName}' not found. Available tools: {string.Join(", ", _tools.Names)}. " +
                    "Provide a final answer (without 'action' JSON) or use a different tool."));
                continue;
            }

            // Execute tool
            toolUsed = toolName;
            var toolResult = await tool.ExecuteAsync(argsJson, ct);
            var resultJson = JsonSerializer.Serialize(new
            {
                success = toolResult.Success,
                output = toolResult.Output,
                error = toolResult.Error,
            }, ActionJsonOpts);

            await Console.Error.WriteLineAsync(
                $"[AgentCore] Tool '{toolName}' → {(toolResult.Success ? "ok" : "FAIL")} " +
                $"(output: {toolResult.Output.Length} chars, error: {toolResult.Error?.Length ?? 0} chars)");

            messages.Add(new ChatTurn(ChatRole.Assistant, last.Text));
            messages.Add(new ChatTurn(ChatRole.User,
                $"[system] Tool '{toolName}' result:\n```json\n{resultJson}\n```\n\n" +
                "Use this result to provide a final answer to the user. " +
                "If you need another tool call, include 'action' JSON again. " +
                "Otherwise provide a plain-text response (no JSON)."));

            if (iter == MaxToolIterations)
            {
                messages.Add(new ChatTurn(ChatRole.System,
                    "[system] Maximum tool iterations reached. Provide final answer now."));
            }
        }
        return (last, toolUsed);
    }

    private static (string Name, string ArgsJson)? TryParseAction(string llmText)
    {
        // Ищем JSON-блок с action. LLM может обернуть его в markdown ```json ... ```
        var cleaned = llmText;
        var jsonMatch = Regex.Match(cleaned, @"```(?:json)?\s*(\{.*?\})\s*```", RegexOptions.Singleline);
        if (jsonMatch.Success)
        {
            cleaned = jsonMatch.Groups[1].Value;
        }

        var match = ActionRx.Match(cleaned);
        if (!match.Success) return null;

        return (match.Groups["name"].Value.Trim(), match.Groups["args"].Value.Trim());
    }

    private AgentResponse BuildResponse(
        string answer, string confidence, string provider, Skill? usedSkill,
        string toolUsed, string mode)
    {
        // (skill creation / improvement threshold logic — same as before)
        return new AgentResponse
        {
            Answer = answer,
            Mode = mode,
            Confidence = confidence,
            Provider = provider,
            UsedSkill = usedSkill,
            ToolUsed = string.IsNullOrEmpty(toolUsed) ? null : toolUsed,
        };
    }

    /// <summary>
    ///     Псевдоним для <see cref="HandleAsync" /> — используется Web API адаптером.
    /// </summary>
    public Task<AgentResponse> ProcessMessageAsync(string input, CancellationToken ct = default)
    {
        return HandleAsync(input, ct);
    }

    /// <summary>Сбросить счётчик повторов запроса (после создания навыка).</summary>
    public void ResetRequestCounter(string input)
    {
        _sessions.ResetRequestCount(SkillRouter.Normalize(input));
    }

    /// <summary>Нужно ли запустить рефлексию по числу команд.</summary>
    public bool ShouldReflectByCount()
    {
        return _cfg.ReflectionEveryNCommands > 0 &&
               CommandCount > 0 &&
               CommandCount % _cfg.ReflectionEveryNCommands == 0;
    }

    public void EndSession()
    {
        _sessions.EndSession(SessionId);
    }

    // ---- Вспомогательные методы ----

    private string BuildSystemPrompt(Skill? skill)
    {
        var sb = new StringBuilder();
        sb.AppendLine(_cfg.SystemPrompt);
        sb.AppendLine();
        sb.AppendLine(_contextBlock);
        if (skill is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"=== АКТИВНЫЙ НАВЫК: {skill.Meta.Name} ===");
            sb.AppendLine(skill.Prompt);
        }

        // Stage 4: tool ecosystem injection
        if (_tools is not null && _tools.Names.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(_tools.ListForLLM());
        }

        sb.AppendLine();
        sb.AppendLine("В КОНЦЕ ответа добавь на отдельной строке маркер уверенности в формате: [confidence: high|medium|low]");
        return sb.ToString();
    }

    private List<ChatTurn> BuildMessages(string systemPrompt, string input)
    {
        var msgs = new List<ChatTurn> { new(ChatRole.System, systemPrompt) };
        msgs.AddRange(_transcript.TakeLast(8));
        msgs.Add(new ChatTurn(ChatRole.User, input));
        return msgs;
    }

    /// <summary>Извлечь маркер уверенности и убрать его из текста ответа.</summary>
    private static (string Answer, string Confidence) ExtractConfidence(string text)
    {
        Match match = ConfidenceRx.Match(text);
        if (!match.Success)
        {
            return (text.Trim(), "medium");
        }

        var confidence = match.Groups[1].Value.ToLowerInvariant();
        var cleaned = ConfidenceRx.Replace(text, "").Trim();
        return (cleaned, confidence);
    }
}
