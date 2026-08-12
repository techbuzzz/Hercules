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
/// Тесты для bounded execution (task_008): wall-clock timeout, max iterations, recursion depth, per-request override.
/// </summary>
public class AgentCoreBoundedExecutionTests : IDisposable
{
    private readonly MemoryManager _memory;
    private readonly SkillRouter _router;
    private readonly SqliteSessionStore _sessions;
    private readonly SkillManager _skillManager;
    private readonly string _tempDir;

    public AgentCoreBoundedExecutionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-bounded-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var storageCfg = new StorageConfig { DataRoot = _tempDir };
        _sessions = new SqliteSessionStore(storageCfg);
        var skillRepo = new FileSkillRepository(storageCfg, NullLogger<FileSkillRepository>.Instance);
        _skillManager = new SkillManager(
            skillRepo,
            new StubLlmClient("ответ"),
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
            new StubLlmClient("ответ агента"),
            _router,
            _skillManager,
            _memory,
            _sessions,
            cfg ?? new AgentConfig
            {
                SkillCreationThreshold = 3,
                SkillImprovementThreshold = 0.6,
                SkillEvaluationWindow = 5,
                MaxToolIterations = 3,
                MaxWallClockTimeoutSeconds = 120,
                MaxRecursionDepth = 2
            },
            NullLogger<AgentCore>.Instance,
            new JsonRepairService());

    // ---- BoundedExecutionOptions ----

    [Fact]
    public void BoundedExecutionOptions_AllNulls_UsesConfigDefaults()
    {
        var opts = new BoundedExecutionOptions();
        Assert.Null(opts.MaxIterations);
        Assert.Null(opts.TimeoutSeconds);
        Assert.Null(opts.MaxRecursionDepth);
    }

    [Fact]
    public void BoundedExecutionOptions_WithValues_HasOverrides()
    {
        var opts = new BoundedExecutionOptions
        {
            MaxIterations = 10,
            TimeoutSeconds = 5,
            MaxRecursionDepth = 3
        };
        Assert.Equal(10, opts.MaxIterations);
        Assert.Equal(5, opts.TimeoutSeconds);
        Assert.Equal(3, opts.MaxRecursionDepth);
    }

    // ---- HandleAsync with BoundedExecutionOptions ----

    [Fact]
    public async Task HandleAsync_WithOptions_OverridesMaxIterations()
    {
        var core = CreateCore(new AgentConfig
        {
            MaxToolIterations = 3,
            MaxWallClockTimeoutSeconds = 120,
            MaxRecursionDepth = 2
        });
        core.StartSession();

        // Pass per-request override with max iterations = 1
        var response = await core.HandleAsync("тестовый запрос",
            new BoundedExecutionOptions { MaxIterations = 1 });

        Assert.Equal("ответ агента", response.Answer);
    }

    [Fact]
    public async Task HandleAsync_WithTimeoutZero_NoTimeout()
    {
        var core = CreateCore(new AgentConfig
        {
            MaxToolIterations = 3,
            MaxWallClockTimeoutSeconds = 0, // unlimited
            MaxRecursionDepth = 2
        });
        core.StartSession();

        var response = await core.HandleAsync("запрос без timeout",
            new BoundedExecutionOptions { TimeoutSeconds = 0 });

        Assert.NotNull(response);
        Assert.NotEqual("timeout", response.Mode);
    }

    [Fact]
    public async Task HandleAsync_DefaultOptions_UsesConfigDefaults()
    {
        var core = CreateCore(new AgentConfig
        {
            MaxToolIterations = 5,
            MaxWallClockTimeoutSeconds = 60,
            MaxRecursionDepth = 3
        });
        core.StartSession();

        // null options → use config defaults
        var response = await core.HandleAsync("запрос", (BoundedExecutionOptions?)null);

        Assert.Equal("ответ агента", response.Answer);
    }

    // ---- AgentConfig defaults ----

    [Fact]
    public void AgentConfig_BoundedExecution_HasSensibleDefaults()
    {
        var cfg = new AgentConfig();
        Assert.Equal(3, cfg.MaxToolIterations);
        Assert.Equal(120, cfg.MaxWallClockTimeoutSeconds);
        Assert.Equal(2, cfg.MaxRecursionDepth);
    }

    [Fact]
    public void AgentConfig_BoundedExecution_CanBeOverridden()
    {
        var cfg = new AgentConfig
        {
            MaxToolIterations = 10,
            MaxWallClockTimeoutSeconds = 300,
            MaxRecursionDepth = 5
        };
        Assert.Equal(10, cfg.MaxToolIterations);
        Assert.Equal(300, cfg.MaxWallClockTimeoutSeconds);
        Assert.Equal(5, cfg.MaxRecursionDepth);
    }

    // ---- LinkedCancellationTokenSource ----

    [Fact]
    public void LinkedCts_NoTimeout_ExternalTokenUsed()
    {
        using var cts = new CancellationTokenSource();
        using var linked = new LinkedCancellationTokenSource(cts.Token);

        Assert.False(linked.IsWallClockTimeout);
        Assert.Null(linked.Timeout);
        Assert.False(linked.Token.IsCancellationRequested);
    }

    [Fact]
    public void LinkedCts_WithTimeout_TimeoutTriggered()
    {
        using var linked = new LinkedCancellationTokenSource(
            CancellationToken.None,
            TimeSpan.FromMilliseconds(20));

        Assert.True(linked.Timeout > TimeSpan.Zero);
        Thread.Sleep(50);
        Assert.True(linked.Token.IsCancellationRequested);
        Assert.True(linked.IsWallClockTimeout);
    }

    [Fact]
    public void LinkedCts_ExternalCancellation_DoesNotSetWallClockTimeout()
    {
        using var cts = new CancellationTokenSource();
        using var linked = new LinkedCancellationTokenSource(cts.Token,
            TimeSpan.FromMinutes(5));

        cts.Cancel();
        Assert.True(linked.Token.IsCancellationRequested);
        Assert.False(linked.IsWallClockTimeout);
    }

    [Fact]
    public void LinkedCts_Elapsed_TracksTime()
    {
        using var linked = new LinkedCancellationTokenSource(
            CancellationToken.None,
            TimeSpan.FromSeconds(30));

        Thread.Sleep(50);
        Assert.True(linked.Elapsed.TotalMilliseconds >= 50);
        Assert.True(linked.Elapsed < TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void LinkedCts_ZeroTimeout_NoTimeoutCtsCreated()
    {
        using var linked = new LinkedCancellationTokenSource(
            CancellationToken.None,
            TimeSpan.Zero);

        Assert.Null(linked.Timeout);
        Assert.Equal(CancellationToken.None, linked.Token);
    }

    [Fact]
    public void LinkedCts_Dispose_DoesNotThrow()
    {
        var linked = new LinkedCancellationTokenSource(
            CancellationToken.None,
            TimeSpan.FromSeconds(30));
        linked.Dispose();

        var ex = Record.Exception(() => linked.Dispose());
        Assert.Null(ex);
    }

    // ---- StubLlmClient ----

    private sealed class StubLlmClient : ILLMClient
    {
        private readonly string _responseText;
        private readonly string _confidence;

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
