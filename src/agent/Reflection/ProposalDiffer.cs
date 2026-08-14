using Hercules.Agent;
using Hercules.Skills;
using Hercules.Storage;
using Microsoft.Extensions.Logging;

namespace Hercules.Reflection;

/// <summary>
///     Вычисляет versioned diff между текущим и предлагаемым состоянием навыка.
/// </summary>
public sealed class ProposalDiffer
{
    private readonly ILogger<ProposalDiffer> _logger;

    public ProposalDiffer(ILogger<ProposalDiffer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Вычислить diff между текущим навыком и предлагаемым состоянием.
    /// </summary>
    public SkillDiff ComputeDiff(
        Skill current,
        string proposedPrompt,
        List<string> proposedPhrases)
    {
        var diff = new SkillDiff
        {
            SkillId = current.Meta.Id,
            CurrentVersion = current.Meta.Version,
            NextVersion = current.Meta.Version + 1,
            RollbackVersion = current.Meta.Version
        };

        var currentPhrases = current.Meta.PhraseReceivers
            .Select(p => p.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var proposedSet = proposedPhrases
            .Select(p => p.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Added phrases: in proposed but not in current
        diff.AddedPhrases = proposedPhrases
            .Where(p => !currentPhrases.Contains(p.Trim().ToLowerInvariant()))
            .ToList();

        // Removed phrases: in current but not in proposed
        diff.RemovedPhrases = current.Meta.PhraseReceivers
            .Where(p => !proposedSet.Contains(p.Trim().ToLowerInvariant()))
            .ToList();

        // Unchanged phrases: in both
        diff.UnchangedPhrases = current.Meta.PhraseReceivers
            .Where(p => proposedSet.Contains(p.Trim().ToLowerInvariant()))
            .ToList();

        // Prompt diff
        diff.PromptDiffLines = ComputePromptDiff(current.Prompt, proposedPrompt);

        // Summary
        var summaryParts = new List<string>();
        if (diff.AddedPhrases.Count > 0)
        {
            summaryParts.Add($"+{diff.AddedPhrases.Count} phrases");
        }

        if (diff.RemovedPhrases.Count > 0)
        {
            summaryParts.Add($"-{diff.RemovedPhrases.Count} phrases");
        }

        if (diff.PromptDiffLines.Count > 0)
        {
            summaryParts.Add($"{diff.PromptDiffLines.Count} prompt changes");
        }

        diff.DiffSummary = summaryParts.Count > 0
            ? string.Join(", ", summaryParts)
            : "no changes";

        _logger.LogDebug(
            "Computed diff for skill '{SkillId}': v{Current}→v{Next}, summary={Summary}",
            current.Meta.Id, diff.CurrentVersion, diff.NextVersion, diff.DiffSummary);

        return diff;
    }

    /// <summary>
    ///     Вычислить ожидаемый gain на основе diff.
    /// </summary>
    public double EstimateScoreGain(SkillDiff diff, double currentScore)
    {
        // Naive heuristic: adding phrases slightly improves coverage
        double gain = 0.0;

        if (diff.AddedPhrases.Count > 0)
        {
            gain += Math.Min(0.05, diff.AddedPhrases.Count * 0.005);
        }

        if (diff.RemovedPhrases.Count > 0)
        {
            gain -= Math.Min(0.03, diff.RemovedPhrases.Count * 0.005);
        }

        if (diff.PromptDiffLines.Count > 0)
        {
            gain += Math.Min(0.04, diff.PromptDiffLines.Count * 0.003);
        }

        // Never predict > 95% success rate
        return Math.Min(0.95, Math.Max(0, Math.Round(currentScore + gain, 4)));
    }

    private static List<string> ComputePromptDiff(string current, string proposed)
    {
        if (current == proposed)
        {
            return new List<string>();
        }

        var result = new List<string>();
        var currentLines = current.Split('\n');
        var proposedLines = proposed.Split('\n');

        // Simple line-by-line diff using LCS approximation
        // Add lines that differ as "-old" / "+new" pairs
        int maxLen = Math.Max(currentLines.Length, proposedLines.Length);
        for (int i = 0; i < maxLen; i++)
        {
            string? oldLine = i < currentLines.Length ? currentLines[i] : null;
            string? newLine = i < proposedLines.Length ? proposedLines[i] : null;

            if (oldLine == newLine)
            {
                continue;
            }

            if (oldLine is not null)
            {
                result.Add($"- {TruncLine(oldLine)}");
            }

            if (newLine is not null)
            {
                result.Add($"+ {TruncLine(newLine)}");
            }
        }

        return result.Take(20).ToList(); // Limit to 20 diff lines
    }

    private static string TruncLine(string s)
    {
        const int max = 120;
        return s.Length <= max ? s : s[..max] + "…";
    }
}
