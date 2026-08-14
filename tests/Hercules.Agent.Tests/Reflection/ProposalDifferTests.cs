using Hercules.Reflection;
using Hercules.Skills;
using Hercules.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Reflection;

/// <summary>
/// Тесты ProposalDiffer: versioned skill diff computation.
/// </summary>
public class ProposalDifferTests
{
    private readonly ProposalDiffer _differ = new(NullLogger<ProposalDiffer>.Instance);

    private static Skill MakeSkill(int version, List<string> phrases, string prompt = "Test prompt.")
    {
        return new Skill
        {
            Meta = new SkillMeta
            {
                Id = "test-skill",
                Name = "Test Skill",
                Version = version,
                PhraseReceivers = phrases
            },
            Description = "# Test Skill",
            Prompt = prompt
        };
    }

    [Fact]
    public void ComputeDiff_AddedPhrases_DetectsCorrectly()
    {
        var current = MakeSkill(1, ["hello", "world"]);
        var proposedPhrases = new List<string> { "hello", "world", "new-phrase", "another" };

        var diff = _differ.ComputeDiff(current, current.Prompt, proposedPhrases);

        Assert.Equal(2, diff.AddedPhrases.Count);
        Assert.Contains("new-phrase", diff.AddedPhrases);
        Assert.Contains("another", diff.AddedPhrases);
        Assert.Empty(diff.RemovedPhrases); // all current phrases present in proposed
        Assert.Equal(2, diff.UnchangedPhrases.Count);
    }

    [Fact]
    public void ComputeDiff_RemovedPhrases_DetectsCorrectly()
    {
        var current = MakeSkill(1, ["hello", "world", "remove-me"]);
        var proposedPhrases = new List<string> { "hello", "world" };

        var diff = _differ.ComputeDiff(current, current.Prompt, proposedPhrases);

        Assert.Empty(diff.AddedPhrases);
        Assert.Single(diff.RemovedPhrases);
        Assert.Equal("remove-me", diff.RemovedPhrases[0]);
        Assert.Equal(2, diff.UnchangedPhrases.Count);
    }

    [Fact]
    public void ComputeDiff_VersionIncrement_SetsNextVersion()
    {
        var current = MakeSkill(3, ["hello"]);
        var diff = _differ.ComputeDiff(current, current.Prompt, new List<string> { "hello", "new" });

        Assert.Equal(3, diff.CurrentVersion);
        Assert.Equal(4, diff.NextVersion);
        Assert.Equal(3, diff.RollbackVersion);
    }

    [Fact]
    public void ComputeDiff_Unchanged_ReturnsEmptyDiffSummary()
    {
        var current = MakeSkill(1, ["hello", "world"], "Same prompt.");
        var diff = _differ.ComputeDiff(current, "Same prompt.", new List<string> { "hello", "world" });

        Assert.Empty(diff.AddedPhrases);
        Assert.Empty(diff.RemovedPhrases);
        Assert.Equal("no changes", diff.DiffSummary);
        Assert.Empty(diff.PromptDiffLines);
    }

    [Fact]
    public void ComputeDiff_PromptChanges_ComputesDiffLines()
    {
        var current = MakeSkill(1, ["hello"], "Line 1.\nLine 2.\nLine 3.");
        var proposed = "Line 1.\nModified Line.\nLine 3.";

        var diff = _differ.ComputeDiff(current, proposed, current.Meta.PhraseReceivers);

        Assert.NotEmpty(diff.PromptDiffLines);
        Assert.Contains(diff.PromptDiffLines, l => l.StartsWith("- "));
        Assert.Contains(diff.PromptDiffLines, l => l.StartsWith("+ "));
    }

    [Fact]
    public void EstimateScoreGain_AddsPhrases_PositiveGain()
    {
        var current = MakeSkill(1, ["hello"]);
        var diff = _differ.ComputeDiff(current, current.Prompt,
            new List<string> { "hello", "new1", "new2", "new3" });

        double gain = _differ.EstimateScoreGain(diff, 0.5);

        Assert.True(gain > 0.5);
        Assert.True(gain <= 0.95);
    }

    [Fact]
    public void EstimateScoreGain_RemovesPhrases_NegativeGain()
    {
        var current = MakeSkill(1, ["a", "b", "c", "d", "e"]);
        var diff = _differ.ComputeDiff(current, current.Prompt, new List<string> { "a" });

        double gain = _differ.EstimateScoreGain(diff, 0.7);

        Assert.True(gain < 0.7);
    }

    [Fact]
    public void EstimateScoreGain_NeverExceedsNinetyFive()
    {
        var current = MakeSkill(1, ["hello"]);
        var diff = _differ.ComputeDiff(current, "New very long improved prompt.",
            new List<string> { "hello", "a", "b", "c", "d", "e", "f", "g", "h" });

        double gain = _differ.EstimateScoreGain(diff, 0.94);

        Assert.True(gain <= 0.95);
    }
}
