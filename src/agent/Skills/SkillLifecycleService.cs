using Hercules.Agent;
using Hercules.Config;
using Hercules.Skills.Eval;
using Hercules.Skills.Quality;
using Hercules.Storage;

namespace Hercules.Skills;

/// <summary>
///     DI-friendly сервис жизненного цикла навыков: объединяет SkillManager,
///     SkillDeprecationManager, SkillLifecyclePolicy и SkillEvaluationEngine.
///     Проверяет policy перед HIGH/MEDIUM_RISK действиями.
/// </summary>
public sealed class SkillLifecycleService
{
    private readonly SkillManager _manager;
    private readonly SkillDeprecationManager _deprecation;
    private readonly SkillLifecyclePolicy _policy;
    private readonly SkillEvaluationEngine _evaluator;
    private readonly EvalConfig _evalConfig;
    private readonly IEvalHarnessService? _harness;
    private readonly ISkillQualityService? _qualityService;
    private readonly SkillQualityConfig? _qualityConfig;

    public SkillLifecycleService(
        SkillManager manager,
        SkillDeprecationManager deprecation,
        SkillLifecyclePolicy policy,
        SkillEvaluationEngine evaluator,
        EvalConfig? evalConfig = null,
        IEvalHarnessService? harness = null,
        ISkillQualityService? qualityService = null,
        SkillQualityConfig? qualityConfig = null)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _deprecation = deprecation ?? throw new ArgumentNullException(nameof(deprecation));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _evalConfig = evalConfig ?? new EvalConfig();
        _harness = harness;
        _qualityService = qualityService;
        _qualityConfig = qualityConfig;
    }

    /// <summary>
    ///     Проверить, требуется ли approval для улучшения навыка.
    /// </summary>
    public PolicyCheckResult CheckImprovePolicy(string skillId)
    {
        var skill = _manager.Get(skillId);
        return _policy.RequiresApproval(skill, SkillAction.Improve);
    }

    /// <summary>
    ///     Проверить, требуется ли approval для депрекации навыка.
    /// </summary>
    public PolicyCheckResult CheckDeprecatePolicy(string skillId)
    {
        var skill = _manager.Get(skillId);
        return _policy.RequiresApproval(skill, SkillAction.Deprecate);
    }

    /// <summary>
    ///     Проверить, требуется ли approval для отката навыка.
    /// </summary>
    public PolicyCheckResult CheckRollbackPolicy(string skillId)
    {
        var skill = _manager.Get(skillId);
        return _policy.RequiresApproval(skill, SkillAction.Rollback);
    }

    /// <summary>
    ///     Запустить оценку навыка (всегда Low risk, без approval).
    /// </summary>
    public Task<SkillEvaluationResult> EvaluateAsync(string skillId, CancellationToken ct = default) =>
        _manager.EvaluateAsync(skillId, _evaluator, ct);

    /// <summary>
    ///     Запустить оценку навыка с явным test suite.
    /// </summary>
    public Task<SkillEvaluationResult> EvaluateAsync(
        string skillId, SkillTestSuite suite, CancellationToken ct = default) =>
        _manager.EvaluateAsync(skillId, suite, _evaluator, ct);

    /// <summary>
    ///     Депрецировать навык. Если HIGH_RISK — выбрасывает ApprovalRequiredException.
    /// </summary>
    public Skill? Deprecate(string skillId, string reason)
    {
        var policy = CheckDeprecatePolicy(skillId);
        if (policy.NeedsHumanApproval)
        {
            throw new ApprovalRequiredException(SkillAction.Deprecate, policy.Risk, policy.Reason);
        }

        return _deprecation.Deprecate(skillId, reason);
    }

    /// <summary>
    ///     Откатить навык. Если HIGH_RISK — выбрасывает ApprovalRequiredException.
    /// </summary>
    public Skill? Rollback(string skillId)
    {
        var policy = CheckRollbackPolicy(skillId);
        if (policy.NeedsHumanApproval)
        {
            throw new ApprovalRequiredException(SkillAction.Rollback, policy.Risk, policy.Reason);
        }

        return _deprecation.Rollback(skillId);
    }

    /// <summary>
    ///     Снять deprecated-статус.
    /// </summary>
    public Skill? Undeprecate(string skillId) => _deprecation.Undeprecate(skillId);

    /// <summary>
    ///     Список deprecated-навыков.
    /// </summary>
    public List<Skill> GetDeprecated() => _deprecation.GetDeprecated();

    /// <summary>
    ///     Удалить навык (с backup).
    /// </summary>
    public bool Delete(string skillId) => _manager.Delete(skillId);

    /// <summary>
    ///     Запустить eval harness с regression check.
    ///     Вызывается после promotion навыка. Если BlockOnRegression=true и обнаружена
    ///     регрессия — откатывает изменения и кидает RegressionBlockedException.
    ///     Task 029: также проверяет quality score против MinScoreForPromotion.
    /// </summary>
    public async Task<RegressionResult> EvalHarnessAsync(string skillId, CancellationToken ct = default)
    {
        if (_harness is null)
        {
            return new RegressionResult
            {
                HasRegression = false,
                ScoreDelta = 0,
                PreviousScore = 0,
                CurrentScore = 0,
                EvaluatedAt = DateTime.UtcNow.ToString("o")
            };
        }

        // Check quality score before promotion (task_029)
        if (_qualityService is not null && _qualityConfig is not null)
        {
            var skill = _manager.Get(skillId);
            if (skill is not null)
            {
                var qScore = await _qualityService.ComputeScoreAsync(skillId, skill.Meta.Version, ct);
                if (qScore.IsReliable && qScore.CompositeScore < _qualityConfig.MinScoreForPromotion)
                {
                    return new RegressionResult
                    {
                        HasRegression = true,
                        ScoreDelta = qScore.CompositeScore,
                        PreviousScore = 0,
                        CurrentScore = qScore.CompositeScore,
                        BlockedReasons = new List<RegressionDetail>
                            { new RegressionDetail
                                {
                                    TestName = "skill_quality_score",
                                    PreviousScore = 0,
                                    CurrentScore = qScore.CompositeScore,
                                    Reason = $"Quality score {qScore.CompositeScore:F2} < MinScoreForPromotion {_qualityConfig.MinScoreForPromotion}"
                                }
                            },
                        EvaluatedAt = DateTime.UtcNow.ToString("o")
                    };
                }
            }
        }

        var result = await _harness.RunHarnessAsync(skillId, ct);

        if (result.HasRegression && _evalConfig.BlockOnRegression)
        {
            // Откатываем к предыдущей версии
            _deprecation.Rollback(skillId);
            throw new RegressionBlockedException(skillId, result);
        }

        return result;
    }
}

/// <summary>
///     Исключение: действие требует human-gate подтверждения.
/// </summary>
public sealed class ApprovalRequiredException : Exception
{
    public SkillAction Action { get; }
    public SkillActionRisk Risk { get; }

    public ApprovalRequiredException(SkillAction action, SkillActionRisk risk, string? reason)
        : base($"Action '{action}' ({risk}) requires approval: {reason ?? "(no reason)"}")
    {
        Action = action;
        Risk = risk;
    }
}

/// <summary>
///     Исключение: автоматическая регрессия обнаружена и изменения откачены.
///     Кидается из SkillLifecycleService.EvalHarnessAsync когда BlockOnRegression=true.
/// </summary>
public sealed class RegressionBlockedException : Exception
{
    public string SkillId { get; }
    public RegressionResult Result { get; }

    public RegressionBlockedException(string skillId, RegressionResult result)
        : base($"Regression detected for skill '{skillId}': score dropped from {result.PreviousScore:F2} to {result.CurrentScore:F2} (delta={result.ScoreDelta:F2}). Rollback performed.")
    {
        SkillId = skillId;
        Result = result;
    }
}
