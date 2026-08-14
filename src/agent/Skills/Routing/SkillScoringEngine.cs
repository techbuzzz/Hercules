using Hercules.Agent;
using Hercules.Config;
using Hercules.Skills.Quality;
using Hercules.Skills.Routing.ScoringComponents;
using Hercules.Storage;

namespace Hercules.Skills.Routing;

/// <summary>
///     Combines multiple scoring components (embedding, lexical, schema, quality, latency, policy)
///     into a single weighted score per skill.
///     Ranking is based on the combined score; ineligible skills are excluded.
/// </summary>
public interface ISkillScoringEngine
{
    /// <summary>Score all skills for the given input and return ranked results.</summary>
    Task<IReadOnlyList<SkillScoreResult>> ScoreAllAsync(string input, CancellationToken ct = default);

    /// <summary>Score all skills and return only the best match (or null).</summary>
    Task<SkillScoreResult?> ScoreBestAsync(string input, CancellationToken ct = default);
}

/// <summary>
///     Default implementation of ISkillScoringEngine.
///     Runs all registered scorers in parallel and combines their scores.
/// </summary>
public sealed class SkillScoringEngine : ISkillScoringEngine
{
    private readonly SkillManager _skills;
    private readonly List<(ISkillScorer Scorer, double Weight)> _scorers;
    private readonly EmbeddingScorer? _embeddingScorer;

    /// <summary>
    ///     Registered tool names (lowercase) for schema/policy eligibility checks.
    /// </summary>
    private readonly HashSet<string> _registeredToolNames;

    public SkillScoringEngine(
        SkillManager skills,
        Phase2Config phase2Config,
        IEnumerable<ISkillScorer> scorers,
        IEnumerable<string> registeredToolNames,
        EmbeddingScorer? embeddingScorer = null)
    {
        _skills = skills ?? throw new ArgumentNullException(nameof(skills));
        _phase2Config = phase2Config ?? throw new ArgumentNullException(nameof(phase2Config));
        _scorers = new List<(ISkillScorer, double)>();
        _embeddingScorer = embeddingScorer;
        _registeredToolNames = new HashSet<string>(
            registeredToolNames.Select(t => t.ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);

        // Apply weights from config
        var weights = _phase2Config.SkillScoringWeights;
        foreach (var scorer in scorers)
        {
            var w = weights.TryGetValue(scorer.ComponentName, out var configured)
                ? configured
                : 1.0; // Default weight if not configured

            if (scorer is EmbeddingScorer es) es.Weight = w;
            else if (scorer is LexicalScorer ls) ls.Weight = w;
            else if (scorer is SchemaCompatibilityScorer ss) ss.Weight = w;
            else if (scorer is HistoricalQualityScorer hs) hs.Weight = w;
            else if (scorer is LatencyScorer lt) lt.Weight = w;
            else if (scorer is PolicyEligibilityScorer ps) ps.Weight = w;
            else if (scorer is SkillQualityScorer qs) qs.Weight = w;

            _scorers.Add((scorer, w));
        }
    }

    private readonly Phase2Config _phase2Config;

    public async Task<IReadOnlyList<SkillScoreResult>> ScoreAllAsync(string input, CancellationToken ct = default)
    {
        var allSkills = _skills.All();
        var results = new List<SkillScoreResult>(allSkills.Count);

        // Refresh embedding cache if using embedding scorer
        if (_embeddingScorer is not null)
        {
            _embeddingScorer.InvalidateCache();
            await _embeddingScorer.RefreshCacheIfNeeded(ct);
        }

        foreach (Skill skill in allSkills)
        {
            var result = await ScoreSkillAsync(input, skill, ct);
            if (result is not null)
            {
                results.Add(result);
            }
        }

        // Sort by overall score descending, ineligible last
        return results
            .Where(r => r.IsEligible)
            .OrderByDescending(r => r.OverallScore)
            .ThenByDescending(r => r.Skill.Meta.SuccessRate)
            .ToList();
    }

    public async Task<SkillScoreResult?> ScoreBestAsync(string input, CancellationToken ct = default)
    {
        var ranked = await ScoreAllAsync(input, ct);
        return ranked.FirstOrDefault();
    }

    private async Task<SkillScoreResult?> ScoreSkillAsync(string input, Skill skill, CancellationToken ct)
    {
        var result = new SkillScoreResult { Skill = skill };

        double totalScore = 0;
        double totalWeight = 0;

        foreach (var (scorer, weight) in _scorers)
        {
            if (weight <= 0) continue; // Skip disabled scorers

            var component = await scorer.ScoreAsync(input, skill, ct);

            if (component is null) continue; // Scorer unavailable — skip

            result.ComponentScores[scorer.ComponentName] = component.Value;

            // If any scorer marks as ineligible → skill is excluded
            if (!component.Value.IsEligible)
            {
                result.IsEligible = false;
                result.IneligibilityReason = $"{scorer.ComponentName}: {component.Value.Details}";
                return result;
            }

            totalScore += component.Value.Value * weight;
            totalWeight += weight;
        }

        // Normalize to [0..1]
        result.OverallScore = totalWeight > 0 ? totalScore / totalWeight : 0;

        // Determine primary routing method
        result.PrimaryMethod = DeterminePrimaryMethod(result.ComponentScores);

        return result;
    }

    private static string DeterminePrimaryMethod(Dictionary<string, ComponentScore> scores)
    {
        // Primary method = highest-weighted component that has a non-zero score
        if (scores.TryGetValue("embedding", out var emb) && emb.Value > 0.1)
            return "embedding";
        if (scores.TryGetValue("lexical", out var lex) && lex.Value > 0)
            return "lexical";
        return "hybrid";
    }
}
