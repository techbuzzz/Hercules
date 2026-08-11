using Hercules.Agent;
using Hercules.Config;
using Hercules.Skills;
using Hercules.Storage;
using Xunit;

namespace Hercules.Agent.Tests.Phase2Tests;

public class EmbeddingSkillRouterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSkillRepository _repo;
    private readonly SkillManager _skillManager;
    private readonly StubEmbeddingProvider _embedder;
    private readonly EmbeddingSkillRouter _router;

    public EmbeddingSkillRouterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-emb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var storageCfg = new StorageConfig { DataRoot = _tempDir };
        _repo = new FileSkillRepository(storageCfg);
        _skillManager = new SkillManager(_repo, new StubLlm("test"), new AgentConfig());
        _embedder = new StubEmbeddingProvider();
        _router = new EmbeddingSkillRouter(_embedder, _skillManager)
        {
            SimilarityThreshold = 0.1, // Низкий порог для stub-embeddings
            UseKeywordFallback = true,
        };
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* best effort */ }
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
    public async Task RouteAsync_Matches_Skill_By_Keyword_Or_Embedding()
    {
        _skillManager.CreateManual("Погода", ["погода"], "Ты — погодный ассистент.");

        // Запрос содержит "погода" — должен сматчиться (embedding или keyword)
        var result = await _router.RouteAsync("какая погода в москве");

        Assert.True(result.IsSkill);
        Assert.Equal("Погода", result.MatchedSkill!.Meta.Name);
        // Метод может быть "embedding" или "keyword" — оба валидны
        Assert.Contains(result.Method, new[] { "embedding", "keyword" });
    }

    [Fact]
    public async Task RouteAsync_Matches_Skill_With_Similar_Text()
    {
        _skillManager.CreateManual("Рецепты", ["готовить", "еда", "рецепты"], "Ты — кулинарный ассистент.");

        // "готовить еду" — содержит "готовить" и "еду" (близко к "еда"), должен сматчиться.
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
        _skillManager.CreateManual("Код", ["код программирование"], "Ты — ассистент программиста.");

        // Без инвалидации кэша — навык может не найдись (кэш еще не перестроен)
        // Но RefreshCacheIfNeeded проверяет список ID, так что должен подхватить
        var result2 = await _router.RouteAsync("напиши код");
        Assert.True(result2.IsSkill);
    }

    [Fact]
    public async Task StubEmbeddingProvider_Returns_NonZero_Vector_For_NonEmpty_Text()
    {
        var vector = await _embedder.EmbedAsync("test text");
        Assert.Equal(256, vector.Length);
        // Хотя бы один элемент ненулевой
        Assert.Contains(vector, v => v != 0);
    }

    [Fact]
    public async Task StubEmbeddingProvider_Returns_Zero_Vector_For_Empty_Text()
    {
        var vector = await _embedder.EmbedAsync("");
        Assert.All(vector, v => Assert.Equal(0, v));
    }

    [Fact]
    public async Task RouteAsync_Disabled_Keyword_Fallback_Returns_None_When_Below_Threshold()
    {
        _skillManager.CreateManual("Тест", ["уникальная фраза которой нет в запросе"], "Промпт");

        _router.UseKeywordFallback = false;
        _router.SimilarityThreshold = 0.99; // Очень высокий порог — stub не достигнет

        var result = await _router.RouteAsync("совершенно другой текст без совпадений");
        Assert.False(result.IsSkill);
    }
}