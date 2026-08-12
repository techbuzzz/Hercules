using Hercules.Agent;
using Hercules.Config;
using Hercules.LLM;
using Hercules.LLM.JsonRepair;
using Hercules.Skills;
using Hercules.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.AgentCoreTests;

/// <summary>
///     Тесты SkillRouter: маршрутизация по фразам-приёмникам, tie-break по success_rate,
///     direct-режим при отсутствии совпадений.
/// </summary>
public class SkillRouterTests : IDisposable
{
    private readonly FileSkillRepository _repo;
    private readonly SkillManager _skillManager;
    private readonly SkillRouter _router;
    private readonly string _tempDir;

    public SkillRouterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-sr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var cfg = new StorageConfig { DataRoot = _tempDir };
        _repo = new FileSkillRepository(cfg, NullLogger<FileSkillRepository>.Instance);
        _skillManager = new SkillManager(_repo, new StubLlm("test"), new AgentConfig(), new JsonRepairService());
        _router = new SkillRouter(_skillManager);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    [Fact]
    public void Route_NoMatchingSkill_ReturnsDirect()
    {
        _skillManager.CreateManual("Погода", ["погода"], "Ты — погодный.");

        var result = _router.Route("напиши код на python");

        Assert.False(result.IsSkill);
    }

    [Fact]
    public void Route_SingleMatchingSkill_ReturnsSkill()
    {
        _skillManager.CreateManual("Погода", ["погода", "forecast"], "Ты — погодный.");

        var result = _router.Route("какая погода завтра?");

        Assert.True(result.IsSkill);
        Assert.Equal("Погода", result.MatchedSkill!.Meta.Name);
        Assert.True(result.Score >= 1);
    }

    [Fact]
    public void Route_MultipleMatches_HighestScoreWins()
    {
        _skillManager.CreateManual("Код", ["код", "программирование", "python"], "Ты — программист.");
        _skillManager.CreateManual("Python", ["python"], "Python-специалист.");

        var result = _router.Route("напиши код python");

        Assert.True(result.IsSkill);
        // "Код" имеет 3 совпадения (код, программирование, python), Python — 1
        Assert.Equal("Код", result.MatchedSkill!.Meta.Name);
    }

    [Fact]
    public void Route_TieBreak_UsesHigherSuccessRate()
    {
        var skill1 = _skillManager.CreateManual("Навык1", ["тест"], "Промпт 1.");
        var skill2 = _skillManager.CreateManual("Навык2", ["тест"], "Промпт 2.");

        // skill1 получает 3 использования с 1 success (0.33)
        _repo.AppendUsage(skill1.Meta.Id, new SkillUsage { Success = true }, 10);
        _repo.AppendUsage(skill1.Meta.Id, new SkillUsage { Success = false }, 10);
        _repo.AppendUsage(skill1.Meta.Id, new SkillUsage { Success = false }, 10);

        // skill2 получает 2 использования с 2 successes (1.0)
        _repo.AppendUsage(skill2.Meta.Id, new SkillUsage { Success = true }, 10);
        _repo.AppendUsage(skill2.Meta.Id, new SkillUsage { Success = true }, 10);

        // При одинаковом score побеждает навык с большим success_rate
        var result = _router.Route("тест");

        Assert.True(result.IsSkill);
        // Оба имеют score=1 (одно совпадение), но skill2 имеет 100% success rate
        Assert.Equal("Навык2", result.MatchedSkill!.Meta.Name);
    }

    [Fact]
    public void Route_EmptyInput_ReturnsDirect()
    {
        var result = _router.Route("");

        Assert.False(result.IsSkill);
        Assert.Equal(0, result.Score);
    }

    [Fact]
    public void Route_WhitespaceInput_ReturnsDirect()
    {
        var result = _router.Route("   ");

        Assert.False(result.IsSkill);
    }

    [Fact]
    public void Route_CaseInsensitive_MatchesPhrase()
    {
        _skillManager.CreateManual("Тест", ["напиши"], "Ты — тестовый.");

        var result = _router.Route("НАПИШИ код");

        Assert.True(result.IsSkill);
    }

    [Fact]
    public void Route_ExactPhraseMatch_ReturnsHighScore()
    {
        _skillManager.CreateManual("Пост", ["напиши пост"], "Ты — копирайтер.");

        var result = _router.Route("напиши пост в блог");

        Assert.True(result.IsSkill);
        Assert.True(result.Score >= 1);
    }

    [Fact]
    public void RouteResult_IsSkill_TrueWhenMatched()
    {
        var matched = new RouteResult(_skillManager.CreateManual("A", ["x"], "P"), 1);
        var unmatched = new RouteResult(null, 0);

        Assert.True(matched.IsSkill);
        Assert.False(unmatched.IsSkill);
    }
}

internal sealed class StubLlm : ILLMClient
{
    private readonly string _response;
    public string ProviderName => "stub";
    public string ModelName => "stub";

    public StubLlm(string response) => _response = response;

    public Task<LlmResponse> CompleteAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
        => Task.FromResult(new LlmResponse(_response, ProviderName, ModelName));

    public Task<LlmResponse> CompleteAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
        => CompleteAsync(Roles.Main, messages, ct);

    public async IAsyncEnumerable<string> StreamAsync(string role, IReadOnlyList<ChatTurn> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        yield return _response;
    }

    public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatTurn> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        yield return _response;
    }
}
