using System.Text;
using System.Text.RegularExpressions;
using Hercules.Config;
using Hercules.LLM;
using Hercules.Storage;

namespace Hercules.Agent;

/// <summary>Ответ агента на один запрос пользователя.</summary>
public sealed record AgentResponse
{
    public string Answer { get; init; } = "";
    public string Mode { get; init; } = "direct"; // skill | direct
    public string Confidence { get; init; } = "medium"; // high | medium | low
    public string Provider { get; init; } = "";
    public Skill? UsedSkill { get; init; }

    /// <summary>Если задан — агент предлагает создать навык (нужно подтверждение пользователя).</summary>
    public string? ProposeSkillForInput { get; init; }

    /// <summary>Если задан — агент предлагает улучшить навык с этим id.</summary>
    public string? ProposeImproveSkillId { get; init; }

    public string? ProposeImproveSkillName { get; init; }
}

/// <summary>
///     Ядро агента: реализует главный цикл обработки запроса —
///     загрузка памяти → маршрутизация навыка → вызов LLM → логирование →
///     проверка порогов создания/улучшения навыков.
/// </summary>
public sealed class AgentCore(
    ILLMClient llm,
    SkillRouter router,
    SkillManager skills,
    MemoryManager memory,
    SqliteSessionStore sessions,
    AgentConfig cfg)
{
    private static readonly Regex ConfidenceRx =
        new(@"\[confidence:\s*(high|medium|low)\s*\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly List<ChatTurn> _transcript = new();
    private string _contextBlock = "";

    public string SessionId { get; } = Guid.NewGuid().ToString("N")[..12];
    public int CommandCount { get; private set; }

    /// <summary>Транскрипт текущей сессии (для сохранения памяти).</summary>
    public IReadOnlyList<ChatTurn> Transcript => _transcript;

    /// <summary>Инициализация сессии: создать запись и загрузить контекст памяти.</summary>
    public void StartSession()
    {
        sessions.StartSession(SessionId);
        _contextBlock = memory.BuildContextBlock();
    }

    /// <summary>Обработать один запрос пользователя.</summary>
    public async Task<AgentResponse> HandleAsync(string input, CancellationToken ct = default)
    {
        CommandCount++;

        // 1-2. Маршрутизация навыка
        RouteResult route = router.Route(input);
        var systemPrompt = BuildSystemPrompt(route.MatchedSkill);

        // 3. Вызов LLM
        List<ChatTurn> messages = BuildMessages(systemPrompt, input);
        LlmResponse llm1;
        try
        {
            llm1 = await llm.CompleteAsync(messages, ct);
        }
        catch (Exception ex)
        {
            return new AgentResponse
            {
                Answer = $"Ошибка обращения к LLM: {ex.Message}",
                Confidence = "low",
                Mode = route.IsSkill
                    ? "skill"
                    : "direct",
                UsedSkill = route.MatchedSkill
            };
        }

        var (answer, confidence) = ExtractConfidence(llm1.Text);

        // Поддержка диалогового контекста (история)
        _transcript.Add(new ChatTurn(ChatRole.User, input));
        _transcript.Add(new ChatTurn(ChatRole.Assistant, answer));

        var mode = route.IsSkill
            ? "skill"
            : "direct";

        // 4. Логирование взаимодействия
        sessions.LogInteraction(new InteractionLog(
            SessionId, input, answer, confidence, mode,
            route.MatchedSkill?.Meta.Id, llm1.Provider, DateTime.UtcNow));

        // Запись использования навыка (успех = уверенность не low)
        if (route.IsSkill)
        {
            skills.RecordUsage(route.MatchedSkill!.Meta.Id, confidence != "low", confidence);
        }

        // 5. Порог создания навыка (только для direct-режима)
        string? proposeSkill = null;
        if (!route.IsSkill)
        {
            var norm = SkillRouter.Normalize(input);
            var count = sessions.IncrementRequestCount(norm);
            if (count >= cfg.SkillCreationThreshold)
            {
                proposeSkill = input;
            }
        }

        // 6. Порог улучшения навыка
        string? improveId = null, improveName = null;
        if (!route.IsSkill)
        {
            return new AgentResponse
            {
                Answer = answer,
                Mode = mode,
                Confidence = confidence,
                Provider = llm1.Provider,
                UsedSkill = route.MatchedSkill,
                ProposeSkillForInput = proposeSkill,
                ProposeImproveSkillId = improveId,
                ProposeImproveSkillName = improveName
            };
        }

        Skill? s = skills.Get(route.MatchedSkill!.Meta.Id);
        if (s is null ||
            s.Meta.TotalUses < cfg.SkillEvaluationWindow ||
            !(s.Meta.SuccessRate < cfg.SkillImprovementThreshold))
        {
            return new AgentResponse
            {
                Answer = answer,
                Mode = mode,
                Confidence = confidence,
                Provider = llm1.Provider,
                UsedSkill = route.MatchedSkill,
                ProposeSkillForInput = proposeSkill,
                ProposeImproveSkillId = improveId,
                ProposeImproveSkillName = improveName
            };
        }

        improveId = s.Meta.Id;
        improveName = s.Meta.Name;

        return new AgentResponse
        {
            Answer = answer,
            Mode = mode,
            Confidence = confidence,
            Provider = llm1.Provider,
            UsedSkill = route.MatchedSkill,
            ProposeSkillForInput = proposeSkill,
            ProposeImproveSkillId = improveId,
            ProposeImproveSkillName = improveName
        };
    }

    /// <summary>
    ///     Псевдоним для <see cref="HandleAsync" /> — используется Web API адаптером
    ///     (см. ТЗ: ChatEndpoint → AgentCore.ProcessMessageAsync()).
    /// </summary>
    public Task<AgentResponse> ProcessMessageAsync(string input, CancellationToken ct = default)
    {
        return HandleAsync(input, ct);
    }

    /// <summary>Сбросить счётчик повторов запроса (после создания навыка).</summary>
    public void ResetRequestCounter(string input)
    {
        sessions.ResetRequestCount(SkillRouter.Normalize(input));
    }

    /// <summary>Нужно ли запустить рефлексию по числу команд.</summary>
    public bool ShouldReflectByCount()
    {
        return cfg.ReflectionEveryNCommands > 0 &&
               CommandCount > 0 &&
               CommandCount % cfg.ReflectionEveryNCommands == 0;
    }

    public void EndSession()
    {
        sessions.EndSession(SessionId);
    }

    // ---- Вспомогательные методы ----

    private string BuildSystemPrompt(Skill? skill)
    {
        var sb = new StringBuilder();
        sb.AppendLine(cfg.SystemPrompt);
        sb.AppendLine();
        sb.AppendLine(_contextBlock);
        if (skill is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"=== АКТИВНЫЙ НАВЫК: {skill.Meta.Name} ===");
            sb.AppendLine(skill.Prompt);
        }

        sb.AppendLine();
        sb.AppendLine("В КОНЦЕ ответа добавь на отдельной строке маркер уверенности в формате: [confidence: high|medium|low]");
        return sb.ToString();
    }

    private List<ChatTurn> BuildMessages(string systemPrompt, string input)
    {
        var msgs = new List<ChatTurn> { new(ChatRole.System, systemPrompt) };
        // Включаем последние реплики истории для связности диалога
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
