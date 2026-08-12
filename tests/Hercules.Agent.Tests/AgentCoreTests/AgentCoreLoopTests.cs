using System.Runtime.CompilerServices;
using Hercules.Agent.Loop;
using Hercules.Config;
using Hercules.LLM;
using Hercules.LLM.JsonRepair;
using Hercules.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.AgentCoreTests;

/// <summary>
/// Тесты для AgentCore, фокус: loop state tracking и EndSessionAsync.
/// </summary>
public class AgentCoreLoopTests : IDisposable
{
    private readonly MemoryManager _memory;
    private readonly SkillRouter _router;
    private readonly SqliteSessionStore _sessions;
    private readonly SkillManager _skillManager;
    private readonly string _tempDir;

    public AgentCoreLoopTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-loop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var storageCfg = new StorageConfig { DataRoot = _tempDir };
        _sessions = new SqliteSessionStore(storageCfg);
        var skillRepo = new FileSkillRepository(storageCfg, NullLogger<FileSkillRepository>.Instance);
        _skillManager = new SkillManager(
            skillRepo,
            new StubLlmClient("test"),
            new AgentConfig { SkillEvaluationWindow = 5 },
            new JsonRepairService());
        _router = new SkillRouter(_skillManager);
        _memory = new MemoryManager(new MemoryStore(storageCfg), new StubLlmClient("mem"));
    }

    public void Dispose()
    {
        try { _sessions.Dispose(); } catch { /* best effort */ }
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    private AgentCore CreateCore(AgentConfig? cfg = null) =>
        new(
            new StubLlmClient("Ответ агента"),
            _router,
            _skillManager,
            _memory,
            _sessions,
            cfg ?? new AgentConfig { SkillCreationThreshold = 3, SkillImprovementThreshold = 0.6, SkillEvaluationWindow = 5 },
            NullLogger<AgentCore>.Instance,
            new JsonRepairService());

    // ---- LoopContext state transitions ----

    [Fact]
    public void LoopContext_Initial_HasCorrectDefaults()
    {
        var ctx = LoopContext.Initial;
        Assert.Equal(LoopStep.SkillRoute, ctx.CurrentStep);
        Assert.Equal(0, ctx.ToolIteration);
        Assert.Null(ctx.LastTool);
        Assert.False(ctx.Cancelled);
    }

    [Fact]
    public void LoopContext_AfterTool_IncrementsIteration()
    {
        var ctx = LoopContext.Initial;
        var next = ctx.AfterTool("http_tool");
        Assert.Equal(LoopStep.ToolExecute, next.CurrentStep);
        Assert.Equal(1, next.ToolIteration);
        Assert.Equal("http_tool", next.LastTool);
    }

    [Fact]
    public void LoopContext_WithStep_UpdatesCurrentStep()
    {
        var ctx = LoopContext.Initial with { CurrentStep = LoopStep.LlmCall };
        Assert.Equal(LoopStep.LlmCall, ctx.CurrentStep);
    }

    [Fact]
    public void LoopContext_CancelledContext_MarksCancelled()
    {
        var ctx = LoopContext.Initial.CancelledContext();
        Assert.True(ctx.Cancelled);
    }

    [Fact]
    public void LoopContext_MultipleToolCalls_IncrementIteration()
    {
        var ctx = LoopContext.Initial;
        var after1 = ctx.AfterTool("tool_a");
        var after2 = after1.AfterTool("tool_b");
        Assert.Equal(1, after1.ToolIteration);
        Assert.Equal(2, after2.ToolIteration);
        Assert.Equal("tool_a", after1.LastTool);
        Assert.Equal("tool_b", after2.LastTool);
    }

    // ---- ShouldReflectByCount ----

    [Fact]
    public async Task ShouldReflectByCount_ZeroThreshold_ReturnsFalse()
    {
        var core = CreateCore(new AgentConfig { ReflectionEveryNCommands = 0 });
        core.StartSession();
        for (var i = 0; i < 10; i++) _ = await core.HandleAsync($"запрос {i}");
        Assert.False(core.ShouldReflectByCount());
    }

    [Fact]
    public async Task ShouldReflectByCount_AtExactThreshold_ReturnsTrue()
    {
        var core = CreateCore(new AgentConfig { ReflectionEveryNCommands = 3 });
        core.StartSession();
        _ = await core.HandleAsync("запрос 1");
        _ = await core.HandleAsync("запрос 2");
        // 3-й вызов HandleAsync инкрементирует CommandCount до 3
        _ = await core.HandleAsync("запрос 3");
        Assert.True(core.ShouldReflectByCount());
    }

    [Fact]
    public async Task ShouldReflectByCount_BelowThreshold_ReturnsFalse()
    {
        var core = CreateCore(new AgentConfig { ReflectionEveryNCommands = 5 });
        core.StartSession();
        for (var i = 0; i < 4; i++) _ = await core.HandleAsync($"запрос {i}");
        Assert.False(core.ShouldReflectByCount());
    }

    [Fact]
    public async Task ShouldReflectByCount_MultipleOfThreshold_ReturnsTrue()
    {
        var core = CreateCore(new AgentConfig { ReflectionEveryNCommands = 2 });
        core.StartSession();
        for (var i = 0; i < 4; i++) _ = await core.HandleAsync($"запрос {i}");
        // CommandCount = 4, 4 % 2 == 0
        Assert.True(core.ShouldReflectByCount());
    }

    // ---- EndSessionAsync ----

    [Fact]
    public async Task EndSessionAsync_PersistsTranscript()
    {
        // Используем stub LLM, который возвращает структурированный markdown
        var structuredLlm = new StubLlmClient("""
            ### SUMMARY
            Разговор о погоде и настроении.

            ### PROFILE
            - пользователь вежливый

            ### ENTITIES
            (нет нового)

            ### PREFERENCES
            (нет нового)
            """);

        var cfg = new AgentConfig { SkillEvaluationWindow = 5 };
        var core = new AgentCore(structuredLlm, _router, _skillManager,
            new MemoryManager(new MemoryStore(new StorageConfig { DataRoot = _tempDir }), structuredLlm),
            _sessions, cfg, NullLogger<AgentCore>.Instance,
            new JsonRepairService());

        core.StartSession();
        await core.HandleAsync("Привет");
        await core.HandleAsync("Как дела?");

        // EndSessionAsync вызывает PersistSessionAsync
        await core.EndSessionAsync();

        // MemoryManager.PersistSessionAsync создаёт файл контекста
        var store = new MemoryStore(new StorageConfig { DataRoot = _tempDir });
        var lastCtx = store.ReadLastContext();
        Assert.NotNull(lastCtx);
        // Stub LLM возвращает структурированный markdown с секцией SUMMARY
        Assert.Contains("Разговор", lastCtx);
    }

    [Fact]
    public void EndSession_Sync_DoesNotThrow()
    {
        var core = CreateCore();
        core.StartSession();
        var ex = Record.Exception(() => core.EndSession());
        Assert.Null(ex);
    }

    // ---- StubLlmClient (reuse from existing test file) ----

    private sealed class StubLlmClient : ILLMClient
    {
        private readonly string _confidence;
        private readonly string _responseText;

        public StubLlmClient(string responseText, string confidence = "medium")
        {
            _responseText = responseText;
            _confidence = confidence;
        }

        public string ProviderName => "stub";
        public string ModelName => "stub-model";

        public Task<LlmResponse> CompleteAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default) =>
            Task.FromResult(new LlmResponse($"{_responseText} [confidence: {_confidence}]", ProviderName, ModelName));

        public Task<LlmResponse> CompleteAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default) =>
            CompleteAsync(Roles.Main, messages, ct);

        public IAsyncEnumerable<string> StreamAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default) =>
            StreamAsync(messages, ct);

        public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatTurn> messages, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return _responseText;
        }
    }
}
