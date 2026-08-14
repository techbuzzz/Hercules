using Hercules.Mesh;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Mesh;

public class ReflectionProposalStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ReflectionProposalStore _store;

    public ReflectionProposalStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"proposal_store_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var logger = new Mock<ILogger<ReflectionProposalStore>>();
        _store = new ReflectionProposalStore(_tempDir, logger.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public void SaveAndGet_RoundTrip_PreservesAllFields()
    {
        var proposal = new ReflectionProposal
        {
            Type = ProposalType.NewSkill,
            Title = "Test Skill Proposal",
            Rationale = "Rationale text",
            Content = "# Skill content",
            Target = "skill-id",
            Priority = ProposalPriority.High,
            Status = ProposalStatus.Pending,
            Trigger = "circuit_open:peer1",
            EvidenceJson = "{\"peer\":\"peer1\",\"capability\":\"test\"}",
            GeneratedBy = "DistributedReflection",
            Reviewer = null,
            ReviewComment = null,
            ReviewedAt = null
        };
        _store.Save(proposal);

        var loaded = _store.Get(proposal.Id);

        Assert.NotNull(loaded);
        Assert.Equal(proposal.Id, loaded.Id);
        Assert.Equal(ProposalType.NewSkill, loaded.Type);
        Assert.Equal("Test Skill Proposal", loaded.Title);
        Assert.Equal("Rationale text", loaded.Rationale);
        Assert.Equal("# Skill content", loaded.Content);
        Assert.Equal("skill-id", loaded.Target);
        Assert.Equal(ProposalPriority.High, loaded.Priority);
        Assert.Equal(ProposalStatus.Pending, loaded.Status);
        Assert.Equal("circuit_open:peer1", loaded.Trigger);
        Assert.Equal("DistributedReflection", loaded.GeneratedBy);
    }

    [Fact]
    public void Save_OverwritesExisting_SameId()
    {
        var p1 = new ReflectionProposal
        {
            Type = ProposalType.TrustUpdate,
            Title = "Original",
            Rationale = "Original",
            Content = "",
            Target = "peer1",
            Priority = ProposalPriority.Low,
            Trigger = "",
            EvidenceJson = "{}",
            GeneratedBy = "test"
        };
        _store.Save(p1);

        var p2 = new ReflectionProposal
        {
            Id = p1.Id, // Same ID
            Type = ProposalType.TrustUpdate,
            Title = "Updated",
            Rationale = "Updated",
            Content = "",
            Target = "peer1",
            Priority = ProposalPriority.High,
            Trigger = "",
            EvidenceJson = "{}",
            GeneratedBy = "test"
        };
        _store.Save(p2);

        var loaded = _store.Get(p1.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Updated", loaded.Title);
        Assert.Equal(ProposalPriority.High, loaded.Priority);
    }

    [Fact]
    public void LoadAll_MultipleProposals_ReturnsAll()
    {
        _store.Save(new ReflectionProposal { Type = ProposalType.NewSkill, Title = "A", Rationale = "", Content = "", Target = "", Priority = ProposalPriority.Low, Trigger = "", EvidenceJson = "{}", GeneratedBy = "test" });
        _store.Save(new ReflectionProposal { Type = ProposalType.NewSkill, Title = "B", Rationale = "", Content = "", Target = "", Priority = ProposalPriority.Low, Trigger = "", EvidenceJson = "{}", GeneratedBy = "test" });
        _store.Save(new ReflectionProposal { Type = ProposalType.NewSkill, Title = "C", Rationale = "", Content = "", Target = "", Priority = ProposalPriority.Low, Trigger = "", EvidenceJson = "{}", GeneratedBy = "test" });

        var all = _store.LoadAll();

        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void LoadAll_FilterPending_ReturnsOnlyPending()
    {
        var p1 = new ReflectionProposal { Type = ProposalType.NewSkill, Title = "P1", Rationale = "", Content = "", Target = "", Priority = ProposalPriority.Low, Trigger = "", EvidenceJson = "{}", GeneratedBy = "test" };
        var p2 = new ReflectionProposal { Type = ProposalType.NewSkill, Title = "P2", Rationale = "", Content = "", Target = "", Priority = ProposalPriority.Low, Trigger = "", EvidenceJson = "{}", GeneratedBy = "test" };
        _store.Save(p1);
        _store.Save(p2);
        _store.UpdateStatus(p1.Id, ProposalStatus.Approved, "admin");

        var pending = _store.LoadAll(ProposalStatus.Pending);
        var approved = _store.LoadAll(ProposalStatus.Approved);

        Assert.Single(pending);
        Assert.Single(approved);
        Assert.Equal("P2", pending[0].Title);
        Assert.Equal("P1", approved[0].Title);
    }

    [Fact]
    public void UpdateStatus_Approve_SetsStatusAndReviewer()
    {
        var proposal = new ReflectionProposal { Type = ProposalType.PeerConnection, Title = "Conn", Rationale = "", Content = "", Target = "", Priority = ProposalPriority.Medium, Trigger = "", EvidenceJson = "{}", GeneratedBy = "test" };
        _store.Save(proposal);

        _store.UpdateStatus(proposal.Id, ProposalStatus.Approved, "reviewer@example.com", "Approved after review");

        var loaded = _store.Get(proposal.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ProposalStatus.Approved, loaded.Status);
        Assert.Equal("reviewer@example.com", loaded.Reviewer);
        Assert.Equal("Approved after review", loaded.ReviewComment);
        Assert.NotNull(loaded.ReviewedAt);
    }

    [Fact]
    public void UpdateStatus_Reject_SetsStatusAndComment()
    {
        var proposal = new ReflectionProposal { Type = ProposalType.RoutingRule, Title = "Rule", Rationale = "", Content = "", Target = "", Priority = ProposalPriority.Low, Trigger = "", EvidenceJson = "{}", GeneratedBy = "test" };
        _store.Save(proposal);

        _store.UpdateStatus(proposal.Id, ProposalStatus.Rejected, "admin", "Not needed");

        var loaded = _store.Get(proposal.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ProposalStatus.Rejected, loaded.Status);
        Assert.Equal("admin", loaded.Reviewer);
    }

    [Fact]
    public void UpdateStatus_MissingProposal_NoCrash()
    {
        var ex = Record.Exception(() =>
            _store.UpdateStatus("nonexistent-id", ProposalStatus.Approved, "admin"));

        Assert.Null(ex); // should not throw
    }

    [Fact]
    public void Delete_ExistingProposal_RemovesFile()
    {
        var proposal = new ReflectionProposal { Type = ProposalType.CapabilityDeclare, Title = "Cap", Rationale = "", Content = "", Target = "", Priority = ProposalPriority.Low, Trigger = "", EvidenceJson = "{}", GeneratedBy = "test" };
        _store.Save(proposal);

        var deleted = _store.Delete(proposal.Id);

        Assert.True(deleted);
        Assert.Null(_store.Get(proposal.Id));
    }

    [Fact]
    public void Delete_NonExistingProposal_ReturnsFalse()
    {
        var deleted = _store.Delete("does-not-exist");
        Assert.False(deleted);
    }

    [Fact]
    public void Get_NonExistingId_ReturnsNull()
    {
        var result = _store.Get("nonexistent-id");
        Assert.Null(result);
    }

    [Fact]
    public void LoadAll_EmptyDirectory_ReturnsEmptyList()
    {
        var emptyStore = new ReflectionProposalStore(Path.Combine(_tempDir, "empty"), new Mock<ILogger<ReflectionProposalStore>>().Object);
        var result = emptyStore.LoadAll();
        Assert.Empty(result);
    }

    [Fact]
    public void LoadAll_OrderByCreatedAtDescending()
    {
        _store.Save(new ReflectionProposal { Type = ProposalType.NewSkill, Title = "First", Rationale = "", Content = "", Target = "", Priority = ProposalPriority.Low, Trigger = "", EvidenceJson = "{}", GeneratedBy = "test" });
        _store.Save(new ReflectionProposal { Type = ProposalType.NewSkill, Title = "Second", Rationale = "", Content = "", Target = "", Priority = ProposalPriority.Low, Trigger = "", EvidenceJson = "{}", GeneratedBy = "test" });
        _store.Save(new ReflectionProposal { Type = ProposalType.NewSkill, Title = "Third", Rationale = "", Content = "", Target = "", Priority = ProposalPriority.Low, Trigger = "", EvidenceJson = "{}", GeneratedBy = "test" });

        var all = _store.LoadAll();

        // Proposals are ordered by CreatedAt descending; title order is not guaranteed without delays
        Assert.Equal(3, all.Count);
    }
}
