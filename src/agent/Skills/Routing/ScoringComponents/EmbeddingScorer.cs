using Hercules.Agent;
using Hercules.Storage;

namespace Hercules.Skills.Routing.ScoringComponents;

/// <summary>
///     Embedding-based similarity scorer.
///     Computes cosine similarity between the input embedding and the skill's cached embedding.
/// </summary>
public sealed class EmbeddingScorer : ISkillScorer
{
    private readonly IEmbeddingProvider _embedder;
    private readonly SkillManager _skills;
    private readonly double _similarityThreshold;

    // Cache: skillId → embedding vector
    private readonly Dictionary<string, float[]> _embeddingCache = new(StringComparer.OrdinalIgnoreCase);
    private List<string>? _cachedSkillIds;

    public EmbeddingScorer(IEmbeddingProvider embedder, SkillManager skills, double similarityThreshold = 0.35)
    {
        _embedder = embedder ?? throw new ArgumentNullException(nameof(embedder));
        _skills = skills ?? throw new ArgumentNullException(nameof(skills));
        _similarityThreshold = similarityThreshold;
    }

    public string ComponentName => "embedding";

    /// <summary>
    ///     Weight for combining with other scores (set by SkillScoringEngine via config).
    /// </summary>
    public double Weight { get; set; } = 0.40;

    public async ValueTask<ComponentScore?> ScoreAsync(string input, Skill skill, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new ComponentScore(ComponentName, 0, IsEligible: true, "empty input");
        }

        // Get input embedding
        var queryEmbedding = await _embedder.EmbedAsync(input, ct);
        if (IsZeroVector(queryEmbedding))
        {
            return null; // Embedder unavailable — skip this scorer
        }

        // Ensure skill embedding is cached
        await RefreshCacheIfNeeded(ct);

        if (!_embeddingCache.TryGetValue(skill.Meta.Id, out var skillEmb))
        {
            return new ComponentScore(ComponentName, 0, IsEligible: true, "no embedding cached for skill");
        }

        var similarity = CosineSimilarity(queryEmbedding, skillEmb);
        // Normalize: threshold maps to 0, 1.0 maps to 1.0
        var normalized = Math.Clamp((similarity - _similarityThreshold) / (1.0 - _similarityThreshold), 0, 1);

        return new ComponentScore(
            ComponentName,
            normalized,
            IsEligible: true,
            $"similarity={similarity:F3}, threshold={_similarityThreshold:F3}");
    }

    /// <summary>
    ///     Invalidate the embedding cache (call after skill creation/update).
    /// </summary>
    public void InvalidateCache()
    {
        _cachedSkillIds = null;
        _embeddingCache.Clear();
    }

    public Task RefreshCacheIfNeeded(CancellationToken ct)
    {
        var allSkills = _skills.All();
        var currentIds = allSkills.Select(s => s.Meta.Id).OrderBy(id => id).ToList();

        if (_cachedSkillIds is not null && _cachedSkillIds.SequenceEqual(currentIds))
        {
            return Task.CompletedTask;
        }

        _embeddingCache.Clear();
        foreach (Skill skill in allSkills)
        {
            var text = $"{skill.Meta.Name} {skill.Meta.Description} {string.Join(" ", skill.Meta.PhraseReceivers)}";
            var emb = _embedder.EmbedAsync(text, ct).GetAwaiter().GetResult();
            _embeddingCache[skill.Meta.Id] = emb;
        }

        _cachedSkillIds = currentIds;
        return Task.CompletedTask;
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        var denom = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denom > 0 ? dot / denom : 0;
    }

    private static bool IsZeroVector(float[] v) => v.All(x => x == 0);
}
