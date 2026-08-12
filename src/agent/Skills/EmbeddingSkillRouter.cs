using Hercules.Agent;
using Hercules.Config;
using Hercules.Skills.Routing;
using Hercules.Storage;

namespace Hercules.Skills;

/// <summary>
///     Результат семантической маршрутизации: навык + оценка сходства.
/// </summary>
public readonly record struct SemanticRouteResult(Skill? MatchedSkill, double Score, string Method)
{
    public bool IsSkill => MatchedSkill is not null;
}

/// <summary>
///     Семантический маршрутизатор навыков.
///     Wraps SkillScoringEngine for hybrid scoring (task_022).
///     Fallback: when SemanticRoutingEnabled=false, uses legacy keyword-only SkillRouter.
/// </summary>
public sealed class EmbeddingSkillRouter
{
    private readonly ISkillScoringEngine? _scoringEngine;
    private readonly SkillRouter _legacyRouter;
    private readonly SkillManager _skills;
    private readonly Phase2Config _phase2Config;

    public EmbeddingSkillRouter(
        SkillManager skills,
        Phase2Config phase2Config,
        SkillRouter legacyRouter,
        ISkillScoringEngine? scoringEngine)
    {
        _skills = skills ?? throw new ArgumentNullException(nameof(skills));
        _phase2Config = phase2Config ?? throw new ArgumentNullException(nameof(phase2Config));
        _legacyRouter = legacyRouter ?? throw new ArgumentNullException(nameof(legacyRouter));
        _scoringEngine = scoringEngine;
    }

    /// <summary>
    ///     Similarity threshold for embedding-based matching (passed through Phase2Config).
    /// </summary>
    public double SimilarityThreshold => _phase2Config.SimilarityThreshold;

    /// <summary>
    ///     Whether keyword fallback is used when embedding score is below threshold.
    /// </summary>
    public bool UseKeywordFallback => _phase2Config.KeywordFallback;

    /// <summary>
    ///     Route a request using the semantic scoring engine.
    ///     Returns the best matching skill and the routing method.
    /// </summary>
    public async Task<SemanticRouteResult> RouteAsync(string input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new SemanticRouteResult(null, 0, "none");
        }

        // Fall back to legacy keyword routing if semantic routing is disabled
        if (!_phase2Config.SemanticRoutingEnabled || _scoringEngine is null)
        {
            return KeywordFallback(input);
        }

        var best = await _scoringEngine.ScoreBestAsync(input, ct);

        if (best is null || !best.IsEligible)
        {
            // Try keyword fallback as last resort
            return UseKeywordFallback ? KeywordFallback(input) : new SemanticRouteResult(null, 0, "none");
        }

        // If embedding score is very low but keyword matched → prefer keyword method
        if (best.ComponentScores.TryGetValue("embedding", out var embScore) &&
            embScore.Value < 0.05 &&
            best.ComponentScores.TryGetValue("lexical", out var lexScore) &&
            lexScore.Value > 0)
        {
            return new SemanticRouteResult(best.Skill, best.OverallScore, "keyword");
        }

        return new SemanticRouteResult(best.Skill, best.OverallScore, best.PrimaryMethod);
    }

    /// <summary>
    ///     Score all skills using the semantic engine and return ranked results.
    /// </summary>
    public async Task<IReadOnlyList<SkillScoreResult>> ScoreAllAsync(
        string input, CancellationToken ct = default)
    {
        if (!_phase2Config.SemanticRoutingEnabled || _scoringEngine is null)
        {
            // Return legacy scoring as single-item list
            var legacy = KeywordFallback(input);
            if (!legacy.IsSkill) return [];
            return new[]
            {
                new SkillScoreResult
                {
                    Skill = legacy.MatchedSkill!,
                    OverallScore = legacy.Score,
                    IsEligible = true,
                    PrimaryMethod = legacy.Method
                }
            };
        }

        return await _scoringEngine.ScoreAllAsync(input, ct);
    }

    private SemanticRouteResult KeywordFallback(string input)
    {
        var legacy = _legacyRouter.Route(input);
        return new SemanticRouteResult(legacy.MatchedSkill, legacy.Score, "keyword");
    }
}
