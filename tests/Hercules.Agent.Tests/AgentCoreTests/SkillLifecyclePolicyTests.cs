using Hercules.Skills;
using Hercules.Storage;
using Xunit;

namespace Hercules.Agent.Tests.AgentCoreTests;

/// <summary>
/// Тесты SkillLifecyclePolicy: risk levels и RequiresApproval.
/// </summary>
public class SkillLifecyclePolicyTests
{
    private readonly SkillLifecyclePolicy _policy = new();

    // ---- Base risk levels ----

    [Theory]
    [InlineData(SkillAction.Create, SkillActionRisk.Low, false)]
    [InlineData(SkillAction.CreateWithAi, SkillActionRisk.Medium, true)]
    [InlineData(SkillAction.Update, SkillActionRisk.Low, false)]
    [InlineData(SkillAction.Evaluate, SkillActionRisk.Low, false)]
    public void BaseRiskLevels_are_correct(SkillAction action, SkillActionRisk expectedRisk, bool expectApproval)
    {
        var result = _policy.RequiresApproval(null, action);
        Assert.Equal(expectedRisk, result.Risk);
        Assert.Equal(expectApproval, result.NeedsHumanApproval);
    }

    [Theory]
    [InlineData(SkillAction.Deprecate, SkillActionRisk.High)]
    [InlineData(SkillAction.Rollback, SkillActionRisk.High)]
    [InlineData(SkillAction.Delete, SkillActionRisk.High)]
    public void HighRiskActions_require_approval(SkillAction action, SkillActionRisk expectedRisk)
    {
        var skill = CreateSkill(successRate: 0.9);
        var result = _policy.RequiresApproval(skill, action);
        Assert.Equal(expectedRisk, result.Risk);
        Assert.True(result.NeedsHumanApproval);
        Assert.NotNull(result.Reason);
    }

    // ---- Improve risk depends on SuccessRate ----

    [Fact]
    public void Improve_HighRisk_when_SuccessRate_below_0_4()
    {
        var skill = CreateSkill(successRate: 0.3);
        var result = _policy.RequiresApproval(skill, SkillAction.Improve);
        Assert.Equal(SkillActionRisk.High, result.Risk);
        Assert.True(result.NeedsHumanApproval);
    }

    [Fact]
    public void Improve_MediumRisk_when_SuccessRate_between_0_4_and_0_6()
    {
        var skill = CreateSkill(successRate: 0.5);
        var result = _policy.RequiresApproval(skill, SkillAction.Improve);
        Assert.Equal(SkillActionRisk.Medium, result.Risk);
        Assert.True(result.NeedsHumanApproval);
    }

    [Fact]
    public void Improve_LowRisk_when_SuccessRate_above_0_6()
    {
        var skill = CreateSkill(successRate: 0.7);
        var result = _policy.RequiresApproval(skill, SkillAction.Improve);
        Assert.Equal(SkillActionRisk.Low, result.Risk);
        Assert.False(result.NeedsHumanApproval);
    }

    [Fact]
    public void Improve_HighRisk_at_exact_0_4_boundary()
    {
        var skill = CreateSkill(successRate: 0.4);
        var result = _policy.RequiresApproval(skill, SkillAction.Improve);
        Assert.Equal(SkillActionRisk.High, result.Risk);
        Assert.True(result.NeedsHumanApproval);
    }

    // ---- Deprecated skill raises risk ----

    [Fact]
    public void Improve_on_deprecated_skill_is_HighRisk()
    {
        var skill = CreateSkill(successRate: 0.8);
        skill.Meta.DeprecatedAt = DateTime.UtcNow.ToString("o");
        skill.Meta.DeprecationReason = "replaced";
        var result = _policy.RequiresApproval(skill, SkillAction.Improve);
        Assert.Equal(SkillActionRisk.High, result.Risk);
        Assert.Contains("deprecated", result.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rollback_on_deprecated_skill_is_HighRisk()
    {
        var skill = CreateSkill(successRate: 0.9);
        skill.Meta.DeprecatedAt = DateTime.UtcNow.ToString("o");
        var result = _policy.RequiresApproval(skill, SkillAction.Rollback);
        Assert.Equal(SkillActionRisk.High, result.Risk);
        Assert.True(result.NeedsHumanApproval);
    }

    [Fact]
    public void Update_on_deprecated_skill_is_HighRisk()
    {
        var skill = CreateSkill(successRate: 0.9);
        skill.Meta.DeprecatedAt = DateTime.UtcNow.ToString("o");
        var result = _policy.RequiresApproval(skill, SkillAction.Update);
        Assert.Equal(SkillActionRisk.High, result.Risk);
        Assert.True(result.NeedsHumanApproval);
    }

    // ---- Static helper ----

    [Fact]
    public void GetBaseRisk_Low_for_Create_and_Update()
    {
        Assert.Equal(SkillActionRisk.Low, SkillLifecyclePolicy.GetBaseRisk(SkillAction.Create));
        Assert.Equal(SkillActionRisk.Low, SkillLifecyclePolicy.GetBaseRisk(SkillAction.Update));
        Assert.Equal(SkillActionRisk.Low, SkillLifecyclePolicy.GetBaseRisk(SkillAction.Evaluate));
    }

    [Fact]
    public void GetBaseRisk_High_for_Deprecate_Rollback_Delete()
    {
        Assert.Equal(SkillActionRisk.High, SkillLifecyclePolicy.GetBaseRisk(SkillAction.Deprecate));
        Assert.Equal(SkillActionRisk.High, SkillLifecyclePolicy.GetBaseRisk(SkillAction.Rollback));
        Assert.Equal(SkillActionRisk.High, SkillLifecyclePolicy.GetBaseRisk(SkillAction.Delete));
    }

    [Fact]
    public void GetBaseRisk_Medium_for_CreateWithAi()
    {
        Assert.Equal(SkillActionRisk.Medium, SkillLifecyclePolicy.GetBaseRisk(SkillAction.CreateWithAi));
    }

    private static Skill CreateSkill(double successRate = 0.9)
    {
        return new Skill
        {
            Meta = new SkillMeta
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Name = "test-skill",
                SuccessRate = successRate,
                TotalUses = 10
            },
            Description = "Test skill",
            Prompt = "Ты — тестовый ассистент."
        };
    }
}
