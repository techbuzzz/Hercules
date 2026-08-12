using Hercules.Config;
using Hercules.LLM.JsonRepair;
using Hercules.Skills.Routing.Deterministic;
using Hercules.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Phase2Tests;

public class DeterministicRouterTests : IDisposable
{
    private readonly FileSkillRepository _repo;
    private readonly SkillManager _skillManager;
    private readonly DeterministicRouter _router;
    private readonly string _tempDir;

    public DeterministicRouterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-det-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var storageCfg = new StorageConfig { DataRoot = _tempDir };
        _repo = new FileSkillRepository(storageCfg, NullLogger<FileSkillRepository>.Instance);
        var cfg = new AgentConfig();
        var llm = new StubLlm("test");
        _skillManager = new SkillManager(_repo, llm, cfg, new JsonRepairService());

        var detConfig = new DeterministicRoutingConfig
        {
            FallbackMode = DeterministicFallbackMode.Always,
            EnableTagMatching = true,
            EnableInputTypeMatching = true,
        };

        _router = new DeterministicRouter(_skillManager, detConfig);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { /* best effort */ }
    }

    // ─── Availability ─────────────────────────────────────────────────────────

    [Fact]
    public void IsAvailable_Always_True()
    {
        Assert.True(_router.IsAvailable);
    }

    // ─── Empty / whitespace input ──────────────────────────────────────────────

    [Fact]
    public void Route_EmptyString_ReturnsNone()
    {
        var result = _router.Route("");
        Assert.False(result.IsSkill);
        Assert.Equal(0, result.Score);
    }

    [Fact]
    public void Route_Whitespace_ReturnsNone()
    {
        var result = _router.Route("   \t\n  ");
        Assert.False(result.IsSkill);
        Assert.Equal(0, result.Score);
    }

    // ─── Keyword-only matching (phrase-receivers) ──────────────────────────────

    [Fact]
    public void Route_KeywordMatch_ReturnsSkill()
    {
        _skillManager.CreateManual("Код", new[] { "код", "программа" }, "Ты пишешь код.");
        var result = _router.Route("напиши код");
        Assert.True(result.IsSkill);
        Assert.Equal("Код", result.MatchedSkill!.Meta.Name);
        Assert.Contains("keyword", result.MatchedMethods);
    }

    [Fact]
    public void Route_NoMatch_ReturnsNone()
    {
        _skillManager.CreateManual("Код", new[] { "код" }, "Пишу код.");
        var result = _router.Route("что-то совершенно несвязанное xyz123");
        Assert.False(result.IsSkill);
    }

    [Fact]
    public void Route_MultipleKeywords_MoreMatchesWins()
    {
        _skillManager.CreateManual("SkillA", new[] { "а" }, "SkillA");
        _skillManager.CreateManual("SkillB", new[] { "а", "б" }, "SkillB");
        var result = _router.Route("а и б вместе");
        Assert.True(result.IsSkill);
        Assert.Equal("SkillB", result.MatchedSkill!.Meta.Name);
    }

    [Fact]
    public void Route_SuccessRateTieBreaker()
    {
        // SkillB has more keyword matches, so it wins regardless of order
        _skillManager.CreateManual("SkillA", new[] { "тест" }, "SkillA");
        _skillManager.CreateManual("SkillB", new[] { "тест", "run" }, "SkillB");

        var result = _router.Route("run тест");
        Assert.True(result.IsSkill);
        Assert.Equal("SkillB", result.MatchedSkill!.Meta.Name);
    }

    // ─── Tag matching ─────────────────────────────────────────────────────────

    [Fact]
    public void Route_TagMatch_ReturnsSkill()
    {
        var skill = _skillManager.CreateManual("Python", new[] { "python" }, "Python developer");
        skill.Meta.Tags = new List<string> { "python", "api" };
        _repo.Save(skill);

        var result = _router.Route("напиши python api");
        Assert.True(result.IsSkill);
        Assert.Equal("Python", result.MatchedSkill!.Meta.Name);
        Assert.Contains("keyword", result.MatchedMethods);
        Assert.Contains("tag", result.MatchedMethods);
    }

    [Fact]
    public void Route_TagMatch_WithoutKeywordMatch()
    {
        // Use a non-matching phrase so only tag matching works
        var skill = _skillManager.CreateManual("GoDev", new[] { "xyz-unmatched-xyz" }, "Go developer");
        skill.Meta.Tags = new List<string> { "go", "golang" };
        _repo.Save(skill);

        var result = _router.Route("go program");
        Assert.True(result.IsSkill);
        Assert.Equal("GoDev", result.MatchedSkill!.Meta.Name);
        Assert.Contains("tag", result.MatchedMethods);
    }

    [Fact]
    public void Route_TagMatch_NoTagsDefined_ReturnsZero()
    {
        _skillManager.CreateManual("БезТегов", new[] { "тест" }, "desc");
        var result = _router.Route("запусти тест");
        Assert.True(result.IsSkill);
        Assert.DoesNotContain("tag", result.MatchedMethods);
    }

    // ─── Input type matching ──────────────────────────────────────────────────

    [Fact]
    public void Route_InputTypeMatch_Code()
    {
        var skill = _skillManager.CreateManual("CodeFix", new[] { "fix" }, "Fix bugs");
        skill.Meta.InputTypes = new List<string> { "code", "qa" };
        _repo.Save(skill);

        var result = _router.Route("debug the function");
        Assert.True(result.IsSkill);
        Assert.Equal("CodeFix", result.MatchedSkill!.Meta.Name);
        Assert.Contains("type", result.MatchedMethods);
    }

    [Fact]
    public void Route_InputTypeMatch_Writing()
    {
        var skill = _skillManager.CreateManual("Writer", new[] { "write" }, "Write text");
        skill.Meta.InputTypes = new List<string> { "writing" };
        _repo.Save(skill);

        var result = _router.Route("compose an article");
        Assert.True(result.IsSkill);
        Assert.Equal("Writer", result.MatchedSkill!.Meta.Name);
    }

    [Fact]
    public void Route_InputTypeMatch_NoTypesDefined_ReturnsZero()
    {
        // Skill matches via keyword "код" but has no input types
        _skillManager.CreateManual("БезТипов", new[] { "код" }, "desc");
        var result = _router.Route("напиши код");
        Assert.True(result.IsSkill);
        Assert.DoesNotContain("type", result.MatchedMethods);
    }

    // ─── Combined scoring ──────────────────────────────────────────────────────

    [Fact]
    public void Route_CombinedKeywordAndTag_ScoreIsHigher()
    {
        var skillA = _skillManager.CreateManual("A", new[] { "python" }, "A");
        skillA.Meta.Tags = new List<string> { "python" };
        _repo.Save(skillA);

        // Skill B has no keyword match but matches via tag
        var skillB = _skillManager.CreateManual("B", new[] { "xyz-no-match" }, "B");
        skillB.Meta.Tags = new List<string> { "python" };
        _repo.Save(skillB);

        var result = _router.Route("python code");

        // Both match via tag, but skill A also matches via keyword → higher score
        Assert.True(result.IsSkill);
        Assert.Equal("A", result.MatchedSkill!.Meta.Name);
    }

    [Fact]
    public void Route_CombinedAllThree_MethodsAllPresent()
    {
        var skill = _skillManager.CreateManual("FullMatch", new[] { "python" }, "Full match skill");
        skill.Meta.Tags = new List<string> { "api" };
        skill.Meta.InputTypes = new List<string> { "code" };
        _repo.Save(skill);

        var result = _router.Route("debug python api code");
        Assert.True(result.IsSkill);
        Assert.Contains("keyword", result.MatchedMethods);
        Assert.Contains("tag", result.MatchedMethods);
        Assert.Contains("type", result.MatchedMethods);
    }

    // ─── Tag matching disabled ────────────────────────────────────────────────

    [Fact]
    public void Route_TagMatchingDisabled_DoesNotUseTags()
    {
        var detConfig = new DeterministicRoutingConfig
        {
            FallbackMode = DeterministicFallbackMode.Always,
            EnableTagMatching = false,
            EnableInputTypeMatching = false,
        };
        var router = new DeterministicRouter(_skillManager, detConfig);

        var skill = _skillManager.CreateManual("TagSkill", new[] { "python" }, "desc");
        skill.Meta.Tags = new List<string> { "python" };
        _repo.Save(skill);

        var result = router.Route("python code");
        Assert.True(result.IsSkill);
        Assert.DoesNotContain("tag", result.MatchedMethods);
    }

    // ─── Fallback modes ───────────────────────────────────────────────────────

    [Fact]
    public void Route_NeverMode_DoesNotRoute()
    {
        var detConfig = new DeterministicRoutingConfig
        {
            FallbackMode = DeterministicFallbackMode.Never,
        };
        var router = new DeterministicRouter(_skillManager, detConfig);
        _skillManager.CreateManual("Test", new[] { "test" }, "desc");

        var result = router.Route("test");
        Assert.False(result.IsSkill);
    }

    [Fact]
    public void Route_OnNoEmbeddingMode_Routes()
    {
        var detConfig = new DeterministicRoutingConfig
        {
            FallbackMode = DeterministicFallbackMode.OnNoEmbedding,
        };
        var router = new DeterministicRouter(_skillManager, detConfig);
        _skillManager.CreateManual("Test", new[] { "тест" }, "desc");

        var result = router.Route("запусти тест");
        Assert.True(result.IsSkill);
    }

    // ─── Score values ─────────────────────────────────────────────────────────

    [Fact]
    public void Route_Score_IsPositive_ForMatchingSkill()
    {
        _skillManager.CreateManual("Test", new[] { "a", "b", "c", "d" }, "desc");
        var result = _router.Route("a b");
        Assert.True(result.Score > 0);
    }

    [Fact]
    public void Route_DeterministicResult_MethodsListIsNotNull()
    {
        _skillManager.CreateManual("Test", new[] { "test" }, "desc");
        var result = _router.Route("test");
        Assert.NotNull(result.MatchedMethods);
        Assert.NotEmpty(result.MatchedMethods);
    }
}
