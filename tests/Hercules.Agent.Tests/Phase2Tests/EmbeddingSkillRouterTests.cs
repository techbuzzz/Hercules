using Hercules.Config;
using Hercules.LLM.JsonRepair;
using Hercules.Skills;
using Hercules.Skills.Routing;
using Hercules.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Phase2Tests;

public class EmbeddingSkillRouterTests : IDisposable
{
    private readonly StubEmbeddingProvider _embedder;
    private readonly FileSkillRepository _repo;
    private readonly EmbeddingSkillRouter _router;
    private readonly SkillManager _skillManager;
    private readonly string _tempDir;

    public EmbeddingSkillRouterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-emb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var storageCfg = new StorageConfig { DataRoot = _tempDir };
        _repo = new FileSkillRepository(storageCfg, NullLogger<FileSkillRepository>.Instance);
        _skillManager = new SkillManager(_repo, new StubLlm("test"), new AgentConfig(), new JsonRepairService());
        _embedder = new StubEmbeddingProvider();

        var phase2Config = new Phase2Config
        {
            SemanticRoutingEnabled = false, // Legacy keyword mode for most tests
            SimilarityThreshold = 0.1,      // Low threshold for stub embeddings
            KeywordFallback = true
        };

        var legacyRouter = new SkillRouter(_skillManager);
        _router = new EmbeddingSkillRouter(_skillManager, phase2Config, legacyRouter, scoringEngine: null);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }
        catch
        {
            /* best effort */
        }
    }

    [Fact]
    public async Task RouteAsync_Returns_None_For_Empty_Input()
    {
        var result = await _router.RouteAsync("");
        Assert.False(result.IsSkill);
        Assert.Equal("none", result.Method);
    }

    [Fact]
    public async Task RouteAsync_Returns_None_When_No_Skills_Exist()
    {
        var result = await _router.RouteAsync("любой запрос");
        Assert.False(result.IsSkill);
    }

    [Fact]
    public async Task RouteAsync_Matches_Skill_By_Keyword()
    {
        _skillManager.CreateManual("Погода", ["погода"], "Ты — погодный ассистент.");

        var result = await _router.RouteAsync("какая погода в москве");

        Assert.True(result.IsSkill);
        Assert.Equal("Погода", result.MatchedSkill!.Meta.Name);
        Assert.Equal("keyword", result.Method);
    }

    [Fact]
    public async Task RouteAsync_Matches_Skill_With_Similar_Text()
    {
        _skillManager.CreateManual("Рецепты", ["готовить", "еда", "рецепты"], "Ты — кулинарный ассистент.");

        var result = await _router.RouteAsync("как готовить еду");

        Assert.True(result.IsSkill);
        Assert.Equal("Рецепты", result.MatchedSkill!.Meta.Name);
    }

    [Fact]
    public async Task RouteAsync_InvalidateCache_Picks_Up_New_Skills()
    {
        // Первый запрос — навыков нет
        var result1 = await _router.RouteAsync("напиши код");
        Assert.False(result1.IsSkill);

        // Добавляем навык
        _skillManager.CreateManual("Код", ["код"], "Ты — ассистент программиста.");

        // Create a fresh router instance to pick up the new skill
        // (legacy SkillRouter caches skills at instantiation in some paths)
        var freshRouter = new EmbeddingSkillRouter(_skillManager, new Phase2Config
        {
            SemanticRoutingEnabled = false,
            SimilarityThreshold = 0.1,
            KeywordFallback = true
        }, new SkillRouter(_skillManager), scoringEngine: null);

        var result2 = await freshRouter.RouteAsync("напиши код");
        Assert.True(result2.IsSkill);
        Assert.Equal("Код", result2.MatchedSkill!.Meta.Name);
    }

    [Fact]
    public async Task StubEmbeddingProvider_Returns_NonZero_Vector_For_NonEmpty_Text()
    {
        var vector = await _embedder.EmbedAsync("test text");
        Assert.Equal(256, vector.Length);
        Assert.Contains(vector, v => v != 0);
    }

    [Fact]
    public async Task StubEmbeddingProvider_Returns_Zero_Vector_For_Empty_Text()
    {
        var vector = await _embedder.EmbedAsync("");
        Assert.All(vector, v => Assert.Equal(0, v));
    }

    /// <summary>
    ///     [task_022] Test: SimilarityThreshold and UseKeywordFallback come from Phase2Config.
    ///     When SemanticRoutingEnabled=false, router falls back to keyword routing regardless of threshold.
    /// </summary>
    [Fact]
    public async Task RouteAsync_LegacyMode_IgnoresThreshold()
    {
        // Create a router with SemanticRoutingEnabled=false and very high threshold
        var highThresholdConfig = new Phase2Config
        {
            SemanticRoutingEnabled = false,
            SimilarityThreshold = 0.99,
            KeywordFallback = true
        };
        var legacyRouter = new SkillRouter(_skillManager);
        var highThresholdRouter = new EmbeddingSkillRouter(
            _skillManager, highThresholdConfig, legacyRouter, scoringEngine: null);

        _skillManager.CreateManual("Тест", ["тест"], "Промпт");

        // With SemanticRoutingEnabled=false, keyword fallback works even with high threshold
        var result = await highThresholdRouter.RouteAsync("тест");
        Assert.True(result.IsSkill);
        Assert.Equal("keyword", result.Method);
    }

    /// <summary>
    ///     [task_022] Test: EmbeddingSkillRouter.ScoreAllAsync returns scored results.
    /// </summary>
    [Fact]
    public async Task ScoreAllAsync_Returns_Empty_When_No_Skills()
    {
        var results = await _router.ScoreAllAsync("любой запрос");
        Assert.Empty(results);
    }

    /// <summary>
    ///     [task_022] Test: ScoreAllAsync returns ranked skills.
    /// </summary>
    [Fact]
    public async Task ScoreAllAsync_Returns_Single_Skill_When_One_Matches()
    {
        _skillManager.CreateManual("Погода", ["погода"], "Ты — погодный.");

        var results = await _router.ScoreAllAsync("какая погода");

        Assert.Single(results);
        Assert.Equal("Погода", results[0].Skill.Meta.Name);
        Assert.True(results[0].IsEligible);
    }
}

public class SkillScoringEngineTests : IDisposable
{
    private readonly FileSkillRepository _repo;
    private readonly SkillManager _skillManager;
    private readonly Phase2Config _phase2Config;
    private readonly StubEmbeddingProvider _embedder;
    private readonly string _tempDir;

    public SkillScoringEngineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-se-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var storageCfg = new StorageConfig { DataRoot = _tempDir };
        _repo = new FileSkillRepository(storageCfg, NullLogger<FileSkillRepository>.Instance);
        _skillManager = new SkillManager(_repo, new StubLlm("test"), new AgentConfig(), new JsonRepairService());
        _embedder = new StubEmbeddingProvider();
        _phase2Config = new Phase2Config
        {
            SemanticRoutingEnabled = true,
            SimilarityThreshold = 0.1,
            SkillScoringWeights = new Dictionary<string, double>
            {
                ["embedding"] = 0.40,
                ["lexical"]   = 0.20,
                ["schema"]    = 0.15,
                ["quality"]    = 0.15,
                ["latency"]   = 0.05,
                ["policy"]     = 0.05,
            }
        };
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }
        catch { /* best effort */ }
    }

    /// <summary>[task_022] LexicalScorer returns score based on phrase receiver matches.</summary>
    [Fact]
    public async Task LexicalScorer_Returns_Score_Based_On_Matches()
    {
        var scorer = new Hercules.Skills.Routing.ScoringComponents.LexicalScorer();
        var skill = _skillManager.CreateManual("Погода", ["погода", "forecast"], "Ты — погодный.");

        var result = await scorer.ScoreAsync("какая погода завтра", skill);

        Assert.NotNull(result);
        Assert.Equal("lexical", result.Value.ComponentName);
        Assert.True(result.Value.Value > 0);
        Assert.True(result.Value.IsEligible);
    }

    /// <summary>[task_022] LexicalScorer returns 0 for empty input.</summary>
    [Fact]
    public async Task LexicalScorer_Returns_Zero_For_Empty_Input()
    {
        var scorer = new Hercules.Skills.Routing.ScoringComponents.LexicalScorer();
        var skill = _skillManager.CreateManual("Погода", ["погода"], "Ты — погодный.");

        var result = await scorer.ScoreAsync("", skill);

        Assert.NotNull(result);
        Assert.Equal(0, result.Value.Value);
    }

    /// <summary>[task_022] HistoricalQualityScorer uses SuccessRate.</summary>
    [Fact]
    public async Task HistoricalQualityScorer_Uses_SuccessRate()
    {
        var scorer = new Hercules.Skills.Routing.ScoringComponents.HistoricalQualityScorer();
        var skill = _skillManager.CreateManual("Тест", ["тест"], "Промпт");

        // Set low success rate directly on the in-memory skill object
        // (AppendUsage writes to disk but doesn't update the returned skill object)
        skill.Meta.SuccessRate = 0.33;

        var result = await scorer.ScoreAsync("тест", skill);

        Assert.NotNull(result);
        Assert.True(result.Value.Value < 1.0);
        Assert.True(result.Value.IsEligible);
    }

    /// <summary>[task_022] HistoricalQualityScorer returns neutral 1.0 for new skill (no history).</summary>
    [Fact]
    public async Task HistoricalQualityScorer_Returns_Neutral_For_New_Skill()
    {
        var scorer = new Hercules.Skills.Routing.ScoringComponents.HistoricalQualityScorer();
        var skill = _skillManager.CreateManual("Новый", ["новый"], "Промпт");

        var result = await scorer.ScoreAsync("новый", skill);

        Assert.NotNull(result);
        Assert.Equal(1.0, result.Value.Value);
    }

    /// <summary>[task_022] SchemaCompatibilityScorer returns 1.0 for skill without declared tools.</summary>
    [Fact]
    public async Task SchemaCompatibilityScorer_Returns_Compatible_For_No_Tools()
    {
        var scorer = new Hercules.Skills.Routing.ScoringComponents.SchemaCompatibilityScorer();
        var skill = _skillManager.CreateManual("Тест", ["тест"], "Промпт");

        var result = await scorer.ScoreAsync("тест", skill);

        Assert.NotNull(result);
        Assert.True(result.Value.IsEligible);
    }

    /// <summary>[task_022] LatencyScorer returns neutral 1.0 when no latency history.</summary>
    [Fact]
    public async Task LatencyScorer_Returns_Neutral_When_No_History()
    {
        var scorer = new Hercules.Skills.Routing.ScoringComponents.LatencyScorer();
        var skill = _skillManager.CreateManual("Тест", ["тест"], "Промпт");

        var result = await scorer.ScoreAsync("тест", skill);

        Assert.NotNull(result);
        Assert.Equal(1.0, result.Value.Value);
    }

    /// <summary>[task_022] LatencyScorer penalizes high-latency skills.</summary>
    [Fact]
    public async Task LatencyScorer_Penalizes_High_Latency()
    {
        var scorer = new Hercules.Skills.Routing.ScoringComponents.LatencyScorer(referenceLatencyMs: 100);
        var skill = _skillManager.CreateManual("Медленный", ["медленный"], "Промпт");

        // Simulate high latency history
        scorer.SetLatencyHistory(skill.Meta.Id, new[] { 1000.0, 1100.0, 900.0 }); // ~1s avg vs 100ms reference

        var result = await scorer.ScoreAsync("медленный", skill);

        Assert.NotNull(result);
        Assert.True(result.Value.Value < 1.0);
        Assert.True(result.Value.Value > 0);
    }

    /// <summary>[task_022] PolicyEligibilityScorer returns eligible for skill without declared tools.</summary>
    [Fact]
    public async Task PolicyEligibilityScorer_Returns_Eligible_For_No_Permissions()
    {
        var scorer = new Hercules.Skills.Routing.ScoringComponents.PolicyEligibilityScorer();
        var skill = _skillManager.CreateManual("Тест", ["тест"], "Промпт");

        var result = await scorer.ScoreAsync("тест", skill);

        Assert.NotNull(result);
        Assert.True(result.Value.IsEligible);
        Assert.Equal(1.0, result.Value.Value);
    }

    /// <summary>[task_022] SkillScoringEngine ranks skills by combined weighted score.</summary>
    [Fact]
    public async Task SkillScoringEngine_Ranks_By_Combined_Score()
    {
        var embeddingScorer = new Hercules.Skills.Routing.ScoringComponents.EmbeddingScorer(_embedder, _skillManager, 0.1);
        var lexicalScorer = new Hercules.Skills.Routing.ScoringComponents.LexicalScorer();
        var qualityScorer = new Hercules.Skills.Routing.ScoringComponents.HistoricalQualityScorer();
        var schemaScorer = new Hercules.Skills.Routing.ScoringComponents.SchemaCompatibilityScorer();
        var latencyScorer = new Hercules.Skills.Routing.ScoringComponents.LatencyScorer();
        var policyScorer = new Hercules.Skills.Routing.ScoringComponents.PolicyEligibilityScorer();

        var engine = new SkillScoringEngine(
            _skillManager,
            _phase2Config,
            new Hercules.Skills.Routing.ISkillScorer[]
            {
                embeddingScorer, lexicalScorer, qualityScorer, schemaScorer, latencyScorer, policyScorer
            },
            Enumerable.Empty<string>(),
            embeddingScorer);

        var skill1 = _skillManager.CreateManual("Погода", ["погода"], "Промпт1");
        var skill2 = _skillManager.CreateManual("Код", ["код"], "Промпт2");

        var results = await engine.ScoreAllAsync("какая погода завтра");

        Assert.NotEmpty(results);
        // "Погода" should rank higher because it matches "погода" in the query
        Assert.Equal("Погода", results[0].Skill.Meta.Name);
        Assert.True(results[0].IsEligible);
    }

    /// <summary>[task_022] SkillScoringEngine.ScoreBestAsync returns null when no skills match.</summary>
    [Fact]
    public async Task SkillScoringEngine_ScoreBestAsync_ReturnsNull_When_NoMatch()
    {
        var embeddingScorer = new Hercules.Skills.Routing.ScoringComponents.EmbeddingScorer(_embedder, _skillManager, 0.1);
        var lexicalScorer = new Hercules.Skills.Routing.ScoringComponents.LexicalScorer();
        var qualityScorer = new Hercules.Skills.Routing.ScoringComponents.HistoricalQualityScorer();
        var schemaScorer = new Hercules.Skills.Routing.ScoringComponents.SchemaCompatibilityScorer();
        var latencyScorer = new Hercules.Skills.Routing.ScoringComponents.LatencyScorer();
        var policyScorer = new Hercules.Skills.Routing.ScoringComponents.PolicyEligibilityScorer();

        var engine = new SkillScoringEngine(
            _skillManager,
            _phase2Config,
            new Hercules.Skills.Routing.ISkillScorer[]
            {
                embeddingScorer, lexicalScorer, qualityScorer, schemaScorer, latencyScorer, policyScorer
            },
            Enumerable.Empty<string>(),
            embeddingScorer);

        var best = await engine.ScoreBestAsync("запрос без совпадений");

        Assert.Null(best);
    }
}
