using Hercules.Skills.Routing;
using Hercules.Storage;

namespace Hercules.Skills.Quality;

/// <summary>
///     Skill quality scorer for the routing scoring engine.
///     Reads per-version quality metrics and returns a composite score.
///     Task 029: Skill Quality Score.
/// </summary>
public sealed class SkillQualityScorer : ISkillScorer
{
    private readonly ISkillQualityService _qualityService;

    public SkillQualityScorer(ISkillQualityService qualityService)
    {
        _qualityService = qualityService;
    }

    public string ComponentName => "quality";

    /// <summary>
    ///     Weight for combining with other scores (from Phase2Config.SkillScoringWeights).
    /// </summary>
    public double Weight { get; set; } = 0.15;

    public async ValueTask<ComponentScore?> ScoreAsync(
        string input,
        Skill skill,
        CancellationToken ct = default)
    {
        try
        {
            var score = await _qualityService.ComputeScoreAsync(skill.Meta.Id, skill.Meta.Version, ct);

            if (!score.IsReliable)
            {
                // No reliable data: return neutral 1.0 without influencing ranking
                return new ComponentScore(ComponentName, 1.0, IsEligible: true,
                    score.Reason ?? "no quality data");
            }

            return new ComponentScore(
                ComponentName,
                score.CompositeScore,
                IsEligible: true,
                $"score={score.CompositeScore:F3}, calls={score.TotalCalls}, "
                    + $"acc={score.AcceptanceRate:F2}, test={score.TestScore:F2}");
        }
        catch
        {
            // Scorer unavailable → skip (neutral)
            return null;
        }
    }
}
