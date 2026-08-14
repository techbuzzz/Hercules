using System.Text;
using Hercules.Agent;
using Hercules.Config;
using Hercules.LLM;
using Hercules.Redaction;
using Hercules.Skills;
using Hercules.Storage;
using Microsoft.Extensions.Logging;

namespace Hercules.Reflection;

/// <summary>
///     Maintenance workflow: анализирует анонимизированные failures и evaluations,
///     генерирует versioned skill diff с ожидаемым gain и rollback plan.
///     Не активирует изменения без approval.
/// </summary>
public sealed class MaintenanceWorkflow
{
    private readonly ILLMClient _llm;
    private readonly SkillManager _skillManager;
    private readonly ProposalDiffer _differ;
    private readonly ProposalStore _store;
    private readonly IRedactionService? _redaction;
    private readonly SelfImprovementConfig _config;
    private readonly ILogger<MaintenanceWorkflow> _logger;

    public MaintenanceWorkflow(
        ILLMClient llm,
        SkillManager skillManager,
        ProposalDiffer differ,
        ProposalStore store,
        IRedactionService? redaction,
        SelfImprovementConfig config,
        ILogger<MaintenanceWorkflow> logger)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _skillManager = skillManager ?? throw new ArgumentNullException(nameof(skillManager));
        _differ = differ ?? throw new ArgumentNullException(nameof(differ));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _redaction = redaction;
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Запустить maintenance analysis для конкретного навыка.
    ///     Возвращает Proposal или null если навык не требует улучшения.
    /// </summary>
    public async Task<Proposal?> RunAsync(string skillId, string triggeredBy = "agent", CancellationToken ct = default)
    {
        // Check if self-improvement is enabled
        if (!_config.Enabled)
        {
            _logger.LogInformation("Self-improvement is disabled (SelfImprovementConfig.Enabled=false)");
            return null;
        }

        // Check daily proposal limit
        if (_config.MaxProposalsPerDay > 0 && _store.CountToday() >= _config.MaxProposalsPerDay)
        {
            _logger.LogWarning("Daily proposal limit reached ({Limit}), skipping analysis",
                _config.MaxProposalsPerDay);
            return null;
        }

        // Load skill
        var skill = _skillManager.Get(skillId);
        if (skill is null)
        {
            _logger.LogWarning("MaintenanceWorkflow: skill '{SkillId}' not found", skillId);
            return null;
        }

        // Check if improvement is warranted
        if (skill.Meta.SuccessRate >= _config.MinSuccessRateThreshold)
        {
            _logger.LogDebug(
                "Skill '{SkillId}' success_rate={Rate} >= threshold={Threshold}, no improvement needed",
                skillId, skill.Meta.SuccessRate, _config.MinSuccessRateThreshold);
            return null;
        }

        // Gather analysis context
        var context = await BuildAnalysisContextAsync(skill, ct);
        if (context is null)
        {
            _logger.LogWarning("MaintenanceWorkflow: could not build context for skill '{SkillId'", skillId);
            return null;
        }

        // Run LLM analysis
        var analysis = await AnalyzeAsync(context, ct);
        if (analysis is null)
        {
            _logger.LogWarning("MaintenanceWorkflow: LLM analysis failed for skill '{SkillId'", skillId);
            return null;
        }

        // Parse proposed changes
        var proposedPrompt = analysis.TryGetPrompt(out var p) ? p : skill.Prompt;
        var proposedPhrases = analysis.TryGetPhrases(out var phrases) ? phrases : skill.Meta.PhraseReceivers;

        // Compute diff
        var diff = _differ.ComputeDiff(skill, proposedPrompt, proposedPhrases);

        // Estimate gain
        double predictedScore = _differ.EstimateScoreGain(diff, skill.Meta.SuccessRate);
        double gain = predictedScore - skill.Meta.SuccessRate;

        _logger.LogInformation(
            "MaintenanceWorkflow: generated proposal for '{SkillId}' v{Current}→v{Next}, " +
            "expected gain={Gain:+0.000}",
            skillId, diff.CurrentVersion, diff.NextVersion, gain);

        // Build proposal
        var proposal = new Proposal
        {
            SkillId = skillId,
            SkillName = skill.Meta.Name,
            CurrentVersion = diff.CurrentVersion,
            RollbackVersion = diff.RollbackVersion,
            AnalysisSummary = analysis.Summary,
            ProposedPrompt = proposedPrompt,
            ProposedPhrases = proposedPhrases,
            RemovedPhrases = diff.RemovedPhrases,
            ExpectedScoreGain = gain,
            CurrentScore = skill.Meta.SuccessRate,
            PredictedScore = predictedScore,
            TriggeredBy = triggeredBy,
            Status = ProposalStatus.Proposed
        };

        // Persist
        _store.Save(proposal);

        return proposal;
    }

    /// <summary>
    ///     Запустить maintenance для всех навыков, требующих улучшения.
    /// </summary>
    public async Task<List<Proposal>> RunAllAsync(string triggeredBy = "agent", CancellationToken ct = default)
    {
        var needsImprove = _skillManager.SkillsNeedingImprovement(_config.MinSuccessRateThreshold);
        var proposals = new List<Proposal>();

        foreach (var skill in needsImprove)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            var proposal = await RunAsync(skill.Meta.Id, triggeredBy, ct);
            if (proposal is not null)
            {
                proposals.Add(proposal);
            }
        }

        return proposals;
    }

    private async Task<AnalysisResult?> BuildAnalysisContextAsync(Skill skill, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Skill Analysis Request");
        sb.AppendLine();
        sb.AppendLine($"Skill: {skill.Meta.Name} (ID: {skill.Meta.Id})");
        sb.AppendLine($"Version: {skill.Meta.Version}");
        sb.AppendLine($"Success Rate: {skill.Meta.SuccessRate:P0}");
        sb.AppendLine($"Total Uses: {skill.Meta.TotalUses}");
        sb.AppendLine();
        sb.AppendLine("## Current Prompt:");
        sb.AppendLine(skill.Prompt);
        sb.AppendLine();
        sb.AppendLine("## Current Phrase Receivers:");
        foreach (var phrase in skill.Meta.PhraseReceivers)
        {
            sb.AppendLine($"- {phrase}");
        }
        sb.AppendLine();
        sb.AppendLine("## Suggested improvements:");

        // Anonymize context if required
        var context = sb.ToString();
        if (_config.AnonymizeData && _redaction is not null)
        {
            context = _redaction.Redact(context, sensitivity: "high");
        }

        return new AnalysisResult(context, skill);
    }

    private async Task<SkillImprovementAnalysis?> AnalyzeAsync(AnalysisResult context, CancellationToken ct)
    {
        var jsonTemplate = "{{ \"summary\": \"краткое резюме анализа (1-3 предложения)\", \"proposed_prompt\": \"улучшенный system prompt\", \"proposed_phrases\": [\"список\", \"новых\", \"phrase_receivers\"] }}";
        var prompt = $"""
                      Проведи анализ навыка и предложи улучшения. Верни результат в формате JSON:

                      {jsonTemplate}

                      Текущее состояние:
                      {context.Context}

                      Требования:
                      - proposed_prompt должен содержать улучшенную версию текущего prompt
                      - добавляй новые phrase_receivers только если они действительно нужны
                      - не удаляй существующие phrase_receivers без веской причины
                      - JSON должен быть корректным (без markdown-обёрток)
                      """;

        try
        {
            LlmResponse resp = await _llm.CompleteAsync(
                LLM.Roles.Main,
                [
                    new LLM.ChatTurn(LLM.ChatRole.System,
                        "Ты — эксперт по улучшению AI-навыков. Анализируй навыки и предлагай concrete улучшения."),
                    new LLM.ChatTurn(LLM.ChatRole.User, prompt)
                ],
                ct);

            return SkillImprovementAnalysis.Parse(resp.Text, context.Skill);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM analysis failed for skill '{SkillId}'", context.Skill.Meta.Id);
            return null;
        }
    }

    private sealed record AnalysisResult(string Context, Skill Skill);
}

/// <summary>
///     Результат LLM-анализа улучшения навыка.
/// </summary>
internal sealed class SkillImprovementAnalysis
{
    public string Summary { get; set; } = "";
    public string ProposedPrompt { get; set; } = "";
    public List<string> ProposedPhrases { get; set; } = new();

    public bool TryGetPrompt(out string prompt)
    {
        prompt = ProposedPrompt;
        return !string.IsNullOrWhiteSpace(prompt);
    }

    public bool TryGetPhrases(out List<string> phrases)
    {
        phrases = ProposedPhrases;
        return phrases.Count > 0;
    }

    public static SkillImprovementAnalysis? Parse(string llmText, Skill currentSkill)
    {
        if (string.IsNullOrWhiteSpace(llmText))
        {
            return null;
        }

        // Try to extract JSON from markdown or raw text
        var json = ExtractJson(llmText);
        if (json is null)
        {
            return null;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            var analysis = new SkillImprovementAnalysis
            {
                Summary = GetString(root, "summary"),
                ProposedPrompt = GetString(root, "proposed_prompt", currentSkill.Prompt),
                ProposedPhrases = GetStringList(root, "proposed_phrases")
            };

            // Fallback: if no phrases suggested, keep current
            if (analysis.ProposedPhrases.Count == 0)
            {
                analysis.ProposedPhrases = currentSkill.Meta.PhraseReceivers.ToList();
            }

            return analysis;
        }
        catch
        {
            return new SkillImprovementAnalysis
            {
                Summary = llmText.Length > 200 ? llmText[..200] + "…" : llmText,
                ProposedPrompt = currentSkill.Prompt,
                ProposedPhrases = currentSkill.Meta.PhraseReceivers.ToList()
            };
        }
    }

    private static string? ExtractJson(string text)
    {
        // Strip markdown code fences
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            var start = trimmed.IndexOf('\n');
            var end = trimmed.LastIndexOf("```");
            if (start >= 0 && end > start)
            {
                return trimmed[(start + 1)..end].Trim();
            }
        }

        if (trimmed.StartsWith("```"))
        {
            var start = trimmed.IndexOf('\n');
            var end = trimmed.LastIndexOf("```");
            if (start >= 0 && end > start)
            {
                return trimmed[(start + 1)..end].Trim();
            }
        }

        // Try to find first { ... } or [ ... ]
        int braceStart = trimmed.IndexOf('{');
        if (braceStart >= 0)
        {
            int braceEnd = trimmed.LastIndexOf('}');
            if (braceEnd > braceStart)
            {
                return trimmed[braceStart..(braceEnd + 1)];
            }
        }

        return null;
    }

    private static string GetString(System.Text.Json.JsonElement root, string key, string fallback = "")
    {
        if (root.TryGetProperty(key, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            return prop.GetString() ?? fallback;
        }
        return fallback;
    }

    private static List<string> GetStringList(System.Text.Json.JsonElement root, string key)
    {
        if (root.TryGetProperty(key, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            return prop.EnumerateArray()
                .Where(e => e.ValueKind == System.Text.Json.JsonValueKind.String)
                .Select(e => e.GetString() ?? "")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }
        return new List<string>();
    }
}
