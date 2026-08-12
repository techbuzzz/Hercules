using Hercules.Storage;

namespace Hercules.Skills.Routing.ScoringComponents;

/// <summary>
///     Historical quality scorer.
///     Weights the final score by the skill's historical success rate and last eval score.
///     Score = SuccessRate (from usage history) × LastEvaluationScore (if available, else 1.0).
///     New skills with no history get a neutral 1.0.
/// </summary>
public sealed class HistoricalQualityScorer : ISkillScorer
{
    public HistoricalQualityScorer()
    {
    }

    public string ComponentName => "quality";

    /// <summary>
    ///     Weight for combining with other scores.
    /// </summary>
    public double Weight { get; set; } = 0.15;

    public ValueTask<ComponentScore?> ScoreAsync(string input, Storage.Skill skill, CancellationToken ct = default)
    {
        var successRate = skill.Meta.SuccessRate;
        var evalScore = skill.Meta.LastEvaluationScore;

        double score;
        string details;

        if (evalScore.HasValue)
        {
            // Blend eval score with success rate (weighted 60/40)
            score = evalScore.Value * 0.6 + successRate * 0.4;
            details = $"eval={evalScore:F3}, successRate={successRate:F3}";
        }
        else
        {
            score = successRate > 0 ? successRate : 1.0;
            details = successRate > 0
                ? $"successRate={successRate:F3}"
                : "no history (neutral 1.0)";
        }

        return new ValueTask<ComponentScore?>(
            new ComponentScore(ComponentName, score, IsEligible: true, details));
    }
}
