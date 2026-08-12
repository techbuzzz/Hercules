using System.Runtime.CompilerServices;
using Hercules.Agent;
using Hercules.Config;
using Hercules.LLM;
using Hercules.Skills;
using Hercules.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.AgentCoreTests;

/// <summary>
/// Тесты SkillEvaluationEngine: запуск test suite, проверка результатов.
/// </summary>
public class SkillEvaluationEngineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSkillRepository _repo;
    private readonly SkillManager _skillManager;
    private readonly SkillDeprecationManager _deprecation;
    private readonly SkillLifecyclePolicy _policy;
    private readonly SkillEvaluationEngine _engine;
    private readonly AgentCore _agent;

    public SkillEvaluationEngineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-eval-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var storageCfg = new StorageConfig { DataRoot = _tempDir };
        _repo = new FileSkillRepository(storageCfg, NullLogger<FileSkillRepository>.Instance);
        _skillManager = new SkillManager(_repo, new StubLlmClient("тестовый ответ"), new AgentConfig());
        _deprecation = new SkillDeprecationManager(_repo, NullLogger<SkillDeprecationManager>.Instance);
        _policy = new SkillLifecyclePolicy();

        var sessions = new SqliteSessionStore(storageCfg);
        var router = new SkillRouter(_skillManager);
        var memory = new MemoryManager(new MemoryStore(storageCfg), new StubLlmClient("память"));
        _agent = new AgentCore(
            new StubLlmClient("тестовый ответ [confidence: high]"),
            router, _skillManager, memory, sessions,
            new AgentConfig(), NullLogger<AgentCore>.Instance);

        _engine = new SkillEvaluationEngine(_agent, NullLogger<SkillEvaluationEngine>.Instance);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task EvaluateAsync_Returns_error_when_skill_not_found()
    {
        var result = await _engine.EvaluateAsync(
            new Skill { Meta = new SkillMeta { Id = "nonexistent" } },
            new SkillTestSuite { Tests = [] });
        Assert.Equal("nonexistent", result.SkillId);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task EvaluateAsync_Returns_zero_score_when_test_suite_empty()
    {
        var skill = _skillManager.CreateManual("пустой", ["empty"], "Пустой навык.");
        var result = await _engine.EvaluateAsync(skill, new SkillTestSuite { Tests = [] });
        Assert.Equal(0, result.TotalTests);
        Assert.Equal(0, result.PassedTests);
        Assert.Equal(0, result.Score);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task EvaluateAsync_Passes_test_when_ExpectedContains_matches()
    {
        // Используем конкретный stub с предсказуемым ответом
        var stub = new ContainsStubLlmClient("ответ: интеграция прошла успешно", "high");
        var sessions = new SqliteSessionStore(new StorageConfig { DataRoot = _tempDir });
        var stubRouter = new SkillRouter(_skillManager);
        var stubMemory = new MemoryManager(new MemoryStore(new StorageConfig { DataRoot = _tempDir }), stub);
        var stubAgent = new AgentCore(stub, stubRouter, _skillManager, stubMemory, sessions,
            new AgentConfig(), NullLogger<AgentCore>.Instance);
        var engine = new SkillEvaluationEngine(stubAgent, NullLogger<SkillEvaluationEngine>.Instance);

        var skill = _skillManager.CreateManual("содержимое", ["содерж"], "Ты — тестовый ассистент.");
        var suite = new SkillTestSuite
        {
            Tests =
            [
                new SkillTestCase
                {
                    Name = "contains check",
                    Input = "скажи что-нибудь",
                    ExpectedContains = "интеграция прошла"
                }
            ]
        };

        var result = await engine.EvaluateAsync(skill, suite);
        Assert.Equal(1, result.TotalTests);
        Assert.Equal(1, result.PassedTests);
        Assert.Equal(1.0, result.Score);
    }

    [Fact]
    public async Task EvaluateAsync_Fails_test_when_ExpectedContains_not_found()
    {
        // stub возвращает ответ БЕЗ искомой фразы
        var stub = new ContainsStubLlmClient("ответ: операция выполнена без ошибок", "medium");
        var sessions = new SqliteSessionStore(new StorageConfig { DataRoot = _tempDir });
        var stubRouter = new SkillRouter(_skillManager);
        var stubMemory = new MemoryManager(new MemoryStore(new StorageConfig { DataRoot = _tempDir }), stub);
        var stubAgent = new AgentCore(stub, stubRouter, _skillManager, stubMemory, sessions,
            new AgentConfig(), NullLogger<AgentCore>.Instance);
        var engine = new SkillEvaluationEngine(stubAgent, NullLogger<SkillEvaluationEngine>.Instance);

        var skill = _skillManager.CreateManual("проверка", ["проверка"], "Ты — ассистент проверки.");
        var suite = new SkillTestSuite
        {
            Tests =
            [
                new SkillTestCase
                {
                    Name = "negative check",
                    Input = "что-то спросить",
                    ExpectedContains = "xyz_no_such_phrase_in_response"
                }
            ]
        };

        var result = await engine.EvaluateAsync(skill, suite);
        Assert.Equal(1, result.TotalTests);
        Assert.Equal(0, result.PassedTests);
        Assert.Equal(0, result.Score);
        Assert.NotNull(result.TestResults[0].FailureReason);
        Assert.Contains("ExpectedContains", result.TestResults[0].FailureReason!);
    }

    [Fact]
    public async Task EvaluateAsync_Passes_test_when_MinConfidence_satisfied()
    {
        var skill = _skillManager.CreateManual("уверенный", ["уверен"], "Ты — уверенный ассистент.");
        var suite = new SkillTestSuite
        {
            Tests =
            [
                new SkillTestCase
                {
                    Name = "confidence check",
                    Input = "спроси",
                    MinConfidence = "low"
                }
            ]
        };

        var result = await _engine.EvaluateAsync(skill, suite);
        Assert.Equal(1, result.TotalTests);
        Assert.Equal(1, result.PassedTests);
        Assert.Equal(1.0, result.Score);
    }

    [Fact]
    public async Task EvaluateAsync_Calculates_score_correctly_for_multiple_tests()
    {
        var skill = _skillManager.CreateManual("микс", ["микс"], "Навык с миксом тестов.");
        var suite = new SkillTestSuite
        {
            Tests =
            [
                new SkillTestCase { Name = "pass1", Input = "вопрос1", ExpectedContains = "тестовый" },
                new SkillTestCase { Name = "pass2", Input = "вопрос2", ExpectedContains = "ответ" },
                new SkillTestCase { Name = "fail1", Input = "вопрос3", ExpectedContains = "XYZ_NOT_IN_RESPONSE" }
            ]
        };

        var result = await _engine.EvaluateAsync(skill, suite);
        Assert.Equal(3, result.TotalTests);
        Assert.Equal(2, result.PassedTests);
        Assert.Equal(1, result.FailedTests);
        Assert.Equal(0.67, result.Score, 2);
    }

    [Fact]
    public async Task EvaluateAsync_Respects_total_timeout()
    {
        var shortTimeoutEngine = new SkillEvaluationEngine(_agent, NullLogger<SkillEvaluationEngine>.Instance, totalTimeoutSeconds: 1);
        var skill = _skillManager.CreateManual("быстрый", ["быстрый"], "Ты — быстрый ассистент.");
        var suite = new SkillTestSuite
        {
            Tests =
            [
                new SkillTestCase { Name = "test1", Input = "вопрос1", ExpectedContains = "быстрый" },
                new SkillTestCase { Name = "test2", Input = "вопрос2", ExpectedContains = "ассистент" }
            ]
        };

        var result = await shortTimeoutEngine.EvaluateAsync(skill, suite);
        Assert.NotNull(result);
        Assert.Equal(skill.Meta.Id, result.SkillId);
    }

    // ---- SkillLifecycleService integration ----

    [Fact]
    public async Task SkillManager_EvaluateAsync_updates_LastEvaluationScore()
    {
        var skill = _skillManager.CreateManual("оценка", ["оценка"], "Ты — оцениваемый ассистент.");
        var suite = new SkillTestSuite
        {
            Tests =
            [
                new SkillTestCase { Name = "test1", Input = "вопрос", ExpectedContains = "тестовый" }
            ]
        };

        var result = await _skillManager.EvaluateAsync(skill.Meta.Id, suite, _engine);

        var reloaded = _skillManager.Get(skill.Meta.Id);
        Assert.Equal(result.Score, reloaded!.Meta.LastEvaluationScore);
    }

    // ---- Stub implementations ----

    /// <summary>
    /// Stub, который возвращает конкретный ответ с маркером уверенности
    /// (нужен для изоляции от skill prompt в EvaluateSkillAsync).
    /// </summary>
    private sealed class ContainsStubLlmClient : ILLMClient
    {
        private readonly string _text;
        private readonly string _confidence;

        public ContainsStubLlmClient(string text, string confidence = "medium")
        {
            _text = text;
            _confidence = confidence;
        }

        public string ProviderName => "stub";
        public string ModelName => "stub-model";

        public Task<LlmResponse> CompleteAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default) =>
            Task.FromResult(new LlmResponse($"{_text} [confidence: {_confidence}]", ProviderName, ModelName));

        public Task<LlmResponse> CompleteAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default) =>
            CompleteAsync(Roles.Main, messages, ct);

        public IAsyncEnumerable<string> StreamAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default) =>
            StreamAsync(messages, ct);

        public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatTurn> messages,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return $"{_text} [confidence: {_confidence}]";
        }
    }

    private sealed class StubLlmClient : ILLMClient
    {
        private readonly string _responseText;
        public StubLlmClient(string responseText) => _responseText = responseText;
        public string ProviderName => "stub";
        public string ModelName => "stub-model";
        public Task<LlmResponse> CompleteAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default) =>
            Task.FromResult(new LlmResponse(_responseText, ProviderName, ModelName));
        public Task<LlmResponse> CompleteAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default) =>
            CompleteAsync(Roles.Main, messages, ct);
        public IAsyncEnumerable<string> StreamAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default) =>
            StreamAsync(messages, ct);
        public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatTurn> messages,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return _responseText;
        }
    }
}
