using Hercules.Agent;
using Hercules.Config;
using Hercules.Context;
using Hercules.LLM;
using Hercules.LLM.JsonRepair;
using Hercules.Memory.Layers;
using Hercules.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.AgentCoreTests;

/// <summary>
///     Тесты для task_075 — DI lifetime fixes: captive dependency (H6) and
///     AgentCore singleton with mutable state (H7). Verifies that parallel
///     HandleAsync calls with different sessionIds do not share state.
/// </summary>
public class DiLifetimeFixesTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteSessionStore _sessions;
    private readonly MemoryManager _memory;
    private readonly SkillRouter _router;
    private readonly SkillManager _skillManager;
    private readonly InMemorySessionStateStore _stateStore;

    public DiLifetimeFixesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-di-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var storageCfg = new StorageConfig { DataRoot = _tempDir };
        _sessions = new SqliteSessionStore(storageCfg);
        var skillRepo = new FileSkillRepository(storageCfg, NullLogger<FileSkillRepository>.Instance);
        _skillManager = new SkillManager(
            skillRepo,
            new DiFixStubLlmClient("test"),
            new AgentConfig { SkillEvaluationWindow = 5 },
            new JsonRepairService());
        _router = new SkillRouter(_skillManager);
        _memory = new MemoryManager(new MemoryStore(storageCfg), new DiFixStubLlmClient("mem"));
        _stateStore = new InMemorySessionStateStore();
    }

    public void Dispose()
    {
        try { _sessions.Dispose(); } catch { /* best effort */ }
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    private AgentCore CreateCore() => new(
        new DiFixStubLlmClient("Echo test"),
        _router,
        _skillManager,
        _memory,
        _sessions,
        new AgentConfig
        {
            SkillCreationThreshold = 100,           // disable propose-skill in tests
            SkillImprovementThreshold = 0.0,
            SkillEvaluationWindow = 100
        },
        NullLogger<AgentCore>.Instance,
        new JsonRepairService(),
        _stateStore);

    // ---- H7 fix: parallel HandleAsync with different sessionIds ----

    [Fact]
    public async Task HandleAsync_ParallelSessions_TranscriptsDoNotInterleave()
    {
        var core = CreateCore();
        var sessionA = "session-A";
        var sessionB = "session-B";

        // Each session gets 3 distinct turns. The two LLM stubs alternate
        // different content per request (StubLlmClient always returns the
        // same text, so the test is conservative — we check the transcript
        // entries, not the LLM output content).
        var tasksA = Enumerable.Range(0, 3)
            .Select(i => core.HandleAsync($"A-turn-{i}", options: null, sessionId: sessionA))
            .ToArray();
        var tasksB = Enumerable.Range(0, 3)
            .Select(i => core.HandleAsync($"B-turn-{i}", options: null, sessionId: sessionB))
            .ToArray();

        await Task.WhenAll(tasksA);
        await Task.WhenAll(tasksB);

        var stateA = _stateStore.GetOrCreate(sessionA);
        var stateB = _stateStore.GetOrCreate(sessionB);

        // Each session has 3 user turns + 3 assistant turns = 6 turns.
        Assert.Equal(6, stateA.Transcript.Count);
        Assert.Equal(6, stateB.Transcript.Count);

        // No session contains the OTHER session's user input.
        var aInputs = stateA.Transcript
            .Where(t => t.Role == ChatRole.User)
            .Select(t => t.Content)
            .ToList();
        var bInputs = stateB.Transcript
            .Where(t => t.Role == ChatRole.User)
            .Select(t => t.Content)
            .ToList();

        Assert.All(aInputs, input => Assert.StartsWith("A-", input));
        Assert.All(bInputs, input => Assert.StartsWith("B-", input));
        Assert.DoesNotContain(aInputs, x => bInputs.Contains(x));
    }

    [Fact]
    public async Task HandleAsync_DifferentSessions_IncrementSeparateCommandCount()
    {
        var core = CreateCore();
        await core.HandleAsync("a1", options: null, sessionId: "alpha");
        await core.HandleAsync("a2", options: null, sessionId: "alpha");
        await core.HandleAsync("a3", options: null, sessionId: "alpha");
        await core.HandleAsync("b1", options: null, sessionId: "beta");
        await core.HandleAsync("b2", options: null, sessionId: "beta");

        Assert.Equal(3, _stateStore.GetOrCreate("alpha").CommandCount);
        Assert.Equal(2, _stateStore.GetOrCreate("beta").CommandCount);
        // Default SessionId is not used here.
        Assert.Equal(0, _stateStore.GetOrCreate(core.SessionId).CommandCount);
    }

    [Fact]
    public async Task HandleAsync_SameSession_AccumulatesCommandCount()
    {
        var core = CreateCore();
        for (int i = 0; i < 5; i++)
        {
            await core.HandleAsync($"turn-{i}", options: null, sessionId: "sess");
        }
        Assert.Equal(5, _stateStore.GetOrCreate("sess").CommandCount);
    }

    [Fact]
    public void SessionStateStore_GetOrCreate_ReturnsSameInstance()
    {
        var s1 = _stateStore.GetOrCreate("x");
        var s2 = _stateStore.GetOrCreate("x");
        Assert.Same(s1, s2);
    }

    [Fact]
    public void SessionStateStore_DifferentIds_ReturnDifferentInstances()
    {
        var s1 = _stateStore.GetOrCreate("x");
        var s2 = _stateStore.GetOrCreate("y");
        Assert.NotSame(s1, s2);
        Assert.Equal("x", s1.SessionId);
        Assert.Equal("y", s2.SessionId);
    }

    [Fact]
    public void SessionStateStore_Remove_DeletesSession()
    {
        _stateStore.GetOrCreate("to-remove");
        Assert.True(_stateStore.Remove("to-remove"));
        Assert.False(_stateStore.Remove("to-remove"));
        Assert.Equal(0, _stateStore.Count);
    }

    [Fact]
    public void SessionState_Transcript_AppendIsolatedPerSession()
    {
        var sA = _stateStore.GetOrCreate("a");
        var sB = _stateStore.GetOrCreate("b");

        sA.AppendTranscript(new ChatTurn(ChatRole.User, "only-in-A"));
        sB.AppendTranscript(new ChatTurn(ChatRole.User, "only-in-B"));

        Assert.Single(sA.Transcript);
        Assert.Single(sB.Transcript);
        Assert.Equal("only-in-A", sA.Transcript[0].Content);
        Assert.Equal("only-in-B", sB.Transcript[0].Content);
    }

    [Fact]
    public void SessionState_ToolTrace_AppendIsolatedPerSession()
    {
        var sA = _stateStore.GetOrCreate("a");
        var sB = _stateStore.GetOrCreate("b");

        sA.AppendToolTrace(new ToolTraceEntry("a-tool", "", "", 1, DateTime.UtcNow, true));
        sB.AppendToolTrace(new ToolTraceEntry("b-tool", "", "", 1, DateTime.UtcNow, true));

        Assert.Single(sA.ToolTrace);
        Assert.Single(sB.ToolTrace);
        Assert.Equal("a-tool", sA.ToolTrace[0].ToolName);
        Assert.Equal("b-tool", sB.ToolTrace[0].ToolName);
    }

    // ---- H6 fix: LayeredMemoryManager per-session working memory ----

    [Fact]
    public async Task LayeredMemoryManager_SessionScoped_WorkingMemoryIsolatedPerSession()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hercules-mem-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var storageCfg = new StorageConfig { DataRoot = tempDir, MemoryDir = "Memory" };
            var facts = new DurableFactsService(storageCfg);
            var episodes = new EpisodicStore(storageCfg);
            var stateStore = new InMemorySessionStateStore();

            var manager = new LayeredMemoryManager(stateStore, facts, episodes);

            // Set the SAME key for two different sessions — they must not collide.
            manager.SetWorking("shared-key", "value-for-A", sessionId: "A");
            manager.SetWorking("shared-key", "value-for-B", sessionId: "B");

            Assert.Equal("value-for-A", manager.GetWorking("shared-key", "A"));
            Assert.Equal("value-for-B", manager.GetWorking("shared-key", "B"));

            // Clearing session A must not touch session B.
            manager.ClearWorking("A");
            Assert.Null(manager.GetWorking("shared-key", "A"));
            Assert.Equal("value-for-B", manager.GetWorking("shared-key", "B"));

            // BuildContextBlockAsync with sessionId A returns no working memory (redacted/empty).
            var ctxA = await manager.BuildContextBlockAsync("A");
            var ctxB = await manager.BuildContextBlockAsync("B");
            // ctxA: empty working memory block (no "РАБОЧАЯ ПАМЯТЬ" section)
            Assert.DoesNotContain("РАБОЧАЯ ПАМЯТЬ", ctxA);
            // ctxB: contains the entry for B.
            Assert.Contains("РАБОЧАЯ ПАМЯТЬ", ctxB);
            Assert.Contains("value-for-B", ctxB);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task LayeredMemoryManager_LegacyCtor_StillWorksForBackCompat()
    {
        // The single-session ctor is still used by direct unit tests and CLI mode.
        var tempDir = Path.Combine(Path.GetTempPath(), $"hercules-mem-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var storageCfg = new StorageConfig { DataRoot = tempDir, MemoryDir = "Memory" };
            var facts = new DurableFactsService(storageCfg);
            var episodes = new EpisodicStore(storageCfg);
            var working = new WorkingMemoryService();

            var manager = new LayeredMemoryManager(working, facts, episodes);
            manager.SetWorking("k", "v");

            var ctx = await manager.BuildContextBlockAsync();
            Assert.Contains("РАБОЧАЯ ПАМЯТЬ", ctx);
            Assert.Contains("v", ctx);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* best effort */ }
        }
    }

    // ---- DI validation: H6 — working memory must be per-session, not process-wide ----

    [Fact]
    public void InMemorySessionStateStore_PerSessionWorkingMemory_AreDifferentInstances()
    {
        // Each session's IWorkingMemory is independently constructed — verifies
        // that the previous singleton/captive-dep bug cannot resurface.
        var sA = _stateStore.GetOrCreate("a");
        var sB = _stateStore.GetOrCreate("b");
        Assert.NotSame(sA.WorkingMemory, sB.WorkingMemory);

        sA.WorkingMemory.Set("k", "A");
        Assert.Null(sB.WorkingMemory.Get("k"));
    }
}

/// <summary>Минимальный LLM-стаб для тестов AgentCore: возвращает фиксированный текст.</summary>
internal sealed class DiFixStubLlmClient : ILLMClient
{
    private readonly string _text;
    public DiFixStubLlmClient(string text) => _text = text;

    public string ProviderName => "stub";
    public string ModelName => "stub";

    public Task<LlmResponse> CompleteAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default) =>
        Task.FromResult(new LlmResponse($"{_text} [confidence: high]", ProviderName, ModelName));

    public Task<LlmResponse> CompleteAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default) =>
        CompleteAsync(messages, ct);

    public IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default) =>
        StreamAsync("main", messages, ct);

    public async IAsyncEnumerable<string> StreamAsync(string role, IReadOnlyList<ChatTurn> messages, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        yield return _text;
    }
}
