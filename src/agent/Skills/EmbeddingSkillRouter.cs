using Hercules.Agent;
using Hercules.Config;
using Hercules.Skills.Routing;
using Hercules.Skills.Routing.Deterministic;
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
///     Fallback chain:
///       1. Semantic routing (embedding + scoring engine)
///       2. Deterministic routing (keyword + tags + input types, task_023)
///       3. Legacy keyword-only SkillRouter
/// </summary>
public sealed class EmbeddingSkillRouter
{
    private readonly ISkillScoringEngine? _scoringEngine;
    private readonly SkillRouter _legacyRouter;
    private readonly SkillManager _skills;
    private readonly Phase2Config _phase2Config;
    private readonly IDeterministicRouter? _deterministicRouter;

    public EmbeddingSkillRouter(
        SkillManager skills,
        Phase2Config phase2Config,
        SkillRouter legacyRouter,
        ISkillScoringEngine? scoringEngine,
        IDeterministicRouter? deterministicRouter)
    {
        _skills = skills ?? throw new ArgumentNullException(nameof(skills));
        _phase2Config = phase2Config ?? throw new ArgumentNullException(nameof(phase2Config));
        _legacyRouter = legacyRouter ?? throw new ArgumentNullException(nameof(legacyRouter));
        _scoringEngine = scoringEngine;
        _deterministicRouter = deterministicRouter;
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

        // ── Step 1: Semantic routing ────────────────────────────────────────
        if (_phase2Config.SemanticRoutingEnabled && _scoringEngine is not null)
        {
            var best = await _scoringEngine.ScoreBestAsync(input, ct);

            if (best is not null && best.IsEligible)
            {
                // Embedding score very low but keyword matched → prefer keyword
                if (best.ComponentScores.TryGetValue("embedding", out var embScore) &&
                    embScore.Value < 0.05 &&
                    best.ComponentScores.TryGetValue("lexical", out var lexScore) &&
                    lexScore.Value > 0)
                {
                    return new SemanticRouteResult(best.Skill, best.OverallScore, "keyword");
                }

                return new SemanticRouteResult(best.Skill, best.OverallScore, best.PrimaryMethod);
            }

            // Semantic routing failed → try deterministic fallback
            var detResult = TryDeterministicRouting(input);
            if (detResult.IsSkill)
            {
                return detResult;
            }

            // Deterministic also failed → try legacy keyword
            return UseKeywordFallback ? KeywordFallback(input)
                                      : new SemanticRouteResult(null, 0, "none");
        }

        // ── Step 2: Semantic routing disabled → Deterministic or legacy ─────
        if (_phase2Config.DeterministicRouting.FallbackMode == DeterministicFallbackMode.Always)
        {
            var detResult = TryDeterministicRouting(input);
            if (detResult.IsSkill)
            {
                return detResult;
            }
            return new SemanticRouteResult(null, 0, "none");
        }

        // OnNoEmbedding or Never with semantic disabled → legacy keyword
        if (UseKeywordFallback)
        {
            return KeywordFallback(input);
        }

        return new SemanticRouteResult(null, 0, "none");
    }

    /// <summary>
    ///     Score all skills using the semantic engine and return ranked results.
    /// </summary>
    public async Task<IReadOnlyList<SkillScoreResult>> ScoreAllAsync(
        string input, CancellationToken ct = default)
    {
        if (_phase2Config.SemanticRoutingEnabled && _scoringEngine is not null)
        {
            return await _scoringEngine.ScoreAllAsync(input, ct);
        }

        // Legacy / deterministic — return single-item list
        var detResult = TryDeterministicRouting(input);
        if (detResult.IsSkill)
        {
            return new[]
            {
                new SkillScoreResult
                {
                    Skill = detResult.MatchedSkill!,
                    OverallScore = detResult.Score,
                    IsEligible = true,
                    PrimaryMethod = detResult.Method
                }
            };
        }

        var legacy = KeywordFallback(input);
        if (!legacy.IsSkill) return [];
        return new[]
        {
            new SkillScoreResult
            {
                Skill = legacy.MatchedSkill!,
                OverallScore = legacy.Score,
                IsEligible = true,
                PrimaryMethod = "keyword"
            }
        };
    }

    /// <summary>
    ///     Try deterministic routing if enabled and available.
    /// </summary>
    private SemanticRouteResult TryDeterministicRouting(string input)
    {
        var mode = _phase2Config.DeterministicRouting.FallbackMode;
        if (mode == DeterministicFallbackMode.Never || _deterministicRouter is null)
        {
            return new SemanticRouteResult(null, 0, "none");
        }

        var det = _deterministicRouter.Route(input);
        if (!det.IsSkill)
        {
            return new SemanticRouteResult(null, 0, "none");
        }

        var primaryMethod = det.MatchedMethods.FirstOrDefault() ?? "deterministic";
        return new SemanticRouteResult(det.MatchedSkill, det.Score, primaryMethod);
    }

    private SemanticRouteResult KeywordFallback(string input)
    {
        var legacy = _legacyRouter.Route(input);
        return new SemanticRouteResult(legacy.MatchedSkill, legacy.Score, "keyword");
    }
}
