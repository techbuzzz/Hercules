using Hercules.Agent;
using Hercules.Storage;

namespace Hercules.Skills.Routing.ScoringComponents;

/// <summary>
///     Lexical/keyword matching scorer.
///     Counts how many phrase-receivers of the skill appear in the normalized input.
///     Score = matched_count / total_phrase_receivers (normalized 0..1).
/// </summary>
public sealed class LexicalScorer : ISkillScorer
{
    public LexicalScorer()
    {
    }

    public string ComponentName => "lexical";

    /// <summary>
    ///     Weight for combining with other scores (set by SkillScoringEngine via config).
    /// </summary>
    public double Weight { get; set; } = 0.20;

    public ValueTask<ComponentScore?> ScoreAsync(string input, Skill skill, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new ValueTask<ComponentScore?>(new ComponentScore(ComponentName, 0, IsEligible: true, "empty input"));
        }

        var normalized = SkillRouter.Normalize(input);
        var receivers = skill.Meta.PhraseReceivers
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .ToList();

        if (receivers.Count == 0)
        {
            return new ValueTask<ComponentScore?>(new ComponentScore(ComponentName, 0, IsEligible: true, "no phrase receivers defined"));
        }

        var matched = receivers.Count(r =>
            normalized.Contains(SkillRouter.Normalize(r), StringComparison.Ordinal));

        var score = (double)matched / receivers.Count;

        return new ValueTask<ComponentScore?>(
            new ComponentScore(
                ComponentName,
                score,
                IsEligible: true,
                $"matched={matched}/{receivers.Count}"));
    }
}
