using Hercules.Agent;
using Hercules.Audit;
using Hercules.Config;
using Hercules.Redaction;
using Hercules.Skills;
using Hercules.Skills.Eval;
using Hercules.Storage;
using Microsoft.Extensions.Logging;

namespace Hercules.Reflection;

/// <summary>
///     DI-friendly сервис safe self-improvement.
///     Объединяет MaintenanceWorkflow, ProposalStore, SkillManager, eval harness.
///     Применение всегда проходит через approval policy.
/// </summary>
public sealed class SelfImprovementService
{
    private readonly MaintenanceWorkflow _workflow;
    private readonly ProposalStore _store;
    private readonly SkillManager _skillManager;
    private readonly SkillLifecycleService _lifecycle;
    private readonly IEvalHarnessService? _harness;
    private readonly IAuditService? _audit;
    private readonly IRedactionService? _redaction;
    private readonly SelfImprovementConfig _config;
    private readonly ILogger<SelfImprovementService> _logger;

    public SelfImprovementService(
        MaintenanceWorkflow workflow,
        ProposalStore store,
        SkillManager skillManager,
        SkillLifecycleService lifecycle,
        IEvalHarnessService? harness,
        IAuditService? audit,
        IRedactionService? redaction,
        SelfImprovementConfig config,
        ILogger<SelfImprovementService> logger)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _skillManager = skillManager ?? throw new ArgumentNullException(nameof(skillManager));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _harness = harness;
        _audit = audit;
        _redaction = redaction;
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Запустить maintenance workflow и вернуть сгенерированный proposal.
    /// </summary>
    public Task<Proposal?> RunMaintenanceAsync(string skillId, string triggeredBy = "agent", CancellationToken ct = default) =>
        _workflow.RunAsync(skillId, triggeredBy, ct);

    /// <summary>
    ///     Запустить maintenance для всех навыков, требующих улучшения.
    /// </summary>
    public Task<List<Proposal>> RunMaintenanceAllAsync(string triggeredBy = "agent", CancellationToken ct = default) =>
        _workflow.RunAllAsync(triggeredBy, ct);

    /// <summary>
    ///     Применить proposal: обновить навык через SkillManager, запустить eval harness.
    ///     Если eval harness доступен — проверяет на regression.
    /// </summary>
    public async Task<ApplyResult> ApplyProposalAsync(
        string proposalId,
        string approvedBy,
        CancellationToken ct = default)
    {
        var proposal = _store.Load(proposalId);
        if (proposal is null)
        {
            return new ApplyResult { Success = false, Error = "Proposal not found" };
        }

        if (proposal.Status != ProposalStatus.Proposed && proposal.Status != ProposalStatus.Approved)
        {
            return new ApplyResult { Success = false, Error = $"Proposal already {proposal.Status}" };
        }

        var skill = _skillManager.Get(proposal.SkillId);
        if (skill is null)
        {
            return new ApplyResult { Success = false, Error = "Skill not found" };
        }

        // Build updated skill using object initializer (Skill/SkillMeta are classes, not records)
        var newVersion = skill.Meta.Version + 1;
        var updatedMeta = new SkillMeta
        {
            Id = skill.Meta.Id,
            Name = skill.Meta.Name,
            Description = skill.Meta.Description,
            PhraseReceivers = proposal.ProposedPhrases,
            Tools = skill.Meta.Tools,
            CreatedAt = skill.Meta.CreatedAt,
            Version = newVersion,
            SuccessRate = skill.Meta.SuccessRate,
            TotalUses = skill.Meta.TotalUses,
            DeprecatedAt = skill.Meta.DeprecatedAt,
            DeprecationReason = skill.Meta.DeprecationReason,
            LastEvaluationScore = skill.Meta.LastEvaluationScore
        };

        var updatedSkill = new Skill
        {
            Meta = updatedMeta,
            Description = skill.Description,
            Prompt = proposal.ProposedPrompt
        };

        RegressionResult? harnessResult = null;
        try
        {
            // Update via SkillManager
            _skillManager.Update(proposal.SkillId, updatedSkill);

            // Run eval harness if available
            string? evalResult = null;
            bool hasRegression = false;
            double newScore = proposal.PredictedScore;

            if (_harness is not null)
            {
                try
                {
                    harnessResult = await _harness.RunHarnessAsync(proposal.SkillId, ct);
                    evalResult = System.Text.Json.JsonSerializer.Serialize(harnessResult);
                    hasRegression = harnessResult.HasRegression;
                    newScore = harnessResult.CurrentScore;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Eval harness failed for proposal '{ProposalId}'", proposalId);
                }
            }

            // Update proposal status
            proposal.Status = hasRegression ? ProposalStatus.Rejected : ProposalStatus.Applied;
            proposal.ResolvedAt = DateTime.UtcNow;
            proposal.ResolvedBy = approvedBy;
            proposal.EvalResult = evalResult;

            if (hasRegression)
            {
                proposal.RejectionReason = "Regression detected by eval harness";
                // Rollback to previous version
                _skillManager.RestoreVersion(proposal.SkillId, proposal.RollbackVersion);
                _store.Save(proposal);

                var scoreMsg = harnessResult is not null
                    ? $"Regression: score dropped from {harnessResult.PreviousScore:F2} to {harnessResult.CurrentScore:F2}"
                    : "Regression detected by eval harness";
                if (_audit is not null)
                {
                    await _audit.LogSkillActionAsync(
                        approvedBy, "self_improvement_rejected", proposal.SkillId,
                        scoreMsg, null, ct);
                }

                return new ApplyResult
                {
                    Success = false,
                    Error = "Regression detected, proposal rejected and rolled back",
                    EvalResult = evalResult,
                    HasRegression = true
                };
            }

            _store.Save(proposal);

            if (_audit is not null)
            {
                await _audit.LogSkillActionAsync(
                    approvedBy, "self_improvement_applied", proposal.SkillId,
                    $"Applied v{skill.Meta.Version}->v{newVersion}, gain={proposal.ExpectedScoreGain:+0.000}",
                    null, ct);
            }

            _logger.LogInformation(
                "Applied self-improvement proposal '{ProposalId}' for skill '{SkillId}'",
                proposalId, proposal.SkillId);

            return new ApplyResult
            {
                Success = true,
                AppliedVersion = newVersion,
                EvalResult = evalResult,
                NewScore = newScore,
                ScoreGain = newScore - proposal.CurrentScore
            };
        }
        catch (Exception ex)
        {
            proposal.Status = ProposalStatus.Rejected;
            proposal.ResolvedAt = DateTime.UtcNow;
            proposal.ResolvedBy = approvedBy;
            proposal.RejectionReason = ex.Message;
            _store.Save(proposal);

            _logger.LogError(ex, "Failed to apply proposal '{ProposalId}'", proposalId);
            return new ApplyResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    ///     Отклонить proposal.
    /// </summary>
    public bool RejectProposal(string proposalId, string rejectedBy, string? reason = null)
    {
        var proposal = _store.Load(proposalId);
        if (proposal is null)
        {
            return false;
        }

        if (proposal.Status is ProposalStatus.Applied or ProposalStatus.Rejected)
        {
            return false;
        }

        proposal.Status = ProposalStatus.Rejected;
        proposal.ResolvedAt = DateTime.UtcNow;
        proposal.ResolvedBy = rejectedBy;
        proposal.RejectionReason = reason;
        _store.Save(proposal);

        _logger.LogInformation(
            "Rejected proposal '{ProposalId}' for skill '{SkillId}' by {By}",
            proposalId, proposal.SkillId, rejectedBy);

        return true;
    }

    /// <summary>
    ///     Вернуть все proposals.
    /// </summary>
    public List<Proposal> GetProposals(int limit = 50) => _store.GetRecent(limit);

    /// <summary>
    ///     Вернуть proposals для навыка.
    /// </summary>
    public List<Proposal> GetProposalsForSkill(string skillId) => _store.GetBySkill(skillId);

    /// <summary>
    ///     Получить один proposal.
    /// </summary>
    public Proposal? GetProposal(string proposalId) => _store.Load(proposalId);
}

/// <summary>
///     Результат применения proposal.
/// </summary>
public sealed class ApplyResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int? AppliedVersion { get; set; }
    public string? EvalResult { get; set; }
    public bool HasRegression { get; set; }
    public double NewScore { get; set; }
    public double ScoreGain { get; set; }
}
