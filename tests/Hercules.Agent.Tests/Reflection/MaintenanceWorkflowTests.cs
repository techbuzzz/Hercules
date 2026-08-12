using System.Runtime.CompilerServices;
using Hercules.Agent;
using Hercules.Config;
using Hercules.LLM;
using Hercules.LLM.JsonRepair;
using Hercules.Reflection;
using Hercules.Skills;
using Hercules.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Reflection;

/// <summary>
/// Тесты MaintenanceWorkflow и SelfImprovementService: proposal generation, apply/reject.
/// </summary>
public class MaintenanceWorkflowTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StorageConfig _storageCfg;
    private readonly FileSkillRepository _repo;
    private readonly SkillManager _skillManager;
    private readonly ProposalStore _store;
    private readonly ProposalDiffer _differ;
    private readonly SelfImprovementConfig _config;
    private readonly SelfImprovementService _service;

    public MaintenanceWorkflowTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-maint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _storageCfg = new StorageConfig { DataRoot = _tempDir };

        _repo = new FileSkillRepository(_storageCfg, NullLogger<FileSkillRepository>.Instance);
        _skillManager = new SkillManager(
            _repo,
            new StubLlmClient("test"),
            new AgentConfig(),
            new JsonRepairService());

        _store = new ProposalStore(_storageCfg, NullLogger<ProposalStore>.Instance);
        _differ = new ProposalDiffer(NullLogger<ProposalDiffer>.Instance);
        _config = new SelfImprovementConfig
        {
            Enabled = true,
            MinSuccessRateThreshold = 0.5,
            RequireApprovalForProd = true,
            MaxProposalsPerDay = 5,
            AnonymizeData = false
        };

        // Create a stub LLM for the workflow
        var stubLlm = new StubLlmClient(
            """
            {
              "summary": "Test summary",
              "proposed_prompt": "Improved prompt with better instructions.",
              "proposed_phrases": ["new-phrase"]
            }
            """);

        var logger = NullLogger<MaintenanceWorkflow>.Instance;
        var workflow = new MaintenanceWorkflow(
            stubLlm, _skillManager, _differ, _store, null, _config, logger);

        _service = new SelfImprovementService(
            workflow,
            _store,
            _skillManager,
            new SkillLifecycleService(
                _skillManager,
                new SkillDeprecationManager(_repo, NullLogger<SkillDeprecationManager>.Instance),
                new SkillLifecyclePolicy(),
                new SkillEvaluationEngine(
                    new AgentCore(
                        new StubLlmClient("ok"),
                        new SkillRouter(_skillManager),
                        _skillManager,
                        new MemoryManager(new MemoryStore(_storageCfg), new StubLlmClient("mem")),
                        new SqliteSessionStore(_storageCfg),
                        new AgentConfig(),
                        NullLogger<AgentCore>.Instance,
                        new JsonRepairService()),
                    NullLogger<SkillEvaluationEngine>.Instance),
                null, null),
            null, null, null, _config,
            NullLogger<SelfImprovementService>.Instance);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    [Fact]
    public void RunMaintenance_DisabledConfig_ReturnsNull()
    {
        var disabledCfg = new SelfImprovementConfig { Enabled = false };
        var stubLlm = new StubLlmClient("{}");
        var differ = new ProposalDiffer(NullLogger<ProposalDiffer>.Instance);
        var logger = NullLogger<MaintenanceWorkflow>.Instance;
        var workflow = new MaintenanceWorkflow(
            stubLlm, _skillManager, differ, _store, null, disabledCfg, logger);

        var skill = _skillManager.CreateManual("test-disabled", ["test"], "Prompt.");
        skill.Meta.SuccessRate = 0.3; // below threshold

        var result = workflow.RunAsync(skill.Meta.Id).Result;

        Assert.Null(result);
    }

    [Fact]
    public void RunMaintenance_HighSuccessRate_NoProposal()
    {
        var stubLlm = new StubLlmClient("{}");
        var differ = new ProposalDiffer(NullLogger<ProposalDiffer>.Instance);
        var logger = NullLogger<MaintenanceWorkflow>.Instance;
        var workflow = new MaintenanceWorkflow(
            stubLlm, _skillManager, differ, _store, null, _config, logger);

        // Create skill with success rate above threshold
        var skill = _skillManager.CreateManual("test-high-rate", ["test"], "Prompt.");
        skill.Meta.SuccessRate = 0.8; // above 0.5 threshold

        var result = workflow.RunAsync(skill.Meta.Id).Result;

        Assert.Null(result);
    }

    [Fact]
    public void RunMaintenance_LowSuccessRate_GeneratesProposal()
    {
        var stubLlm = new StubLlmClient(
            """
            {
              "summary": "Skill needs improvement",
              "proposed_prompt": "Improved test prompt.",
              "proposed_phrases": ["test", "improved"]
            }
            """);
        var differ = new ProposalDiffer(NullLogger<ProposalDiffer>.Instance);
        var logger = NullLogger<MaintenanceWorkflow>.Instance;
        var workflow = new MaintenanceWorkflow(
            stubLlm, _skillManager, differ, _store, null, _config, logger);

        // Create skill below threshold
        var skill = _skillManager.CreateManual("test-low-rate", ["test"], "Original prompt.");
        skill.Meta.SuccessRate = 0.3;
        _repo.Save(skill); // persist the updated success rate

        var result = workflow.RunAsync(skill.Meta.Id).Result;

        Assert.NotNull(result);
        Assert.Equal(skill.Meta.Id, result.SkillId);
        Assert.Equal(ProposalStatus.Proposed, result.Status);
        Assert.NotEmpty(result.AnalysisSummary);
        Assert.True(result.ExpectedScoreGain > 0);
        Assert.Equal(skill.Meta.Version, result.RollbackVersion);
    }

    [Fact]
    public void RunMaintenance_SkillNotFound_ReturnsNull()
    {
        var stubLlm = new StubLlmClient("{}");
        var differ = new ProposalDiffer(NullLogger<ProposalDiffer>.Instance);
        var logger = NullLogger<MaintenanceWorkflow>.Instance;
        var workflow = new MaintenanceWorkflow(
            stubLlm, _skillManager, differ, _store, null, _config, logger);

        var result = workflow.RunAsync("non-existent-skill").Result;

        Assert.Null(result);
    }

    [Fact]
    public void ApplyProposal_ProposalNotFound_ReturnsFailure()
    {
        var result = _service.ApplyProposalAsync("non-existent", "user").Result;

        Assert.False(result.Success);
        Assert.Equal("Proposal not found", result.Error);
    }

    [Fact]
    public void RejectProposal_ExistingProposal_UpdatesStatus()
    {
        // Create a skill and generate a proposal
        var stubLlm = new StubLlmClient(
            """{"summary": "Test", "proposed_prompt": "Test.", "proposed_phrases": ["test"]}""");
        var differ = new ProposalDiffer(NullLogger<ProposalDiffer>.Instance);
        var logger = NullLogger<MaintenanceWorkflow>.Instance;
        var workflow = new MaintenanceWorkflow(
            stubLlm, _skillManager, differ, _store, null, _config, logger);

        var skill = _skillManager.CreateManual("test-reject", ["test"], "Original.");
        skill.Meta.SuccessRate = 0.3;
        _repo.Save(skill);

        var proposal = workflow.RunAsync(skill.Meta.Id).Result;
        Assert.NotNull(proposal);

        var ok = _service.RejectProposal(proposal.Id, "user", "Not needed right now");

        Assert.True(ok);

        var reloaded = _store.Load(proposal.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(ProposalStatus.Rejected, reloaded.Status);
        Assert.Equal("Not needed right now", reloaded.RejectionReason);
        Assert.Equal("user", reloaded.ResolvedBy);
    }

    [Fact]
    public void GetProposals_Empty_ReturnsEmptyList()
    {
        var proposals = _service.GetProposals(10);

        Assert.Empty(proposals);
    }

    [Fact]
    public void GetProposals_AfterRun_ReturnsProposal()
    {
        var stubLlm = new StubLlmClient(
            """{"summary": "Test", "proposed_prompt": "Test.", "proposed_phrases": ["test"]}""");
        var differ = new ProposalDiffer(NullLogger<ProposalDiffer>.Instance);
        var logger = NullLogger<MaintenanceWorkflow>.Instance;
        var workflow = new MaintenanceWorkflow(
            stubLlm, _skillManager, differ, _store, null, _config, logger);

        var skill = _skillManager.CreateManual("test-list", ["test"], "Original.");
        skill.Meta.SuccessRate = 0.3;
        _repo.Save(skill);

        workflow.RunAsync(skill.Meta.Id).Wait();

        var proposals = _service.GetProposals(10);
        Assert.NotEmpty(proposals);
        Assert.Contains(proposals, p => p.SkillId == skill.Meta.Id);
    }

    [Fact]
    public void RunMaintenance_DailyLimit_StopsAtLimit()
    {
        var limitedCfg = new SelfImprovementConfig
        {
            Enabled = true,
            MinSuccessRateThreshold = 0.5,
            MaxProposalsPerDay = 2
        };

        var stubLlm = new StubLlmClient(
            """{"summary": "Test", "proposed_prompt": "Test.", "proposed_phrases": ["test"]}""");
        var differ = new ProposalDiffer(NullLogger<ProposalDiffer>.Instance);
        var logger = NullLogger<MaintenanceWorkflow>.Instance;
        var workflow = new MaintenanceWorkflow(
            stubLlm, _skillManager, differ, _store, null, limitedCfg, logger);

        // Create 3 skills below threshold.
        // AppendUsage with window=5 and 0 successes → TotalUses=5, SuccessRate=0.0
        for (int i = 0; i < 3; i++)
        {
            var skill = _skillManager.CreateManual($"test-limit-{i}", [$"test{i}"], "Prompt.");
            for (int u = 0; u < 5; u++)
            {
                _repo.AppendUsage(skill.Meta.Id, new SkillUsage { Success = false, Confidence = "low" }, window: 5);
            }
        }

        var proposals = workflow.RunAllAsync("agent").Result;

        Assert.Equal(2, proposals.Count); // limited to 2 per day
    }
}

/// <summary>
/// Stub LLM client for tests — always returns the configured text.
/// </summary>
internal sealed class StubLlmClient(string response) : ILLMClient
{
    public string ProviderName => "stub";
    public string ModelName => "stub";

    public Task<LlmResponse> CompleteAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
    {
        return Task.FromResult(new LlmResponse(response, "stub", "stub-model", 0, 0));
    }

    public Task<LlmResponse> CompleteAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
    {
        return Task.FromResult(new LlmResponse(response, "stub", "stub-model", 0, 0));
    }

    public IAsyncEnumerable<string> StreamAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
    {
        return AsyncEnumerable.Empty<string>();
    }

    public IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
    {
        return AsyncEnumerable.Empty<string>();
    }
}

/// <summary>
/// Minimal IAsyncEnumerable.Empty implementation for test stubs.
/// </summary>
internal static class AsyncEnumerable
{
    public static IAsyncEnumerable<T> Empty<T>()
    {
        return new EmptyAsyncEnumerable<T>();
    }

    private sealed class EmptyAsyncEnumerable<T> : IAsyncEnumerable<T>
    {
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new EmptyAsyncEnumerator<T>();
        }
    }

    private sealed class EmptyAsyncEnumerator<T> : IAsyncEnumerator<T>
    {
        public T Current => default!;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(false);
    }
}
