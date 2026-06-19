using System.Text.Json;
using Hercules.Config;
using Hercules.LLM;
using Hercules.Storage;

namespace Hercules.Agent;

/// <summary>
///     CRUD навыков и их версионирование. Генерирует описание + system prompt навыка
///     с помощью LLM, фиксирует использование и инициирует улучшение версий.
/// </summary>
public sealed class SkillManager(FileSkillRepository repo, ILLMClient llm, AgentConfig cfg)
{
    public List<Skill> All()
    {
        return repo.LoadAll();
    }

    public Skill? Get(string id)
    {
        return repo.Load(id);
    }

    /// <summary>
    ///     Создать новый навык: LLM генерирует имя, описание, триггеры и system prompt
    ///     на основе примера запроса пользователя.
    /// </summary>
    public async Task<Skill> CreateAsync(string topicOrExample, CancellationToken ct = default)
    {
        var prompt = $$"""
                       Создай описание навыка ассистента на основе запроса пользователя.
                       Верни СТРОГО валидный JSON без пояснений и markdown-блоков:
                       {
                         "name": "краткое название навыка",
                         "description": "что делает навык и когда вызывать (1-2 предложения)",
                         "triggers": ["ключевое-слово1", "ключевое-слово2", "ключевое-слово3"],
                         "prompt": "system prompt, который задаёт ассистенту роль и инструкции для этой задачи"
                       }

                       Запрос/тема пользователя: "{{topicOrExample}}"
                       """;

        LlmResponse resp = await llm.CompleteAsync([
            new ChatTurn(ChatRole.System, "Ты — конструктор навыков. Возвращаешь только JSON."),
            new ChatTurn(ChatRole.User, prompt)
        ], ct);

        (var name, var desc, List<string> triggers, var sysPrompt) = ParseSkillJson(resp.Text, topicOrExample);

        var skill = new Skill
        {
            Meta = new SkillMeta
            {
                Name = name,
                Description = desc,
                Triggers = triggers
            },
            Description = $"# {name}\n\n{desc}\n\n## Когда вызывать\nТриггеры: {string.Join(", ", triggers)}\n",
            Prompt = sysPrompt
        };
        repo.Save(skill);
        return skill;
    }

    /// <summary>Улучшить навык: LLM создаёт новую версию с учётом прошлых неудач.</summary>
    public async Task<Skill?> ImproveAsync(string id, CancellationToken ct = default)
    {
        Skill? skill = repo.Load(id);
        if (skill is null)
        {
            return null;
        }

        List<SkillUsage> usages = repo.LoadUsages(id);
        var fails = usages.TakeLast(cfg.SkillEvaluationWindow).Count(u => !u.Success);

        var prompt = $$"""
                       Улучши навык ассистента. Текущая версия работает недостаточно хорошо
                       (неудачных использований за последнее время: {{fails}}).

                       Текущее описание:
                       {{skill.Description}}

                       Текущий system prompt:
                       {{skill.Prompt}}

                       Верни СТРОГО валидный JSON:
                       {
                         "description": "улучшенное markdown-описание навыка",
                         "prompt": "улучшенный system prompt (более точный, с учётом возможных ошибок)"
                       }
                       """;

        LlmResponse resp = await llm.CompleteAsync([
            new ChatTurn(ChatRole.System, "Ты — оптимизатор навыков. Возвращаешь только JSON."),
            new ChatTurn(ChatRole.User, prompt)
        ], ct);

        var (newDesc, newPrompt) = ParseImproveJson(resp.Text, skill);
        repo.SaveNewVersion(skill, newDesc, newPrompt);
        return skill;
    }

    /// <summary>Зафиксировать использование навыка и пересчитать метрики.</summary>
    public void RecordUsage(string id, bool success, string confidence)
    {
        repo.AppendUsage(id, new SkillUsage { Success = success, Confidence = confidence }, cfg.SkillEvaluationWindow);
    }

    /// <summary>
    ///     Создать навык вручную из явных данных (без обращения к LLM).
    ///     Используется Web API: POST /api/skills (trigger + prompt).
    /// </summary>
    public Skill CreateManual(string name, IEnumerable<string> triggers, string prompt, string? description = null)
    {
        var triggerList = triggers
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();
        if (triggerList.Count == 0)
        {
            throw new ArgumentException("Нужен хотя бы один триггер.", nameof(triggers));
        }

        var safeName = string.IsNullOrWhiteSpace(name)
            ? triggerList[0]
            : name.Trim();
        var desc = string.IsNullOrWhiteSpace(description)
            ? $"Навык по триггерам: {string.Join(", ", triggerList)}"
            : description.Trim();

        var skill = new Skill
        {
            Meta = new SkillMeta
            {
                Name = safeName,
                Description = desc,
                Triggers = triggerList
            },
            Description = $"# {safeName}\n\n{desc}\n\n## Когда вызывать\nТриггеры: {string.Join(", ", triggerList)}\n",
            Prompt = string.IsNullOrWhiteSpace(prompt)
                ? $"Ты — ассистент, специализирующийся на задаче: {safeName}."
                : prompt.Trim()
        };
        repo.Save(skill);
        return skill;
    }

    /// <summary>
    ///     Обновить навык вручную, создавая новую версию (старые версии сохраняются).
    ///     Используется Web API: PUT /api/skills/{id}. Любой параметр опционален.
    /// </summary>
    public Skill? UpdateManual(string id, IEnumerable<string>? triggers, string? prompt, string? description)
    {
        Skill? skill = repo.Load(id);
        if (skill is null)
        {
            return null;
        }

        if (triggers is not null)
        {
            var list = triggers.Select(t => t.Trim().ToLowerInvariant())
                .Where(t => t.Length > 0).Distinct().ToList();
            if (list.Count > 0)
            {
                skill.Meta.Triggers = list;
            }
        }

        var newDesc = string.IsNullOrWhiteSpace(description)
            ? skill.Description
            : description.Trim();
        var newPrompt = string.IsNullOrWhiteSpace(prompt)
            ? skill.Prompt
            : prompt.Trim();

        repo.SaveNewVersion(skill, newDesc, newPrompt);
        return skill;
    }

    /// <summary>Навыки, которые стоит улучшить (success_rate ниже порога при достаточном числе использований).</summary>
    public List<Skill> SkillsNeedingImprovement()
    {
        return repo.LoadAll()
            .Where(s => s.Meta.TotalUses >= cfg.SkillEvaluationWindow && s.Meta.SuccessRate < cfg.SkillImprovementThreshold)
            .ToList();
    }

    // ---- Парсинг JSON-ответов LLM (с защитой от лишнего текста) ----

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start
            ? text[start..(end + 1)]
            : "{}";
    }

    private static (string, string, List<string>, string) ParseSkillJson(string text, string fallbackTopic)
    {
        try
        {
            using var doc = JsonDocument.Parse(ExtractJson(text));
            JsonElement root = doc.RootElement;
            var name = root.TryGetProperty("name", out JsonElement n)
                ? n.GetString() ?? fallbackTopic
                : fallbackTopic;
            var desc = root.TryGetProperty("description", out JsonElement d)
                ? d.GetString() ?? ""
                : "";
            List<string> triggers = root.TryGetProperty("triggers", out JsonElement t) && t.ValueKind == JsonValueKind.Array
                ? t.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList()
                : [];
            var prompt = root.TryGetProperty("prompt", out JsonElement p)
                ? p.GetString() ?? ""
                : "";
            if (triggers.Count == 0)
            {
                triggers.Add(fallbackTopic.ToLowerInvariant());
            }

            if (string.IsNullOrWhiteSpace(prompt))
            {
                prompt = $"Ты — ассистент, специализирующийся на задаче: {fallbackTopic}. Отвечай чётко и по делу.";
            }

            return (name, desc, triggers, prompt);
        }
        catch
        {
            return (fallbackTopic, $"Навык по теме: {fallbackTopic}",
                [fallbackTopic.ToLowerInvariant()],
                $"Ты — ассистент по задаче: {fallbackTopic}.");
        }
    }

    private static (string, string) ParseImproveJson(string text, Skill current)
    {
        try
        {
            using var doc = JsonDocument.Parse(ExtractJson(text));
            JsonElement root = doc.RootElement;
            var desc = root.TryGetProperty("description", out JsonElement d)
                ? d.GetString() ?? current.Description
                : current.Description;
            var prompt = root.TryGetProperty("prompt", out JsonElement p)
                ? p.GetString() ?? current.Prompt
                : current.Prompt;
            return (desc, prompt);
        }
        catch
        {
            return (current.Description, current.Prompt);
        }
    }
}
