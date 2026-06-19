using Hercules.Storage;

namespace Hercules.Agent;

// ============================================================================
//  DTO для Web API. Размещены в ядре, чтобы переиспользоваться веб-слоем
//  и сохранять единый контракт между AgentCore и HTTP-эндпоинтами.
// ============================================================================

/// <summary>Запрос чата.</summary>
public sealed record ChatRequest(string Message);

/// <summary>Ответ чата для HTTP-клиента.</summary>
public sealed record ChatResponseDto(
    string Answer,
    string Mode,
    string Confidence,
    string Provider,
    SkillDto? Skill,
    string? ProposeSkillForInput,
    string? ProposeImproveSkillId,
    string? ProposeImproveSkillName);

/// <summary>Краткое представление навыка.</summary>
public sealed record SkillDto(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<string> Triggers,
    int Version,
    double SuccessRate,
    int TotalUses,
    string CreatedAt);

/// <summary>Полное представление навыка (с prompt и описанием).</summary>
public sealed record SkillDetailDto(
    SkillDto Meta,
    string DescriptionMarkdown,
    string Prompt);

/// <summary>Запрос на создание навыка (trigger + prompt).</summary>
public sealed record CreateSkillRequest(string? Name, List<string>? Triggers, string? Trigger, string Prompt, string? Description);

/// <summary>Запрос на обновление навыка.</summary>
public sealed record UpdateSkillRequest(List<string>? Triggers, string? Prompt, string? Description);

/// <summary>Запрос на обновление профиля.</summary>
public sealed record UpdateProfileRequest(string Content);

/// <summary>Точка графика по дням.</summary>
public sealed record DailyStatDto(string Date, int Total, int Skill, int Direct);

/// <summary>Сводная статистика.</summary>
public sealed record StatsDto(
    int TotalInteractions,
    int SkillBased,
    int Direct,
    double SuccessRate,
    int TotalSkills,
    IReadOnlyList<DailyStatDto> ByDay);

/// <summary>Результат рефлексии.</summary>
public sealed record ReflectDto(string Markdown, string File);

/// <summary>
///     Адаптер между HTTP-слоем и ядром агента. Инкапсулирует AgentCore,
///     SkillManager, MemoryManager, ReflectionEngine и SqliteSessionStore,
///     предоставляя удобные методы и DTO для Web API.
///     Согласно ТЗ:
///     ChatEndpoint   → AgentCore.ProcessMessageAsync()
///     SkillsEndpoint → SkillManager
///     MemoryEndpoint → MemoryManager
/// </summary>
public sealed class WebApiAdapter(
    AgentCore agent,
    SkillManager skills,
    MemoryManager memory,
    ReflectionEngine reflection,
    SqliteSessionStore sessions)
{
    /// <summary>Инициализировать сессию агента (вызывается один раз при старте сервера).</summary>
    public void EnsureSessionStarted()
    {
        agent.StartSession();
    }

    // ---- Chat ----

    public async Task<ChatResponseDto> ChatAsync(string message, CancellationToken ct = default)
    {
        AgentResponse r = await agent.ProcessMessageAsync(message, ct);
        return new ChatResponseDto(
            r.Answer, r.Mode, r.Confidence, r.Provider,
            r.UsedSkill is null
                ? null
                : ToDto(r.UsedSkill),
            r.ProposeSkillForInput, r.ProposeImproveSkillId, r.ProposeImproveSkillName);
    }

    // ---- Skills ----

    public List<SkillDto> ListSkills()
    {
        return skills.All().Select(ToDto).ToList();
    }

    public SkillDetailDto? GetSkill(string id)
    {
        Skill? s = skills.Get(id);
        return s is null
            ? null
            : new SkillDetailDto(ToDto(s), s.Description, s.Prompt);
    }

    public SkillDto CreateSkill(CreateSkillRequest req)
    {
        List<string> triggers = req.Triggers ?? new List<string>();
        if (!string.IsNullOrWhiteSpace(req.Trigger))
        {
            triggers.Add(req.Trigger);
        }

        Skill skill = skills.CreateManual(req.Name ?? "", triggers, req.Prompt, req.Description);
        return ToDto(skill);
    }

    /// <summary>Сгенерировать навык по теме через LLM (POST /api/skills?ai=true).</summary>
    public async Task<SkillDto> CreateSkillWithLlmAsync(string topic, CancellationToken ct = default)
    {
        return ToDto(await skills.CreateAsync(topic, ct));
    }

    public SkillDto? UpdateSkill(string id, UpdateSkillRequest req)
    {
        Skill? skill = skills.UpdateManual(id, req.Triggers, req.Prompt, req.Description);
        return skill is null
            ? null
            : ToDto(skill);
    }

    /// <summary>Улучшить навык через LLM (создаёт новую версию).</summary>
    public async Task<SkillDto?> ImproveSkillAsync(string id, CancellationToken ct = default)
    {
        Skill? skill = await skills.ImproveAsync(id, ct);
        return skill is null
            ? null
            : ToDto(skill);
    }

    // ---- Memory ----

    public string GetProfile()
    {
        return memory.ProfileMarkdown;
    }

    public void UpdateProfile(string content)
    {
        memory.UpdateProfile(content);
    }

    public void ResetMemory()
    {
        memory.Reset();
    }

    // ---- Reflection ----

    public async Task<ReflectDto> ReflectAsync(CancellationToken ct = default)
    {
        ReflectionResult result = await reflection.ReflectAsync(agent.SessionId, ct);
        return new ReflectDto(result.Markdown, result.FilePath);
    }

    // ---- Stats ----

    public StatsDto GetStats()
    {
        var (skill, direct) = sessions.GetGlobalModeStats();
        var daily = sessions.GetDailyStats()
            .Select(d => new DailyStatDto(d.Date, d.Total, d.Skill, d.Direct))
            .ToList();
        return new StatsDto(
            sessions.GetTotalInteractions(),
            skill, direct,
            sessions.GetGlobalSuccessRate(),
            skills.All().Count,
            daily);
    }

    private static SkillDto ToDto(Skill s)
    {
        return new SkillDto(
            s.Meta.Id, s.Meta.Name, s.Meta.Description, s.Meta.Triggers,
            s.Meta.Version, s.Meta.SuccessRate, s.Meta.TotalUses, s.Meta.CreatedAt);
    }
}
