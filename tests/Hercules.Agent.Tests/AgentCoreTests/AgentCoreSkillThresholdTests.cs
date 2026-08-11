using Hercules.Agent;
using Hercules.Config;
using Hercules.LLM;
using Hercules.Storage;
using Xunit;

namespace Hercules.Agent.Tests.AgentCoreTests;

/// <summary>
///     Stub LLM-клиент: всегда отвечает фиксированным текстом с маркером уверенности.
///     Не обращается к реальному LLM-провайдеру — нужен только для проверки логики AgentCore.
/// </summary>
internal sealed class StubLlmClient : ILLMClient
{
    public string ProviderName => "stub";
    public string ModelName => "stub-model";

    private readonly string _responseText;
    private readonly string _confidence;

    public StubLlmClient(string responseText, string confidence = "medium")
    {
        _responseText = responseText;
        _confidence = confidence;
    }

    public Task<LlmResponse> CompleteAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
        => Task.FromResult(new LlmResponse($"{_responseText} [confidence: {_confidence}]", ProviderName, ModelName));

    public Task<LlmResponse> CompleteAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
        => CompleteAsync(Roles.Main, messages, ct);

    public IAsyncEnumerable<string> StreamAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
        => StreamAsync(messages, ct);

    public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatTurn> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        yield return _responseText;
    }
}

public class AgentCoreSkillThresholdTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteSessionStore _sessions;
    private readonly FileSkillRepository _skillRepo;
    private readonly SkillManager _skillManager;
    private readonly SkillRouter _router;
    private readonly MemoryManager _memory;

    public AgentCoreSkillThresholdTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var storageCfg = new StorageConfig { DataRoot = _tempDir };
        _sessions = new SqliteSessionStore(storageCfg);
        _skillRepo = new FileSkillRepository(storageCfg);
        _skillManager = new SkillManager(_skillRepo, new StubLlmClient("test"), new AgentConfig
        {
            SkillCreationThreshold = 3,
            SkillImprovementThreshold = 0.6,
            SkillEvaluationWindow = 5,
        });
        _router = new SkillRouter(_skillManager);
        _memory = new MemoryManager(new MemoryStore(storageCfg), new StubLlmClient("mem"));
    }

    public void Dispose()
    {
        try { _sessions.Dispose(); } catch { /* best effort */ }
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    private AgentCore CreateCore(AgentConfig? cfg = null)
    {
        return new AgentCore(
            new StubLlmClient("Ответ агентa"),
            _router,
            _skillManager,
            _memory,
            _sessions,
            cfg ?? new AgentConfig { SkillCreationThreshold = 3, SkillImprovementThreshold = 0.6, SkillEvaluationWindow = 5 });
    }

    [Fact]
    public async Task Repeated_Direct_Request_Below_Threshold_Does_Not_Propose_Skill()
    {
        var core = CreateCore();
        core.StartSession();
        var input = "какая погода в москве";

        // 2 повтора (< порога 3) — не должно предлагать создание навыка
        await core.HandleAsync(input);
        var resp = await core.HandleAsync(input);

        Assert.Null(resp.ProposeSkillForInput);
    }

    [Fact]
    public async Task Repeated_Direct_Request_At_Threshold_Proposes_Skill()
    {
        var core = CreateCore();
        core.StartSession();
        var input = "какая погода в москве";

        await core.HandleAsync(input);
        await core.HandleAsync(input);
        var resp = await core.HandleAsync(input);

        // 3-й повтор = порог достигнут → предложение создать навык
        Assert.NotNull(resp.ProposeSkillForInput);
        Assert.Equal(input, resp.ProposeSkillForInput);
    }

    [Fact]
    public async Task Request_Matching_Skill_Does_Not_Propose_Creation()
    {
        var core = CreateCore();
        core.StartSession();

        // Создаём навык вручную с фразой-приёмником "погода"
        _skillManager.CreateManual(
            name: "Погода",
            phraseReceivers: ["погода"],
            prompt: "Ты — ассистент по погоде.");

        // Запрос, матчится с навыком → не должен предлагать создание (уже есть навык)
        var resp = await core.HandleAsync("какая погода в москве");

        Assert.Null(resp.ProposeSkillForInput);
        Assert.Equal("skill", resp.Mode);
    }

    [Fact]
    public async Task ResetRequestCounter_Prevents_Future_Proposals()
    {
        var core = CreateCore();
        core.StartSession();
        var input = "переведи текст";

        await core.HandleAsync(input);
        await core.HandleAsync(input);
        // Сбрасываем счётчик (как после создания навыка)
        core.ResetRequestCounter(input);

        // Снова 2 повтора — не должно предлагать (счётчик сброшен)
        await core.HandleAsync(input);
        var resp = await core.HandleAsync(input);

        Assert.Null(resp.ProposeSkillForInput);
    }

    [Fact]
    public async Task Low_Success_Rate_Skill_Triggers_Improve_Proposal()
    {
        var cfg = new AgentConfig
        {
            SkillCreationThreshold = 3,
            SkillImprovementThreshold = 0.6,
            SkillEvaluationWindow = 5,
        };
        var core = CreateCore(cfg);
        core.StartSession();

        // Создаём навык и накручиваем неудачные использования
        var skill = _skillManager.CreateManual(
            name: "Тест",
            phraseReceivers: ["тестовый запрос"],
            prompt: "Ты — тестовый ассистент.");

        // 5 неудачных использований (success=false, confidence=low)
        for (int i = 0; i < 5; i++)
        {
            _skillManager.RecordUsage(skill.Meta.Id, success: false, confidence: "low");
        }

        // Теперь запрос с low confidence должен предложить улучшение
        var lowCore = new AgentCore(
            new StubLlmClient("плохой ответ", confidence: "low"),
            _router, _skillManager, _memory, _sessions, cfg);
        lowCore.StartSession();

        var resp = await lowCore.HandleAsync("тестовый запрос");

        Assert.NotNull(resp.ProposeImproveSkillId);
        Assert.Equal(skill.Meta.Id, resp.ProposeImproveSkillId);
    }

    [Fact]
    public async Task High_Success_Rate_Skill_Does_Not_Propose_Improve()
    {
        var cfg = new AgentConfig
        {
            SkillCreationThreshold = 3,
            SkillImprovementThreshold = 0.6,
            SkillEvaluationWindow = 5,
        };
        var core = CreateCore(cfg);
        core.StartSession();

        var skill = _skillManager.CreateManual(
            name: "Хороший навык",
            phraseReceivers: ["успешный запрос"],
            prompt: "Ты — хороший ассистент.");

        // 5 успешных использований
        for (int i = 0; i < 5; i++)
        {
            _skillManager.RecordUsage(skill.Meta.Id, success: true, confidence: "high");
        }

        var resp = await core.HandleAsync("успешный запрос");

        Assert.Null(resp.ProposeImproveSkillId);
        Assert.Equal("skill", resp.Mode);
    }

    [Fact]
    public void Reload_Updates_AgentConfig_For_Skill_Thresholds()
    {
        var initialCfg = new AgentConfig { SkillCreationThreshold = 3, ReflectionEveryNCommands = 10 };
        var core = new AgentCore(
            new StubLlmClient("x"),
            _router, _skillManager, _memory, _sessions, initialCfg);

        var newCfg = new AppConfig
        {
            Agent = new AgentConfig { SkillCreationThreshold = 10, ReflectionEveryNCommands = 5 }
        };
        core.Reload(newCfg);

        // Проверяем, что ShouldReflectByCount использует новый порог
        Assert.Equal(5, newCfg.Agent.ReflectionEveryNCommands);
    }
}