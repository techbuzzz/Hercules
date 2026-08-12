using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hercules.Agent.Loop;
using Hercules.Config;
using Hercules.LLM;
using Hercules.Storage;
using Hercules.Tools;
using Microsoft.Extensions.Logging;

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
public sealed class AgentCore : IConfigReload
{
    /// <summary>Максимум tool-итераций (защита от infinite loops).</summary>
    private const int MaxToolIterations = 3;

    private static readonly Regex ConfidenceRx =
        new(@"\[confidence:\s*(high|medium|low)\s*\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Парсим JSON-блок с action из ответа LLM: {"action": "tool_name", "arguments": {...}}</summary>
    private static readonly Regex ActionRx =
        new(@"\{\s*""action""\s*:\s*""(?<name>[^""]+)""\s*,\s*""arguments""\s*:\s*(?<args>\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\})\s*\}",
            RegexOptions.Compiled);

    private static readonly JsonSerializerOptions ActionJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILLMClient _llm;
    private readonly ILogger<AgentCore> _logger;
    private readonly MemoryManager _memory;
    private readonly SkillRouter _router;
    private readonly SqliteSessionStore _sessions;
    private readonly SkillManager _skills;
    private readonly ToolRegistry? _tools;

    private readonly List<ChatTurn> _transcript = new();
    private readonly object _transcriptLock = new();
    private AgentConfig _cfg;
    private string _contextBlock = "";
    private string _lastInput = "";

    public AgentCore(
        ILLMClient llm,
        SkillRouter router,
        SkillManager skills,
        MemoryManager memory,
        SqliteSessionStore sessions,
        AgentConfig cfg,
        ILogger<AgentCore> logger,
        ToolRegistry? tools = null)
    {
        _llm = llm;
        _router = router;
        _skills = skills;
        _memory = memory;
        _sessions = sessions;
        _cfg = cfg;
        _logger = logger;
        _tools = tools;
    }

    public string SessionId { get; } = Guid.NewGuid().ToString("N")[..12];
    public int CommandCount { get; private set; }

    /// <summary>Транскрипт текущей сессии (для сохранения памяти).</summary>
    public IReadOnlyList<ChatTurn> Transcript
    {
        get
        {
            lock (_transcriptLock)
            {
                return _transcript.ToList();
            }
        }
    }

    /// <summary>
    ///     Применить новую конфигурацию агента без перезагрузки.
    ///     Пороги (SkillCreationThreshold, SkillImprovementThreshold, SkillEvaluationWindow,
    ///     ReflectionEveryNCommands) и SystemPrompt обновляются сразу.
    /// </summary>
    public void Reload(AppConfig config)
    {
        _cfg = config.Agent;
    }

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
        var loopCtx = LoopContext.Initial;

        // [Loop] Step 1 — Skill routing
        _logger.LogDebug("[Loop] {Step} started (input: {InputLen} chars)", loopCtx.CurrentStep, input.Length);
        var routeSw = Stopwatch.StartNew();
        RouteResult route = _router.Route(input);
        _lastInput = input;
        var systemPrompt = BuildSystemPrompt(route.MatchedSkill);
        routeSw.Stop();
        _logger.LogDebug("[Loop] {Step} finished in {ElapsedMs}ms — skill={SkillName}",
            LoopStep.SkillRoute, routeSw.ElapsedMilliseconds, route.MatchedSkill?.Meta.Name ?? "(direct)");

        // [Loop] Step 2 — LLM call (with tool iteration)
        var messages = BuildMessages(systemPrompt, input);
        loopCtx = loopCtx with { CurrentStep = LoopStep.LlmCall };
        _logger.LogDebug("[Loop] {Step} started", loopCtx.CurrentStep);
        LlmResponse llmResp;
        string toolUsed = "";
        try
        {
            (llmResp, toolUsed, loopCtx) = await RunWithToolsAsync(messages, loopCtx, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Loop] {Step} failed: {Error}", LoopStep.LlmCall, ex.Message);
            return new AgentResponse
            {
                Answer = $"Ошибка обращения к LLM: {ex.Message}",
                Confidence = "low",
                Mode = route.IsSkill ? "skill" : "direct",
                UsedSkill = route.MatchedSkill
            };
        }

        var (answer, confidence) = ExtractConfidence(llmResp.Text);

        // [Loop] Step 3 — Transcript update
        lock (_transcriptLock)
        {
            _transcript.Add(new ChatTurn(ChatRole.User, input));
            _transcript.Add(new ChatTurn(ChatRole.Assistant, answer));
        }

        var mode = !string.IsNullOrEmpty(toolUsed)
            ? "tool"
            : route.IsSkill ? "skill" : "direct";

        // [Loop] Step 4 — Interaction log
        var logCtx = loopCtx with { CurrentStep = LoopStep.LogInteraction };
        _sessions.LogInteraction(new InteractionLog(
            SessionId, input, answer, confidence, mode,
            route.MatchedSkill?.Meta.Id, llmResp.Provider, DateTime.UtcNow));

        // Запись использования навыка (успех = уверенность не low)
        if (route.IsSkill)
        {
            _skills.RecordUsage(route.MatchedSkill!.Meta.Id, confidence != "low", confidence);
        }

        // 5-6. Пороги (skill creation/improvement)
        return BuildResponse(answer, confidence, llmResp.Provider, route.MatchedSkill, toolUsed, mode);
    }

    /// <summary>
    ///     Вызвать LLM с возможной tool-итерацией: если LLM вернул JSON с action,
    ///     выполнить tool, положить результат в transcript, вызвать LLM снова.
    ///     Max MaxToolIterations итераций (защита от infinite loops).
    ///     Контекст обновляется после каждого tool execution для observability.
    /// </summary>
    private async Task<(LlmResponse Response, string ToolUsed, LoopContext Context)> RunWithToolsAsync(
        List<ChatTurn> messages, LoopContext ctx, CancellationToken ct)
    {
        var toolUsed = "";
        LlmResponse last = default!;
        for (var iter = 0; iter <= MaxToolIterations; iter++)
        {
            last = await _llm.CompleteAsync(messages, ct);

            if (_tools is null || _tools.Names.Count == 0)
            {
                _logger.LogDebug("[Loop] {Step} finished (no tools configured)", LoopStep.LlmCall);
                return (last, toolUsed, ctx);
            }

            // Try to parse tool action from LLM output
            (string Name, string ArgsJson)? action = TryParseAction(last.Text);
            if (action is null)
            {
                _logger.LogDebug("[Loop] {Step} finished — no tool action parsed", LoopStep.LlmCall);
                return (last, toolUsed, ctx);
            }

            var (toolName, argsJson) = action.Value;
            ITool? tool = _tools.Get(toolName);
            if (tool is null)
            {
                messages.Add(new ChatTurn(ChatRole.Assistant, last.Text));
                messages.Add(new ChatTurn(ChatRole.User,
                    $"[system] Tool '{toolName}' not found. Available tools: {string.Join(", ", _tools.Names)}. " +
                    "Provide a final answer (without 'action' JSON) or use a different tool."));
                continue;
            }

            // Execute tool — log step
            toolUsed = toolName;
            ctx = ctx.AfterTool(toolName);
            _logger.LogDebug("[Loop] {Step} started — tool={ToolName} iter={Iter}",
                LoopStep.ToolExecute, toolName, iter);

            ToolResult toolResult = await tool.ExecuteAsync(argsJson, ct);
            var resultJson = JsonSerializer.Serialize(new
            {
                success = toolResult.Success,
                output = toolResult.Output,
                error = toolResult.Error
            }, ActionJsonOpts);

            _logger.LogWarning("Tool '{ToolName}' → {Status} (output: {OutputLen} chars, error: {Error_len} chars)", toolName, toolResult.Success ? "ok" : "FAIL", toolResult.Output.Length, toolResult.Error?.Length ?? 0);

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
                _logger.LogWarning("[Loop] {Step} — max iterations ({Max}) reached, stopping tool loop",
                    LoopStep.ToolExecute, MaxToolIterations);
            }
        }

        return (last, toolUsed, ctx);
    }

    private static (string Name, string ArgsJson)? TryParseAction(string llmText)
    {
        // Ищем JSON-блок с action. LLM может обернуть его в markdown ```json ... ```
        var cleaned = llmText;
        Match jsonMatch = Regex.Match(cleaned, @"```(?:json)?\s*(\{.*?\})\s*```", RegexOptions.Singleline);
        if (jsonMatch.Success)
        {
            cleaned = jsonMatch.Groups[1].Value;
        }

        Match match = ActionRx.Match(cleaned);
        if (!match.Success)
        {
            return null;
        }

        return (match.Groups["name"].Value.Trim(), match.Groups["args"].Value.Trim());
    }

    private AgentResponse BuildResponse(
        string answer, string confidence, string provider, Skill? usedSkill,
        string toolUsed, string mode)
    {
        // 5. Порог создания навыка: если однотипный запрос повторился >= SkillCreationThreshold раз,
        //    и для него ещё нет навыка (direct-режим) — предложить создать навык.
        string? proposeSkill = null;
        if (usedSkill is null && _cfg.SkillCreationThreshold > 0)
        {
            var repeatCount = _sessions.IncrementRequestCount(SkillRouter.Normalize(_lastInput));
            if (repeatCount >= _cfg.SkillCreationThreshold)
            {
                proposeSkill = _lastInput;
            }
        }

        // 6. Порог улучшения навыка: если использован навык и его success_rate
        //    упал ниже SkillImprovementThreshold — предложить улучшение.
        string? proposeImproveId = null;
        string? proposeImproveName = null;
        if (usedSkill is not null &&
            usedSkill.Meta.TotalUses >= _cfg.SkillEvaluationWindow &&
            usedSkill.Meta.SuccessRate < _cfg.SkillImprovementThreshold)
        {
            proposeImproveId = usedSkill.Meta.Id;
            proposeImproveName = usedSkill.Meta.Name;
        }

        return new AgentResponse
        {
            Answer = answer,
            Mode = mode,
            Confidence = confidence,
            Provider = provider,
            UsedSkill = usedSkill,
            ToolUsed = string.IsNullOrEmpty(toolUsed)
                ? null
                : toolUsed,
            ProposeSkillForInput = proposeSkill,
            ProposeImproveSkillId = proposeImproveId,
            ProposeImproveSkillName = proposeImproveName
        };
    }

    /// <summary>
    ///     Псевдоним для <see cref="HandleAsync" /> — используется Web API адаптером.
    /// </summary>
    public Task<AgentResponse> ProcessMessageAsync(string input, CancellationToken ct = default)
    {
        return HandleAsync(input, ct);
    }

    /// <summary>
    ///     Оценить навык на конкретном запросе: принудительно использует skill,
    ///     игнорируя роутер. Используется SkillEvaluationEngine для запуска test suite.
    /// </summary>
    /// <param name="skill">Навык для принудительного использования.</param>
    /// <param name="input">Тестовый запрос.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>AgentResponse от навыка.</returns>
    public async Task<AgentResponse> EvaluateSkillAsync(Skill skill, string input, CancellationToken ct = default)
    {
        _logger.LogDebug("[Eval] Evaluating skill '{Name}' with input: {Input}", skill.Meta.Name, input);
        CommandCount++;

        // Строим system prompt напрямую с навыком (минуя роутер)
        var systemPrompt = BuildSystemPrompt(skill);
        var messages = BuildMessages(systemPrompt, input);

        LlmResponse llmResp;
        string toolUsed = "";
        try
        {
            (llmResp, toolUsed, _) = await RunWithToolsAsync(messages, LoopContext.Initial, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Eval] LLM call failed for skill '{Name}'", skill.Meta.Name);
            return new AgentResponse
            {
                Answer = $"Ошибка LLM: {ex.Message}",
                Confidence = "low",
                Mode = "skill",
                UsedSkill = skill
            };
        }

        var (answer, confidence) = ExtractConfidence(llmResp.Text);
        var mode = !string.IsNullOrEmpty(toolUsed) ? "tool" : "skill";

        return new AgentResponse
        {
            Answer = answer,
            Mode = mode,
            Confidence = confidence,
            Provider = llmResp.Provider,
            UsedSkill = skill,
            ToolUsed = string.IsNullOrEmpty(toolUsed) ? null : toolUsed
        };
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

    /// <summary>
    ///     Завершить сессию: записать транскрипт в память и закрыть сессионное хранилище.
    /// </summary>
    public async Task EndSessionAsync(CancellationToken ct = default)
    {
        // [Loop] Memory update — сохраняем итоги сессии
        _logger.LogDebug("[Loop] {Step} started — session={SessionId}", LoopStep.MemoryUpdate, SessionId);
        await _memory.PersistSessionAsync(Transcript, ct);
        _sessions.EndSession(SessionId);
        _logger.LogDebug("[Loop] {Step} finished", LoopStep.MemoryUpdate);
    }

    /// <summary>Завершить сессию синхронно (без сохранения памяти — для совместимости).</summary>
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
        List<ChatTurn> snapshot;
        lock (_transcriptLock)
        {
            snapshot = _transcript.TakeLast(8).ToList();
        }

        var msgs = new List<ChatTurn> { new(ChatRole.System, systemPrompt) };
        msgs.AddRange(snapshot);
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
