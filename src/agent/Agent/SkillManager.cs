using System.Text.Json;
using Hercules.Config;
using Hercules.Contracts;
using Hercules.LLM;
using Hercules.LLM.JsonRepair;
using Hercules.Skills;
using Hercules.Storage;

namespace Hercules.Agent;

/// <summary>
///     CRUD навыков и их версионирование. Генерирует описание + system prompt навыка
///     с помощью LLM, фиксирует использование и инициирует улучшение версий.
/// </summary>
public sealed class SkillManager(
    FileSkillRepository repo,
    ILLMClient llm,
    AgentConfig cfg,
    IJsonRepairService jsonRepair) : IConfigReload
{
    private AgentConfig _cfg = cfg;
    private readonly IJsonRepairService _jsonRepair = jsonRepair;

    /// <summary>
    ///     Применить новую конфигурацию агента без перезагрузки.
    ///     Пороги оценки и улучшения навыков обновляются сразу.
    /// </summary>
    public void Reload(AppConfig config)
    {
        _cfg = config.Agent;
    }

    public List<Skill> All()
    {
        return repo.LoadAll();
    }

    public Skill? Get(string id)
    {
        return repo.Load(id);
    }

    /// <summary>
    ///     Создать новый навык: LLM генерирует имя, описание, фразы-приёмники и system prompt
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
                         "phrase_receivers": ["фраза-приёмник1", "фраза-приёмник2", "фраза-приёмник3"],
                         "prompt": "system prompt, который задаёт ассистенту роль и инструкции для этой задачи"
                       }

                       Пояснение: phrase_receivers — это ключевые слова/фразы, по которым агент-роутер
                       определяет, что пользователь обращается именно к этому навыку (например,
                       "напиши пост", "составь резюме", "объясни код").

                       Запрос/тема пользователя: "{{topicOrExample}}"
                       """;

        LlmResponse resp = await llm.CompleteAsync([
            new ChatTurn(ChatRole.System, "Ты — конструктор навыков. Возвращаешь только JSON."),
            new ChatTurn(ChatRole.User, prompt)
        ], ct);

        (var name, var desc, List<string> receivers, var sysPrompt) = ParseSkillJson(resp.Text, topicOrExample);

        var skill = new Skill
        {
            Meta = new SkillMeta
            {
                Name = name,
                Description = desc,
                PhraseReceivers = receivers
            },
            Description = $"# {name}\n\n{desc}\n\n## Когда вызывать\nФразы-приёмники: {string.Join(", ", receivers)}\n",
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
        var fails = usages.TakeLast(_cfg.SkillEvaluationWindow).Count(u => !u.Success);

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
        repo.AppendUsage(id, new SkillUsage { Success = success, Confidence = confidence }, _cfg.SkillEvaluationWindow);
    }

    /// <summary>Зафиксировать использование навыка с latency и пересчитать метрики (task_022).</summary>
    public void RecordUsage(string id, bool success, string confidence, int latencyMs)
    {
        repo.AppendUsage(id, new SkillUsage { Success = success, Confidence = confidence, LatencyMs = latencyMs }, _cfg.SkillEvaluationWindow);
    }

    /// <summary>Получить среднюю latency навыка из usage history (task_022).</summary>
    public double GetAverageLatency(string id)
    {
        var usages = repo.LoadUsages(id);
        var withLatency = usages.Where(u => u.LatencyMs > 0).ToList();
        return withLatency.Count > 0 ? withLatency.Average(u => u.LatencyMs) : 0;
    }

    /// <summary>
    ///     Создать навык вручную из явных данных (без обращения к LLM).
    ///     Используется Web API: POST /api/skills (phraseReceivers + prompt).
    ///     Принимает новые PhraseReceivers и legacy Triggers (для обратной совместимости).
    /// </summary>
    public Skill CreateManual(
        string name,
        IEnumerable<string>? phraseReceivers,
        string prompt,
        string? description = null,
        IEnumerable<string>? triggers = null)
    {
        List<string> receiverList = NormalizeReceivers(phraseReceivers, triggers);
        if (receiverList.Count == 0)
        {
            throw new ArgumentException(
                "Нужна хотя бы одна фраза-приёмник (phrase_receivers) или триггер (triggers).",
                nameof(phraseReceivers));
        }

        var safeName = string.IsNullOrWhiteSpace(name)
            ? receiverList[0]
            : name.Trim();
        var desc = string.IsNullOrWhiteSpace(description)
            ? $"Навык по фразам-приёмникам: {string.Join(", ", receiverList)}"
            : description.Trim();

        var skill = new Skill
        {
            Meta = new SkillMeta
            {
                Name = safeName,
                Description = desc,
                PhraseReceivers = receiverList
            },
            Description = $"# {safeName}\n\n{desc}\n\n## Когда вызывать\nФразы-приёмники: {string.Join(", ", receiverList)}\n",
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
    ///     Принимает новые PhraseReceivers и legacy Triggers (для обратной совместимости).
    /// </summary>
    public Skill? UpdateManual(
        string id,
        IEnumerable<string>? phraseReceivers,
        string? prompt,
        string? description,
        IEnumerable<string>? triggers = null)
    {
        Skill? skill = repo.Load(id);
        if (skill is null)
        {
            return null;
        }

        if (phraseReceivers is not null || triggers is not null)
        {
            List<string> list = NormalizeReceivers(phraseReceivers, triggers);
            if (list.Count > 0)
            {
                skill.Meta.PhraseReceivers = list;
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

    /// <summary>
    ///     Нормализовать список фраз-приёмников: lowercase, trim, distinct.
    ///     Приоритет у phraseReceivers; legacy triggers используются, если phraseReceivers пуст.
    /// </summary>
    private static List<string> NormalizeReceivers(
        IEnumerable<string>? phraseReceivers,
        IEnumerable<string>? triggers)
    {
        var primary = phraseReceivers?
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();

        if (primary is { Count: > 0 })
        {
            return primary;
        }

        return triggers?
                   .Select(t => t.Trim().ToLowerInvariant())
                   .Where(t => t.Length > 0)
                   .Distinct()
                   .ToList() ??
               [];
    }

    /// <summary>Навыки, которые стоит улучшить (success_rate ниже порога при достаточном числе использований).</summary>
    public List<Skill> SkillsNeedingImprovement()
    {
        return SkillsNeedingImprovement(_cfg.SkillImprovementThreshold);
    }

    /// <summary>
    ///     Навыки, которые стоит улучшить (custom threshold).
    ///     Используется MaintenanceWorkflow с его собственным порогом.
    /// </summary>
    public List<Skill> SkillsNeedingImprovement(double threshold)
    {
        return repo.LoadAll()
            .Where(s => s.Meta.TotalUses >= _cfg.SkillEvaluationWindow && s.Meta.SuccessRate < threshold)
            .ToList();
    }

    /// <summary>
    ///     Обновить навык (для self-improvement). Сохраняет текущую версию в историю
    ///     и создаёт новую (SaveNewVersion semantics: version++, description→v{N}.md).
    /// </summary>
    public Skill? Update(string id, Skill updated)
    {
        Skill? current = repo.Load(id);
        if (current is null)
        {
            return null;
        }

        repo.SaveNewVersion(current, updated.Description, updated.Prompt);
        return repo.Load(id);
    }

    /// <summary>
    ///     Восстановить предыдущую версию навыка (для rollback).
    ///     Копирует content из skill.{id}.v{version}.md → skill.{id}.md
    ///     и skill.{id}.v{version}.prompt.md → skill.{id}.prompt.md.
    /// </summary>
    public Skill? RestoreVersion(string id, int version)
    {
        var versionDescPath = Path.Combine(repo.SkillsDirectory, $"skill.{id}.v{version}.md");
        var versionPromptPath = Path.Combine(repo.SkillsDirectory, $"skill.{id}.v{version}.prompt.md");

        if (!File.Exists(versionDescPath))
        {
            return null;
        }

        var currentDescPath = Path.Combine(repo.SkillsDirectory, $"skill.{id}.md");
        var currentPromptPath = Path.Combine(repo.SkillsDirectory, $"skill.{id}.prompt.md");

        File.Copy(versionDescPath, currentDescPath, overwrite: true);
        if (File.Exists(versionPromptPath))
        {
            File.Copy(versionPromptPath, currentPromptPath, overwrite: true);
        }

        // Update meta: rollback version number
        var skill = repo.Load(id);
        if (skill is not null)
        {
            skill.Meta.Version = version;
            repo.Save(skill);
        }

        return repo.Load(id);
    }

    /// <summary>
    ///     Запустить оценку навыка через SkillEvaluationEngine.
    ///     После оценки обновляет Meta.LastEvaluationScore.
    /// </summary>
    public async Task<SkillEvaluationResult> EvaluateAsync(
        string id,
        SkillTestSuite? testSuite,
        SkillEvaluationEngine engine,
        CancellationToken ct = default)
    {
        Skill? skill = repo.Load(id);
        if (skill is null)
        {
            return new SkillEvaluationResult
            {
                SkillId = id,
                SkillName = "(unknown)",
                TotalTests = 0,
                PassedTests = 0,
                FailedTests = 0,
                Score = 0,
                TestResults = [],
                EvaluatedAt = DateTime.UtcNow.ToString("o"),
                Error = $"Навык '{id}' не найден."
            };
        }

        var result = await engine.EvaluateAsync(skill, testSuite, ct);

        // Записываем LastEvaluationScore в meta.json
        skill.Meta.LastEvaluationScore = result.Score;
        repo.Save(skill);

        return result;
    }

    /// <summary>
    ///     Запустить оценку навыка, загружая test suite из skill.{id}.tests.json.
    /// </summary>
    public async Task<SkillEvaluationResult> EvaluateAsync(
        string id,
        SkillEvaluationEngine engine,
        CancellationToken ct = default)
    {
        var testSuite = SkillEvaluationEngine.LoadTestSuite(id, repo.SkillsDirectory);
        return await EvaluateAsync(id, testSuite, engine, ct);
    }

    /// <summary>
    ///     Удалить навык (создаёт backup перед удалением).
    /// </summary>
    public bool Delete(string id)
    {
        Skill? skill = repo.Load(id);
        if (skill is null)
        {
            return false;
        }

        // Backup: перемещаем meta.json в .deleted
        var metaPath = Path.Combine(repo.SkillsDirectory, $"skill.{id}.meta.json");
        var backupPath = Path.Combine(repo.SkillsDirectory, $"skill.{id}.deleted.{DateTime.UtcNow:yyyyMMddHHmmss}.json");
        if (File.Exists(metaPath))
        {
            File.Move(metaPath, backupPath);
        }

        // Остальные файлы не удаляем — можно восстановить вручную
        return true;
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

    private (string, string, List<string>, string) ParseSkillJson(string text, string fallbackTopic)
    {
        // Try typed contract first (preferred path)
        SkillCreationContract? contract = _jsonRepair.TryParse<SkillCreationContract>(text);
        if (contract is not null)
        {
            var receivers = contract.PhraseReceivers.Count > 0
                ? contract.PhraseReceivers
                : contract.Triggers ?? [];

            if (receivers.Count == 0)
            {
                receivers.Add(fallbackTopic.ToLowerInvariant());
            }

            var prompt = string.IsNullOrWhiteSpace(contract.Prompt)
                ? $"Ты — ассистент, специализирующийся на задаче: {fallbackTopic}. Отвечай чётко и по делу."
                : contract.Prompt;

            return (
                string.IsNullOrWhiteSpace(contract.Name) ? fallbackTopic : contract.Name,
                string.IsNullOrWhiteSpace(contract.Description) ? "" : contract.Description,
                receivers,
                prompt);
        }

        // Fallback: legacy JsonDocument parse
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

            // Приоритет: phrase_receivers (новое имя). Fallback: triggers (legacy).
            List<string> receivers = ReadReceivers(root, out List<string> legacyTriggers);
            if (receivers.Count == 0 && legacyTriggers.Count > 0)
            {
                receivers = legacyTriggers;
            }

            var prompt = root.TryGetProperty("prompt", out JsonElement p)
                ? p.GetString() ?? ""
                : "";
            if (receivers.Count == 0)
            {
                receivers.Add(fallbackTopic.ToLowerInvariant());
            }

            if (string.IsNullOrWhiteSpace(prompt))
            {
                prompt = $"Ты — ассистент, специализирующийся на задаче: {fallbackTopic}. Отвечай чётко и по делу.";
            }

            return (name, desc, receivers, prompt);
        }
        catch
        {
            return (fallbackTopic, $"Навык по теме: {fallbackTopic}",
                [fallbackTopic.ToLowerInvariant()],
                $"Ты — ассистент по задаче: {fallbackTopic}.");
        }
    }

    /// <summary>
    ///     Прочитать фразы-приёмники из JSON-рутов LLM-ответа.
    ///     Сначала пробует "phrase_receivers" (новое имя), затем "triggers" (legacy).
    /// </summary>
    private static List<string> ReadReceivers(JsonElement root, out List<string> legacyTriggers)
    {
        legacyTriggers = [];

        if (root.TryGetProperty("phrase_receivers", out JsonElement pr) && pr.ValueKind == JsonValueKind.Array)
        {
            var list = pr.EnumerateArray()
                .Select(e => e.GetString() ?? "")
                .Where(s => s.Length > 0)
                .ToList();
            if (list.Count > 0)
            {
                return list;
            }
        }

        if (root.TryGetProperty("triggers", out JsonElement tr) && tr.ValueKind == JsonValueKind.Array)
        {
            legacyTriggers = tr.EnumerateArray()
                .Select(e => e.GetString() ?? "")
                .Where(s => s.Length > 0)
                .ToList();
        }

        return [];
    }

    private (string, string) ParseImproveJson(string text, Skill current)
    {
        // Try typed contract first
        SkillImproveContract? contract = _jsonRepair.TryParse<SkillImproveContract>(text);
        if (contract is not null)
        {
            return (
                string.IsNullOrWhiteSpace(contract.Description) ? current.Description : contract.Description,
                string.IsNullOrWhiteSpace(contract.Prompt) ? current.Prompt : contract.Prompt);
        }

        // Fallback: legacy JsonDocument parse
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
